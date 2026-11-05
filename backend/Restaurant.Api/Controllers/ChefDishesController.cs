using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Controllers;

/// <summary>
/// Chef-only view of current kitchen dish availability.
/// Order tickets are not implemented in this project yet.
/// </summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.ChefOnly)]
[Route("chef/dishes")]
public sealed class ChefDishesController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly RestaurantAccessService _access;
    private readonly IConfiguration _configuration;

    public ChefDishesController(
        RestaurantDbContext db,
        RestaurantAccessService access,
        IConfiguration configuration)
    {
        _db = db;
        _access = access;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        int menuRestaurantId = _configuration.GetValue<int>("Menu:RestaurantId");
        if (menuRestaurantId <= 0)
            return Problem(title: "Menu restaurant is not configured.", statusCode: 503);

        // Both current session scope and active Chef membership are required.
        if (!await _access.CanChangeDishAvailabilityAsync(
                User, menuRestaurantId, ct))
            return Forbid();

        // Read the existing global menu, including unavailable dishes.
        // This is valid only for the configured legacy-menu restaurant.
        var dishes = await _db.Dishes.AsNoTracking()
            .OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Category,
                d.IsAvailable
            })
            .ToListAsync(ct);

        return Ok(dishes);
    }
}
