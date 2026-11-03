using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Services.Administration;
using Restaurant.Api.Services.Dishes;

namespace Restaurant.Api.Controllers;

public sealed record SetDishAvailabilityRequest(
    bool? IsAvailable
);

[ApiController]
[Authorize]
[Route("dishes")]
public sealed class DishAvailabilityController
    : ControllerBase
{
    private readonly IDishAvailabilityService _availability;
    private readonly RestaurantAccessService _access;
    private readonly IConfiguration _configuration;

    public DishAvailabilityController(
        IDishAvailabilityService availability,
        RestaurantAccessService access,
        IConfiguration configuration)
    {
        _availability = availability;
        _access = access;
        _configuration = configuration;
    }

    [HttpPatch("{dishId:int}/availability")]
    public async Task<IActionResult> UpdateAvailability(
        int dishId,
        [FromBody] SetDishAvailabilityRequest request,
        CancellationToken ct)
    {
        // 1. Validate dish ID.
        if (dishId <= 0)
        {
            return BadRequest(new
            {
                error = "Invalid dish ID."
            });
        }

        // 2. Identify the owner of the current menu.
        int restaurantId = _configuration.GetValue<int>(
            "Menu:RestaurantId");

        if (restaurantId <= 0)
        {
            return Problem(
                title: "Menu restaurant is not configured.",
                statusCode: 503);
        }

        // 3. Only an active Administrator or Chef
        // of this restaurant may update availability.
        bool allowed =
            await _access.CanChangeDishAvailabilityAsync(
                User,
                restaurantId,
                ct);

        if (!allowed)
            return Forbid();

        // 4. Require an explicit Boolean.
        if (request.IsAvailable is null)
        {
            return BadRequest(new
            {
                error = "isAvailable is required."
            });
        }

        // 5. Reuse Oberd's existing service.
        var result =
            await _availability.SetAvailabilityAsync(
                dishId,
                request.IsAvailable.Value,
                ct);

        if (result == AvailabilityUpdateResult.NotFound)
        {
            return NotFound(new
            {
                error = "Dish not found."
            });
        }

        // 6. Report whether the state changed.
        return Ok(new
        {
            dishId,
            isAvailable = request.IsAvailable.Value,
            changed =
                result == AvailabilityUpdateResult.Updated
        });
    }
}