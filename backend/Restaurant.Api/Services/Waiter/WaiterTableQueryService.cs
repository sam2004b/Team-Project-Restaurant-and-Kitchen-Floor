using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using System.Text.Json.Serialization;

namespace Restaurant.Api.Services.Waiter;

// Existing response formats remain unchanged for frontend compatibility.
public sealed record AssignedTableView(int Id, int TableNumber, int SeatCount);

public sealed record TableOrderView(
    int Id,
    int DiningTableId,
    DateTime CreatedAtUtc,
    string Status = RestaurantOrderStates.Legacy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SeatNumber = null);

/// <summary>
/// Read-only queries scoped to the restaurant and membership from a
/// validated staff session. Each query checks CURRENT table assignments.
/// </summary>
public sealed class WaiterTableQueryService
{
    private readonly RestaurantDbContext _db;

    public WaiterTableQueryService(RestaurantDbContext db) => _db = db;

    private IQueryable<TableStaffAssignment> CurrentAssignments(
        int restaurantId,
        int membershipId)
    {
        return _db.TableStaffAssignments.AsNoTracking()
            .Where(a =>
                a.RestaurantId == restaurantId &&
                a.StaffMembershipId == membershipId &&
                a.StaffMembership.RestaurantId == restaurantId &&
                a.StaffMembership.Status == MembershipStatus.Active &&
                a.StaffMembership.Role == StaffRoleNames.Waiter &&
                a.DiningTable.RestaurantId == restaurantId &&
                a.DiningTable.IsActive);
    }

    // Join orders with CURRENT assignments in the SAME database query.
    // This prevents guessed order IDs from bypassing assignment filtering.
    private IQueryable<RestaurantOrder> CurrentOrders(
        int restaurantId,
        int membershipId)
    {
        return from order in _db.Orders.AsNoTracking()
               join assignment in CurrentAssignments(restaurantId, membershipId)
                   on new { order.RestaurantId, order.DiningTableId }
                   equals new { assignment.RestaurantId, assignment.DiningTableId }
               where order.RestaurantId == restaurantId
               select order;
    }

    public Task<List<AssignedTableView>> GetAssignedTablesAsync(
        int restaurantId,
        int membershipId,
        CancellationToken ct = default)
    {
        return CurrentAssignments(restaurantId, membershipId)
            .OrderBy(a => a.DiningTable.TableNumber)
            .ThenBy(a => a.DiningTableId)
            .Select(a => new AssignedTableView(
                a.DiningTableId,
                a.DiningTable.TableNumber,
                a.DiningTable.SeatCount))
            .ToListAsync(ct);
    }

    public Task<AssignedTableView?> GetAssignedTableAsync(
        int restaurantId,
        int membershipId,
        int tableId,
        CancellationToken ct = default)
    {
        return CurrentAssignments(restaurantId, membershipId)
            .Where(a => a.DiningTableId == tableId)
            .Select(a => new AssignedTableView(
                a.DiningTableId,
                a.DiningTable.TableNumber,
                a.DiningTable.SeatCount))
            .SingleOrDefaultAsync(ct);
    }

    // Existing per-table endpoint: 404 for an unassigned/inactive table,
    // 200 with [] when currently assigned but no orders exist.
    public async Task<List<TableOrderView>?> GetVisibleOrdersAsync(
        int restaurantId,
        int membershipId,
        int tableId,
        CancellationToken ct = default)
    {
        bool assigned = await CurrentAssignments(restaurantId, membershipId)
            .AnyAsync(a => a.DiningTableId == tableId, ct);

        if (!assigned)
            return null;

        // Repeat the assignment restriction as part of the order query,
        // including when reassignment happens between the two queries.
        return await CurrentOrders(restaurantId, membershipId)
            .Where(o => o.DiningTableId == tableId)
            .OrderBy(o => o.CreatedAtUtc)
            .ThenBy(o => o.Id)
            .Select(o => new TableOrderView(
                o.Id,
                o.DiningTableId,
                o.CreatedAtUtc,
                o.Status,
                o.SeatNumber))
            .ToListAsync(ct);
    }

    // NEW: all orders for tables assigned to the current waiter.
    public Task<List<TableOrderView>> GetAssignedOrdersAsync(
        int restaurantId,
        int membershipId,
        CancellationToken ct = default)
    {
        return CurrentOrders(restaurantId, membershipId)
            .OrderBy(o => o.CreatedAtUtc)
            .ThenBy(o => o.Id)
            .Select(o => new TableOrderView(
                o.Id,
                o.DiningTableId,
                o.CreatedAtUtc,
                o.Status,
                o.SeatNumber))
            .ToListAsync(ct);
    }

    // NEW: direct order lookup. A missing OR unassigned order returns null.
    public Task<TableOrderView?> GetVisibleOrderAsync(
        int restaurantId,
        int membershipId,
        int orderId,
        CancellationToken ct = default)
    {
        return CurrentOrders(restaurantId, membershipId)
            .Where(o => o.Id == orderId)
            .Select(o => new TableOrderView(
                o.Id,
                o.DiningTableId,
                o.CreatedAtUtc,
                o.Status,
                o.SeatNumber))
            .SingleOrDefaultAsync(ct);
    }
}
