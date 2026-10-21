using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Dtos;
using Restaurant.Api.Services.Menu;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("dishes")]
public sealed class DishesController : ControllerBase
{
    private readonly IMenuReadService _menuReadService;

    public DishesController(
        IMenuReadService menuReadService)
    {
        _menuReadService = menuReadService;
    }

    // GET /dishes
    // GET /dishes?limit=10
    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<MenuDishDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MenuDishDto>>>
        GetAvailableDishes(
            [FromQuery] int? limit,
            CancellationToken cancellationToken)
    {
        // Validate the optional limit parameter
        if (limit is < 1 or > 100)
        {
            return BadRequest(new
            {
                error = "limit must be between 1 and 100."
            });
        }

        // Reuse Oberd's existing menu service
        var dishes = await _menuReadService
            .GetAvailableDishesAsync(cancellationToken);

        IReadOnlyList<MenuDishDto> result =
            limit.HasValue
                ? dishes.Take(limit.Value).ToArray()
                : dishes;

        return Ok(result);
    }
}