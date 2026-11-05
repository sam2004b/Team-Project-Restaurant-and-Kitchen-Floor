using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Controllers;

/// <summary>Waiter-only view of the current employee's assigned tables.</summary>
[ApiController]
[Authorize(Policy = StaffRolePolicies.WaiterOnly)]
[Route("waiter/tables")]
public sealed class WaiterTablesController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly RestaurantAccessService _access;

    public WaiterTablesController(
        RestaurantDbContext db,
        RestaurantAccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(
                StaffSessionClaims.RestaurantId), out int restaurantId) ||
            !int.TryParse(User.FindFirstValue(
                StaffSessionClaims.MembershipId), out int membershipId))
            return Unauthorized();

        if (!await _access.CanSelectDishesAsync(User, restaurantId, ct))
            return Forbid();

        var tables = await _db.TableStaffAssignments.AsNoTracking()
            .Where(a => a.RestaurantId == restaurantId &&
                        a.StaffMembershipId == membershipId &&
                        a.DiningTable.IsActive)
            .OrderBy(a => a.DiningTable.TableNumber)
            .Select(a => new
            {
                id = a.DiningTableId,
                a.DiningTable.TableNumber,
                a.DiningTable.SeatCount
            })
            .ToListAsync(ct);

        return Ok(tables);
    }
}
