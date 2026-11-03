using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Menu;

namespace Restaurant.Api.Controllers;

[ApiController]
[Authorize]
[Route("waiter/dishes")]
public sealed class WaiterMenuController
    : ControllerBase
{
    private readonly IMenuReadService _menu;
    private readonly RestaurantAccessService _access;
    private readonly IConfiguration _configuration;

    public WaiterMenuController(
        IMenuReadService menu,
        RestaurantAccessService access,
        IConfiguration configuration)
    {
        _menu = menu;
        _access = access;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableDishes(
        CancellationToken ct)
    {
        int restaurantId =
            _configuration.GetValue<int>(
                "Menu:RestaurantId");

        if (restaurantId <= 0)
        {
            return Problem(
                title: "Menu restaurant is not configured.",
                statusCode: 503);
        }

        bool allowed =
            await _access.CanSelectDishesAsync(
                User,
                restaurantId,
                ct);

        if (!allowed)
            return Forbid();

        // Reuse the exact same menu service
        // that already serves customers.
        var dishes =
            await _menu.GetAvailableDishesAsync(ct);

        return Ok(dishes);
    }
}