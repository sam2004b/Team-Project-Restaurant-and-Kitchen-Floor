using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Waiter;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Controllers;

/// <summary>Waiter-only access to current table assignments and their orders.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter/tables")]
public sealed class WaiterTablesController : ControllerBase
{
    private readonly RestaurantAccessService _access;
    private readonly WaiterTableQueryService _tables;

    public WaiterTablesController(
        RestaurantAccessService access,
        WaiterTableQueryService tables)
    {
        _access = access;
        _tables = tables;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var tables = await _tables.GetAssignedTablesAsync(
            restaurantId, membershipId, ct);

        return Ok(tables);
    }

    // Direct table access: return 404 for other waiters' or restaurants' tables.
    [HttpGet("{tableId:int}")]
    public async Task<IActionResult> GetTable(
        int tableId,
        CancellationToken ct)
    {
        if (tableId <= 0)
            return BadRequest(new { error = "Invalid table ID." });

        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var table = await _tables.GetAssignedTableAsync(
            restaurantId, membershipId, tableId, ct);

        return table is null ? NotFound() : Ok(table);
    }

    // Read-only order headers. Order creation is a separate Sprint 2 task.
    [HttpGet("{tableId:int}/orders")]
    public async Task<IActionResult> GetTableOrders(
        int tableId,
        CancellationToken ct)
    {
        if (tableId <= 0)
            return BadRequest(new { error = "Invalid table ID." });

        if (!TryGetStaffContext(out int restaurantId, out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var orders = await _tables.GetVisibleOrdersAsync(
            restaurantId, membershipId, tableId, ct);

        // 404 avoids disclosing tables belonging to other staff/restaurants.
        return orders is null ? NotFound() : Ok(orders);
    }

    private bool TryGetStaffContext(out int restaurantId, out int membershipId)
    {
        bool validRestaurant = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.RestaurantId),
            out restaurantId);
        bool validMembership = int.TryParse(
            User.FindFirstValue(StaffSessionClaims.MembershipId),
            out membershipId);

        return validRestaurant && restaurantId > 0 &&
               validMembership && membershipId > 0;
    }
}
