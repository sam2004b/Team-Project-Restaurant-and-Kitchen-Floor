using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Administration;
using Restaurant.Api.Models;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("restaurants")]
[Authorize]
public sealed class RestaurantsController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public RestaurantsController(RestaurantDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    // Bootstrap privilege is separate from restaurant-scoped Administrator membership.
    [HttpPost]
    [Authorize(Policy = "RestaurantCreator")]
    public async Task<IActionResult> Create(
        [FromBody] CreateRestaurantRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var address = request.Address?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 ||
            string.IsNullOrWhiteSpace(address) || address.Length > 300)
            return BadRequest(new { error = "Valid name and address are required." });

        var userId = _users.GetUserId(User);
        if (userId is null)
            return Unauthorized();

        var restaurant = new RestaurantAccount
        {
            Name = name,
            Address = address
        };
        _db.Restaurants.Add(restaurant);
        _db.StaffMemberships.Add(new StaffMembership
        {
            Restaurant = restaurant,
            UserId = userId,
            Role = StaffRoleNames.Administrator,
            Status = MembershipStatus.Active
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation &&
            pg.ConstraintName == "IX_restaurants_NormalizedName")
        {
            return Conflict(new { error = "Restaurant name already exists." });
        }

        return StatusCode(201, new
        {
            restaurant.Id,
            restaurant.Name,
            restaurant.Address
        });
    }
}
