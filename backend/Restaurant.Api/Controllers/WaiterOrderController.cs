using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Waiter;

namespace Restaurant.Api.Controllers;

/// <summary>Read-only order lists scoped to the current waiter's assignments.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter/orders")]
public sealed class WaiterOrdersController : ControllerBase
{
    private readonly RestaurantAccessService _access;
    private readonly WaiterTableQueryService _tables;

    public WaiterOrdersController(
        RestaurantAccessService access,
        WaiterTableQueryService tables)
    {
        _access = access;
        _tables = tables;
    }

    // Returns all order headers for currently assigned, active tables.
    [HttpGet]
    public async Task<IActionResult> GetOrders(CancellationToken ct)
    {
        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var orders = await _tables.GetAssignedOrdersAsync(
            restaurantId, membershipId, ct);

        return Ok(orders);
    }

    // Direct object access is protected by the database assignment join.
    // Both missing and unauthorized order IDs return 404, avoiding leaks.
    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetOrder(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });

        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var order = await _tables.GetVisibleOrderAsync(
            restaurantId, membershipId, orderId, ct);

        return order is null ? NotFound() : Ok(order);
    }

    private bool TryGetStaffContext(out int restaurantId, out int membershipId)
    {
        bool hasRestaurant = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.RestaurantId),
            out restaurantId);
        bool hasMembership = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.MembershipId),
            out membershipId);

        return hasRestaurant && restaurantId > 0 &&
               hasMembership && membershipId > 0;
    }
}
