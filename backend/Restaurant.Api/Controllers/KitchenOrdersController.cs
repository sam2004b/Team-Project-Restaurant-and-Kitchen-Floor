using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Controllers;

/// <summary>
/// Durable Chef-only kitchen feed. Every GET reads committed Sent orders from
/// PostgreSQL, so reconnected clients can recover orders missed while offline.
/// </summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.ChefOnly)]
[Route("chef/orders")]
public sealed class KitchenOrdersController : ControllerBase
{
    private readonly RestaurantAccessService _access;
    private readonly OrderDetailsQueryService _orders;

    public KitchenOrdersController(
        RestaurantAccessService access,
        OrderDetailsQueryService orders)
    {
        _access = access;
        _orders = orders;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        return Ok(await _orders.GetKitchenFeedAsync(restaurantId, ct));
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Details(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });

        var (restaurantId, error) = await GetAuthorizedRestaurantAsync(ct);
        if (error is not null) return error;
        var order = await _orders.GetKitchenOrderAsync(restaurantId, orderId, ct);
        return order is null ? NotFound() : Ok(order);
    }

    private async Task<(int RestaurantId, IActionResult? Error)>
        GetAuthorizedRestaurantAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(StaffSessionClaims.RestaurantId),
                out int restaurantId) || restaurantId <= 0)
            return (0, Unauthorized());

        if (!await _access.CanChangeDishAvailabilityAsync(User, restaurantId, ct))
            return (0, Forbid());

        return (restaurantId, null);
    }
}
