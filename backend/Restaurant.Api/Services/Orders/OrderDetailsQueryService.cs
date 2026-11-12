using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Orders;
using Restaurant.Api.Models;

namespace Restaurant.Api.Services.Orders;

/// <summary>
/// Read-only projections for waiter order composer and durable kitchen feed.
/// Restaurant and membership come only from validated staff-session claims.
/// </summary>
public sealed class OrderDetailsQueryService
{
    private readonly RestaurantDbContext _db;

    public OrderDetailsQueryService(RestaurantDbContext db) => _db = db;

    public async Task<OrderDetailsDto?> GetForWaiterAsync(
        int restaurantId, int membershipId, int orderId,
        CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o =>
                o.RestaurantId == restaurantId &&
                o.Id == orderId &&
                _db.TableStaffAssignments.Any(a =>
                    a.RestaurantId == restaurantId &&
                    a.DiningTableId == o.DiningTableId &&
                    a.StaffMembershipId == membershipId &&
                    a.DiningTable.IsActive &&
                    a.StaffMembership.RestaurantId == restaurantId &&
                    a.StaffMembership.Role == StaffRoleNames.Waiter &&
                    a.StaffMembership.Status == MembershipStatus.Active))
            .SingleOrDefaultAsync(ct);

        return order is null ? null : Project(order);
    }

    public async Task<List<OrderDetailsDto>> GetKitchenFeedAsync(
        int restaurantId, CancellationToken ct = default)
    {
        // Backwards-compatible order feed: show submitted lines only, never
        // pending additions. Closed orders remain visible to the kitchen.
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId &&
                        o.Items.Any(i => i.KitchenSubmissionId != null))
            .Include(o => o.Items)
            .OrderBy(o => o.SentAtUtc)
            .ThenBy(o => o.Id).ToListAsync(ct);

        return orders.Select(o => Project(o, submittedOnly: true)).ToList();
    }

    public async Task<OrderDetailsDto?> GetKitchenOrderAsync(
        int restaurantId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o =>
                o.RestaurantId == restaurantId &&
                o.Id == orderId &&
                o.Items.Any(i => i.KitchenSubmissionId != null), ct);
        return order is null ? null : Project(order, submittedOnly: true);
    }

    public async Task<List<KitchenSubmissionDto>> GetKitchenSubmissionsAsync(
        int restaurantId, int? orderId = null, CancellationToken ct = default)
    {
        var submissions = await _db.KitchenSubmissions.AsNoTracking()
            .Where(s => s.Order.RestaurantId == restaurantId &&
                        (!orderId.HasValue || s.OrderId == orderId.Value))
            .Include(s => s.Order)
            .Include(s => s.Items)
            .OrderBy(s => s.SentAtUtc)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        return submissions.Select(s => new KitchenSubmissionDto(
            s.Id, s.OrderId, s.Order.DiningTableId, s.SentAtUtc,
            s.Items.OrderBy(i => i.Id).Select(ProjectItem).ToList(),
            s.Order.SeatNumber
        )).ToList();
    }

    public async Task<List<KitchenSubmissionDto>?> GetForWaiterSubmissionsAsync(
        int restaurantId, int membershipId, int orderId,
        CancellationToken ct = default)
    {
        // Reuse the same membership and current-table-assignment check as Details.
        if (await GetForWaiterAsync(restaurantId, membershipId, orderId, ct) is null)
            return null;
        return await GetKitchenSubmissionsAsync(restaurantId, orderId, ct);
    }

    private static OrderItemDetailsDto ProjectItem(RestaurantOrderItem i) =>
        new(i.Id, i.DishId, i.DishNameSnapshot, i.UnitPriceSnapshot,
            i.Quantity, i.SpecialRequest, i.KitchenSubmissionId);

    private static OrderDetailsDto Project(RestaurantOrder order,
        bool submittedOnly = false) =>
        new(
            order.Id,
            order.RestaurantId,
            order.DiningTableId,
            order.CreatedByStaffMembershipId,
            order.Status,
            order.CreatedAtUtc,
            order.SentAtUtc,
            order.Items.Where(i => !submittedOnly || i.KitchenSubmissionId != null)
                .OrderBy(i => i.Id).Select(ProjectItem).ToList(),
            order.PaidAtUtc,
            order.SeatNumber);
}
