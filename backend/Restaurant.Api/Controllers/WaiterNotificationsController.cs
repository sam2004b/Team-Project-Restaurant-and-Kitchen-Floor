using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Controllers;

/// <summary>Waiter-only ready-dish inbox and per-dish served action.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter")]
public sealed class WaiterNotificationsController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly RestaurantAccessService _access;

    public WaiterNotificationsController(
        RestaurantDbContext db,
        RestaurantAccessService access)
    {
        _db = db;
        _access = access;
    }

    /// <summary>Returns ready-dish notifications for the current waiter.</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken ct)
    {
        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var notifications = await _db.WaiterNotifications
            .AsNoTracking()
            .Where(n => n.RestaurantId == restaurantId &&
                        n.WaiterStaffMembershipId == membershipId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Select(n => new
            {
                n.Id,
                n.OrderId,
                n.OrderItemId,
                n.CreatedAtUtc,
                isRead = n.ReadAtUtc != null,
                readAtUtc = n.ReadAtUtc,
                dishName = n.OrderItem.DishNameSnapshot,
                quantity = n.OrderItem.Quantity,
                tableNumber = n.Order.DiningTable.TableNumber,
                n.DiningTableId,
                preparationStatus = n.OrderItem.PreparationStatus,
                serviceStatus = n.OrderItem.ServiceStatus
            })
            .ToListAsync(ct);

        return Ok(notifications);
    }

    /// <summary>Marks one ready dish line as served by its currently assigned waiter.</summary>
    [HttpPatch("orders/items/{orderItemId:int}/served")]
    public async Task<IActionResult> MarkServed(
        int orderItemId,
        CancellationToken ct)
    {
        if (orderItemId <= 0)
            return BadRequest(new { error = "Invalid order item ID." });

        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var item = await _db.OrderItems
            .Include(i => i.Order)
            .ThenInclude(o => o.DiningTable)
            .SingleOrDefaultAsync(i =>
                i.Id == orderItemId &&
                i.Order.RestaurantId == restaurantId &&
                i.KitchenSubmissionId != null, ct);

        if (item is null)
            return NotFound();

        // Check CURRENT assignment in the same restaurant. Historical
        // notification ownership alone must not grant access after reassignment.
        bool currentlyAssigned = await _db.TableStaffAssignments.AnyAsync(a =>
            a.RestaurantId == restaurantId &&
            a.DiningTableId == item.Order.DiningTableId &&
            a.StaffMembershipId == membershipId &&
            a.StaffMembership.RestaurantId == restaurantId &&
            a.StaffMembership.Status == MembershipStatus.Active &&
            a.StaffMembership.Role == StaffRoleNames.Waiter &&
            a.DiningTable.RestaurantId == restaurantId &&
            a.DiningTable.IsActive, ct);

        if (!currentlyAssigned)
            return NotFound();

        if (item.PreparationStatus != DishPreparationStates.Ready)
            return Conflict(new { error = "Only ready dishes can be marked as served." });

        // Idempotent: repeating the request does not change the original timestamp.
        if (item.ServiceStatus != DishServiceStates.Served)
        {
            item.ServiceStatus = DishServiceStates.Served;
            item.ServedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            item.Id,
            item.OrderId,
            item.DishId,
            dishName = item.DishNameSnapshot,
            tableNumber = item.Order.DiningTable.TableNumber,
            serviceStatus = item.ServiceStatus,
            item.ServedAtUtc
        });
    }

    private bool TryGetStaffContext(out int restaurantId, out int membershipId)
    {
        bool hasRestaurant = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.RestaurantId), out restaurantId);
        bool hasMembership = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.MembershipId), out membershipId);

        return hasRestaurant && restaurantId > 0 &&
               hasMembership && membershipId > 0;
    }
}
