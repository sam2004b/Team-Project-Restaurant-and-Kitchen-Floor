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
        // Retrieve ALL persisted Sent orders so a reconnecting kitchen client
        // can reconcile missed notifications without relying on a push socket.
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId &&
                        o.Status == RestaurantOrderStates.Sent)
            .Include(o => o.Items)
            .OrderBy(o => o.SentAtUtc)
            .ThenBy(o => o.Id)
            .ToListAsync(ct);

        return orders.Select(Project).ToList();
    }

    public async Task<OrderDetailsDto?> GetKitchenOrderAsync(
        int restaurantId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o =>
                o.RestaurantId == restaurantId &&
                o.Id == orderId &&
                o.Status == RestaurantOrderStates.Sent, ct);

        return order is null ? null : Project(order);
    }

    private static OrderDetailsDto Project(RestaurantOrder order) =>
        new(
            order.Id,
            order.RestaurantId,
            order.DiningTableId,
            order.CreatedByStaffMembershipId,
            order.Status,
            order.CreatedAtUtc,
            order.SentAtUtc,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDetailsDto(
                    i.Id,
                    i.DishId,
                    i.DishNameSnapshot,
                    i.UnitPriceSnapshot,
                    i.Quantity,
                    i.SpecialRequest))
                .ToList());
}
