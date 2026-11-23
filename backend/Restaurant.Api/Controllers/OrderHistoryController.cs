
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Security;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Controllers;

[ApiController]
[Authorize]
[Route("order-history")]
public sealed class OrderHistoryController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly OrderHistoryQueryService _history;

    public OrderHistoryController(
        RestaurantDbContext db,
        OrderHistoryQueryService history)
    {
        _db = db;
        _history = history;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] int? tableNumber = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new
            {
                error = "Page must be positive and pageSize must be between 1 and 100."
            });

        if (tableNumber.HasValue && tableNumber.Value <= 0)
            return BadRequest(new { error = "Table number must be positive." });

        if (fromUtc.HasValue && toUtc.HasValue &&
            fromUtc.Value > toUtc.Value)
            return BadRequest(new
            {
                error = "fromUtc cannot be later than toUtc."
            });

        var context = await GetStaffContextAsync(ct);

        if (context.Error is not null)
            return context.Error;

        var result = await _history.SearchAsync(
            context.RestaurantId,
            context.MembershipId,
            context.IsAdministrator,
            page,
            pageSize,
            tableNumber,
            status,
            fromUtc,
            toUtc,
            search,
            ct);

        return Ok(result);
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Details(
        int orderId,
        CancellationToken ct)
    {
        if (orderId <= 0)
            return BadRequest(new { error = "Invalid order ID." });

        var context = await GetStaffContextAsync(ct);

        if (context.Error is not null)
            return context.Error;

        var result = await _history.GetDetailsAsync(
            context.RestaurantId,
            context.MembershipId,
            context.IsAdministrator,
            orderId,
            ct);

        return result is null
            ? NotFound(new { error = "Order not found or not accessible." })
            : Ok(result);
    }

    private async Task<(
        int RestaurantId,
        int MembershipId,
        bool IsAdministrator,
        IActionResult? Error)> GetStaffContextAsync(
            CancellationToken ct)
    {
        if (!int.TryParse(
                User.FindFirstValue(StaffSessionClaims.RestaurantId),
                out var restaurantId) ||
            restaurantId <= 0 ||
            !int.TryParse(
                User.FindFirstValue(StaffSessionClaims.MembershipId),
                out var membershipId) ||
            membershipId <= 0)
        {
            return (0, 0, false, Unauthorized());
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return (0, 0, false, Unauthorized());

        var membership = await _db.StaffMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(m =>
                m.Id == membershipId &&
                m.RestaurantId == restaurantId &&
                m.UserId == userId &&
                m.Status == MembershipStatus.Active,
                ct);

        if (membership is null)
            return (0, 0, false, Forbid());

        var isAdministrator =
            membership.Role == StaffRoleNames.Administrator;

        var isWaiter =
            membership.Role == StaffRoleNames.Waiter;

        if (!isAdministrator && !isWaiter)
            return (0, 0, false, Forbid());

        return (
            restaurantId,
            membershipId,
            isAdministrator,
            null);
    }
}
