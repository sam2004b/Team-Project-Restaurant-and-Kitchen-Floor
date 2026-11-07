using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Restaurant.Api.Data;
using Restaurant.Api.Models;

namespace Restaurant.Api.Services.Orders;

public enum OrderWriteStatus
{
    Created,
    ExistingOpen,
    ItemAdded,
    Sent,
    AlreadySent,
    NotAccessible,
    InvalidItem,
    DishUnavailable,
    OrderNotOpen,
    EmptyOrder
}

public sealed record OrderWriteResult(
    OrderWriteStatus Status,
    int? OrderId = null,
    int? ItemId = null,
    string? Error = null);

/// <summary>
/// Persistence layer for waiter order drafts. The API in a later task should
/// obtain restaurant/membership IDs from validated staff-session claims.
/// </summary>
public sealed class OrderDraftPersistenceService
{
    private const string OpenOrderIndex = "UX_orders_one_open_per_table";
    private readonly RestaurantDbContext _db;

    public OrderDraftPersistenceService(RestaurantDbContext db) => _db = db;

    /// <summary>
    /// Return the existing Open order, or create exactly one for an assigned
    /// active table. PostgreSQL row locking serializes this with reassignment
    /// and competing order writers; a partial unique index is the final guard.
    /// </summary>
    public async Task<OrderWriteResult> GetOrCreateOpenAsync(
        int restaurantId,
        int waiterMembershipId,
        int tableId,
        CancellationToken ct = default)
    {
        await using var tx = await LockRestaurantAsync(restaurantId, ct);

        if (!await HasCurrentAssignmentAsync(
                restaurantId, waiterMembershipId, tableId, ct))
            return new(OrderWriteStatus.NotAccessible);

        var existing = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o =>
                o.RestaurantId == restaurantId &&
                o.DiningTableId == tableId &&
                o.Status == RestaurantOrderStates.Open, ct);

        if (existing is not null)
        {
            await CommitAsync(tx, ct);
            return new(OrderWriteStatus.ExistingOpen, existing.Id);
        }

        var order = new RestaurantOrder
        {
            RestaurantId = restaurantId,
            DiningTableId = tableId,
            CreatedByStaffMembershipId = waiterMembershipId,
            Status = RestaurantOrderStates.Open,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Orders.Add(order);

        try
        {
            await _db.SaveChangesAsync(ct);
            await CommitAsync(tx, ct);
            return new(OrderWriteStatus.Created, order.Id);
        }
        catch (DbUpdateException ex) when (IsOpenOrderUniqueViolation(ex))
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            // Another transaction won the race. Fetch its persisted order.
            var winner = await _db.Orders.AsNoTracking()
                .SingleAsync(o =>
                    o.RestaurantId == restaurantId &&
                    o.DiningTableId == tableId &&
                    o.Status == RestaurantOrderStates.Open, ct);
            return new(OrderWriteStatus.ExistingOpen, winner.Id);
        }
    }

    /// <summary>Add a line to an Open order only while the table is assigned.</summary>
    public async Task<OrderWriteResult> AddItemAsync(
        int restaurantId,
        int waiterMembershipId,
        int orderId,
        int dishId,
        int quantity,
        string? specialRequest,
        CancellationToken ct = default)
    {
        if (orderId <= 0 || dishId <= 0 || quantity <= 0 ||
            specialRequest?.Length > 500)
            return new(OrderWriteStatus.InvalidItem,
                Error: "Valid IDs, positive quantity and at most 500 request characters are required.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);
        var order = await GetAccessibleOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);

        if (order is null)
            return new(OrderWriteStatus.NotAccessible);
        if (order.Status != RestaurantOrderStates.Open)
            return new(OrderWriteStatus.OrderNotOpen, order.Id);

        var dish = await GetDishWithReadLockAsync(dishId, ct);
        if (dish is null || !dish.IsAvailable)
            return new(OrderWriteStatus.DishUnavailable, order.Id);

        var item = new RestaurantOrderItem
        {
            OrderId = order.Id,
            DishId = dish.Id,
            Quantity = quantity,
            SpecialRequest = string.IsNullOrWhiteSpace(specialRequest)
                ? null : specialRequest.Trim(),
            DishNameSnapshot = dish.Name,
            UnitPriceSnapshot = dish.Price,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<RestaurantOrderItem>().Add(item);
        await _db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new(OrderWriteStatus.ItemAdded, order.Id, item.Id);
    }

    /// <summary>Durably change a non-empty Open order to Sent at most once.</summary>
    public async Task<OrderWriteResult> SendAsync(
        int restaurantId,
        int waiterMembershipId,
        int orderId,
        CancellationToken ct = default)
    {
        await using var tx = await LockRestaurantAsync(restaurantId, ct);
        var order = await GetAccessibleOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);

        if (order is null)
            return new(OrderWriteStatus.NotAccessible);
        if (order.Status == RestaurantOrderStates.Sent)
        {
            await CommitAsync(tx, ct);
            return new(OrderWriteStatus.AlreadySent, order.Id);
        }
        if (order.Status != RestaurantOrderStates.Open)
            return new(OrderWriteStatus.OrderNotOpen, order.Id);

        var dishIds = await _db.Set<RestaurantOrderItem>()
            .Where(i => i.OrderId == orderId)
            .Select(i => i.DishId).Distinct().OrderBy(id => id)
            .ToListAsync(ct);

        if (dishIds.Count == 0)
            return new(OrderWriteStatus.EmptyOrder, order.Id);

        // Revalidate the current menu at send time, not just when selected.
        // FOR SHARE prevents a concurrent dish UPDATE from racing ahead of
        // the commit on PostgreSQL, ensuring a consistent send decision.
        foreach (var dishId in dishIds)
        {
            var dish = await GetDishWithReadLockAsync(dishId, ct);
            if (dish is null || !dish.IsAvailable)
                return new(OrderWriteStatus.DishUnavailable, order.Id);
        }

        order.Status = RestaurantOrderStates.Sent;
        order.SentAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new(OrderWriteStatus.Sent, order.Id);
    }

    private Task<bool> HasCurrentAssignmentAsync(
        int restaurantId, int membershipId, int tableId,
        CancellationToken ct) =>
        _db.TableStaffAssignments.AsNoTracking().AnyAsync(a =>
            a.RestaurantId == restaurantId &&
            a.DiningTableId == tableId &&
            a.StaffMembershipId == membershipId &&
            a.DiningTable.IsActive &&
            a.StaffMembership.RestaurantId == restaurantId &&
            a.StaffMembership.Status == MembershipStatus.Active &&
            a.StaffMembership.Role == StaffRoleNames.Waiter,
            ct);

    private async Task<RestaurantOrder?> GetAccessibleOrderAsync(
        int restaurantId, int membershipId, int orderId,
        CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(o =>
            o.Id == orderId && o.RestaurantId == restaurantId, ct);

        if (order is null || !await HasCurrentAssignmentAsync(
                restaurantId, membershipId, order.DiningTableId, ct))
            return null;

        return order;
    }

    private async Task<Dish?> GetDishWithReadLockAsync(
        int dishId, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            // This application uses PostgreSQL; FOR SHARE is provider-specific.
            var rows = await _db.Dishes.FromSqlInterpolated(
                    $"SELECT * FROM dishes WHERE \"Id\" = {dishId} FOR SHARE")
                .AsNoTracking().ToListAsync(ct);
            return rows.SingleOrDefault();
        }

        return await _db.Dishes.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == dishId, ct);
    }

    private async Task<IDbContextTransaction?> LockRestaurantAsync(
        int restaurantId, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return null; // EF InMemory is only used by unit tests.

        var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.Restaurants.FromSqlInterpolated(
                    $"SELECT * FROM restaurants WHERE \"Id\" = {restaurantId} FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private static Task CommitAsync(
        IDbContextTransaction? tx, CancellationToken ct) =>
        tx is null ? Task.CompletedTask : tx.CommitAsync(ct);

    private static bool IsOpenOrderUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg &&
        pg.SqlState == PostgresErrorCodes.UniqueViolation &&
        pg.ConstraintName == OpenOrderIndex;
}
