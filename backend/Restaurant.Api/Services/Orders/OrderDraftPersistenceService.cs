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
    ItemUpdated,
    ItemRemoved,
    ItemNotFound,
    ItemAlreadySubmitted,
    InvalidSeat,
    SeatRequired,
    AmbiguousActiveOrders,
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
    string? Error = null,
    int? SubmissionId = null);

/// <summary>
/// Persistence layer for waiter order drafts. The API in a later task should
/// obtain restaurant/membership IDs from validated staff-session claims.
/// </summary>
public sealed class OrderDraftPersistenceService
{
    private const string ActiveSeatOrderIndex = "UX_orders_one_active_per_seat";
    private readonly RestaurantDbContext _db;

    public OrderDraftPersistenceService(RestaurantDbContext db) => _db = db;

    /// <summary>
    /// Compatibility entry point for existing frontends that do not send a seat.
    /// Reuses a single unassigned order; if ambiguous, require explicit selection.
    /// New clients should pass a seat number through the seat-specific endpoint.
    /// </summary>
    public Task<OrderWriteResult> GetOrCreateOpenAsync(
        int restaurantId, int waiterMembershipId, int tableId,
        CancellationToken ct = default) =>
        GetOrCreateOpenAsync(restaurantId, waiterMembershipId, tableId,
            seatNumber: null, ct);

    /// <summary>
    /// Reuse the active order for a SPECIFIC seat, or create it.
    /// Restaurant row locking and the unique seat-scoped index prevent races.
    /// An order can be reopened after its kitchen submission until it is paid.
    /// </summary>
    public async Task<OrderWriteResult> GetOrCreateOpenAsync(
        int restaurantId,
        int waiterMembershipId,
        int tableId,
        int? seatNumber,
        CancellationToken ct = default)
    {
        if (seatNumber.HasValue && seatNumber.Value <= 0)
            return new(OrderWriteStatus.InvalidSeat,
                Error: "Seat numbers start at 1.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);

        if (!await HasCurrentAssignmentAsync(
                restaurantId, waiterMembershipId, tableId, ct))
            return new(OrderWriteStatus.NotAccessible);

        var seatCount = await _db.DiningTables.AsNoTracking()
            .Where(t => t.Id == tableId && t.RestaurantId == restaurantId)
            .Select(t => t.SeatCount)
            .SingleAsync(ct);

        if (seatNumber.HasValue && seatNumber.Value > seatCount)
            return new(OrderWriteStatus.InvalidSeat,
                Error: $"Seat {seatNumber} does not exist at this table (1..{seatCount}).");

        var matches = await _db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId &&
                o.DiningTableId == tableId &&
                o.SeatNumber == seatNumber &&
                (o.Status == RestaurantOrderStates.Open ||
                 o.Status == RestaurantOrderStates.Sent))
            .OrderBy(o => o.Id)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count > 1)
            return new(OrderWriteStatus.AmbiguousActiveOrders,
                Error: "Multiple unassigned active orders exist. Select the order ID from the table's order list.");

        if (matches.Count == 1)
        {
            await CommitAsync(tx, ct);
            return new(OrderWriteStatus.ExistingOpen, matches[0].Id);
        }

        // An old client must not silently create an unassigned order when the
        // table is now managed per seat. Ask it to use a seat-specific route.
        if (!seatNumber.HasValue && await _db.Orders.AsNoTracking().AnyAsync(o =>
                o.RestaurantId == restaurantId &&
                o.DiningTableId == tableId &&
                o.SeatNumber != null &&
                (o.Status == RestaurantOrderStates.Open ||
                 o.Status == RestaurantOrderStates.Sent), ct))
            return new(OrderWriteStatus.SeatRequired,
                Error: "Choose a seat or select an existing order from the table list.");

        var order = new RestaurantOrder
        {
            RestaurantId = restaurantId,
            DiningTableId = tableId,
            SeatNumber = seatNumber,
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
        catch (DbUpdateException ex) when (IsSeatOrderUniqueViolation(ex))
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            var winner = await _db.Orders.AsNoTracking().SingleAsync(o =>
                o.RestaurantId == restaurantId &&
                o.DiningTableId == tableId &&
                o.SeatNumber == seatNumber &&
                (o.Status == RestaurantOrderStates.Open ||
                 o.Status == RestaurantOrderStates.Sent), ct);
            return new(OrderWriteStatus.ExistingOpen, winner.Id);
        }
    }

    /// <summary>Add a pending line to any active order while the table is assigned.</summary>
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
        if (!RestaurantOrderStates.IsActive(order.Status))
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

    /// <summary>
    /// Change pending line quantity/notes in an active order.
    /// Already submitted lines are immutable; add a new pending line instead.
    /// </summary>
    public async Task<OrderWriteResult> UpdateItemAsync(
        int restaurantId, int waiterMembershipId, int orderId,
        int itemId, int quantity, string? specialRequest,
        CancellationToken ct = default)
    {
        if (orderId <= 0 || itemId <= 0 || quantity <= 0 ||
            specialRequest?.Length > 500)
            return new(OrderWriteStatus.InvalidItem,
                Error: "Order/item IDs and quantity must be positive; notes max 500 characters.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);
        var order = await GetAccessibleOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        if (order is null)
            return new(OrderWriteStatus.NotAccessible);
        if (!RestaurantOrderStates.IsActive(order.Status))
            return new(OrderWriteStatus.OrderNotOpen, order.Id);

        var item = await _db.OrderItems.SingleOrDefaultAsync(
            i => i.Id == itemId && i.OrderId == orderId, ct);
        if (item is null)
            return new(OrderWriteStatus.ItemNotFound, order.Id);
        if (item.KitchenSubmissionId is not null)
            return new(OrderWriteStatus.ItemAlreadySubmitted, order.Id, item.Id);

        var dish = await GetDishWithReadLockAsync(item.DishId, ct);
        if (dish is null || !dish.IsAvailable)
            return new(OrderWriteStatus.DishUnavailable, order.Id);

        item.Quantity = quantity;
        item.SpecialRequest = string.IsNullOrWhiteSpace(specialRequest)
            ? null : specialRequest.Trim();
        await _db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new(OrderWriteStatus.ItemUpdated, order.Id, item.Id);
    }

    /// <summary>Remove only a pending line from an active order.</summary>
    public async Task<OrderWriteResult> RemoveItemAsync(
        int restaurantId, int waiterMembershipId, int orderId,
        int itemId, CancellationToken ct = default)
    {
        if (orderId <= 0 || itemId <= 0)
            return new(OrderWriteStatus.InvalidItem,
                Error: "Order and item IDs must be positive.");

        await using var tx = await LockRestaurantAsync(restaurantId, ct);
        var order = await GetAccessibleOrderAsync(
            restaurantId, waiterMembershipId, orderId, ct);
        if (order is null)
            return new(OrderWriteStatus.NotAccessible);
        if (!RestaurantOrderStates.IsActive(order.Status))
            return new(OrderWriteStatus.OrderNotOpen, order.Id);

        var item = await _db.OrderItems.SingleOrDefaultAsync(
            i => i.Id == itemId && i.OrderId == orderId, ct);
        if (item is null)
            return new(OrderWriteStatus.ItemNotFound, order.Id);
        if (item.KitchenSubmissionId is not null)
            return new(OrderWriteStatus.ItemAlreadySubmitted, order.Id, item.Id);

        _db.OrderItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new(OrderWriteStatus.ItemRemoved, order.Id, item.Id);
    }

    /// <summary>
    /// Atomically submit ONLY the still-pending lines as a new kitchen batch.
    /// The restaurant row lock serializes concurrent send/edit requests.
    /// Repeating Send without new lines never creates duplicate tickets.
    /// </summary>
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
        if (!RestaurantOrderStates.IsActive(order.Status))
            return new(OrderWriteStatus.OrderNotOpen, order.Id);

        var pending = await _db.OrderItems
            .Where(i => i.OrderId == orderId && i.KitchenSubmissionId == null)
            .OrderBy(i => i.Id).ToListAsync(ct);

        if (pending.Count == 0)
        {
            if (order.Status == RestaurantOrderStates.Open)
                return new(OrderWriteStatus.EmptyOrder, order.Id);

            var lastSubmissionId = await _db.KitchenSubmissions.AsNoTracking()
                .Where(s => s.OrderId == orderId)
                .OrderByDescending(s => s.Id)
                .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
            await CommitAsync(tx, ct);
            return new(OrderWriteStatus.AlreadySent, order.Id,
                SubmissionId: lastSubmissionId);
        }

        // Revalidate only dishes newly submitted. PostgreSQL FOR SHARE prevents
        // concurrent availability changes between validation and commit.
        foreach (var dishId in pending.Select(i => i.DishId).Distinct().OrderBy(id => id))
        {
            var dish = await GetDishWithReadLockAsync(dishId, ct);
            if (dish is null || !dish.IsAvailable)
                return new(OrderWriteStatus.DishUnavailable, order.Id);
        }

        var now = DateTime.UtcNow;
        var submission = new KitchenSubmission
        {
            OrderId = order.Id,
            SentAtUtc = now
        };
        _db.KitchenSubmissions.Add(submission);
        foreach (var item in pending)
            item.KitchenSubmission = submission;

        order.Status = RestaurantOrderStates.Sent;
        order.SentAtUtc ??= now; // retain timestamp of the FIRST send
        await _db.SaveChangesAsync(ct);
        await CommitAsync(tx, ct);
        return new(OrderWriteStatus.Sent, order.Id,
            SubmissionId: submission.Id);
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

    private static bool IsSeatOrderUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg &&
        pg.SqlState == PostgresErrorCodes.UniqueViolation &&
        pg.ConstraintName == ActiveSeatOrderIndex;
}
