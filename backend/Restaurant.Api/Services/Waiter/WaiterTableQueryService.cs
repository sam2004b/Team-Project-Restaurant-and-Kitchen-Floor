using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Waiter;

public sealed record AssignedTableView(int Id, int TableNumber, int SeatCount);

public sealed record TableOrderView(
    int Id,
    int DiningTableId,
    DateTime CreatedAtUtc);

/// <summary>
/// Reads only the tables currently assigned to an active waiter,
/// and order headers associated with those tables in the same restaurant.
/// Callers must obtain restaurantId/membershipId from a validated staff session.
/// </summary>
public sealed class WaiterTableQueryService
{
    private readonly RestaurantDbContext _db;

    public WaiterTableQueryService(RestaurantDbContext db)
    {
        _db = db;
    }

    public Task<List<AssignedTableView>> GetAssignedTablesAsync(
        int restaurantId,
        int membershipId,
        CancellationToken ct = default)
    {
        return _db.TableStaffAssignments
            .AsNoTracking()
            .Where(a =>
                a.RestaurantId == restaurantId &&
                a.StaffMembershipId == membershipId &&
                a.StaffMembership.Status == MembershipStatus.Active &&
                a.StaffMembership.Role == StaffRoleNames.Waiter &&
                a.DiningTable.IsActive)
            .OrderBy(a => a.DiningTable.TableNumber)
            .Select(a => new AssignedTableView(
                a.DiningTableId,
                a.DiningTable.TableNumber,
                a.DiningTable.SeatCount))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns null when the waiter has no CURRENT assignment to this table.
    /// Returns an empty list if assigned but the table has no orders.
    /// </summary>
    public async Task<List<TableOrderView>?> GetVisibleOrdersAsync(
        int restaurantId,
        int membershipId,
        int tableId,
        CancellationToken ct = default)
    {
        var assigned = await _db.TableStaffAssignments
            .AsNoTracking()
            .AnyAsync(a =>
                a.RestaurantId == restaurantId &&
                a.DiningTableId == tableId &&
                a.StaffMembershipId == membershipId &&
                a.StaffMembership.Status == MembershipStatus.Active &&
                a.StaffMembership.Role == StaffRoleNames.Waiter &&
                a.DiningTable.IsActive,
                ct);

        if (!assigned)
            return null;

        return await _db.Orders.AsNoTracking()
            .Where(o =>
                o.RestaurantId == restaurantId &&
                o.DiningTableId == tableId)
            .OrderBy(o => o.CreatedAtUtc)
            .ThenBy(o => o.Id)
            .Select(o => new TableOrderView(
                o.Id,
                o.DiningTableId,
                o.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
