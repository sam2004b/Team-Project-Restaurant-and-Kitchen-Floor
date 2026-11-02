using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;

namespace Restaurant.Api.Services.Administration;

public sealed class RestaurantAccessService
{
    private readonly RestaurantDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public RestaurantAccessService(RestaurantDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    public Task<bool> IsActiveAdminAsync(
        ClaimsPrincipal principal, int restaurantId, CancellationToken ct = default)
    {
        var userId = _users.GetUserId(principal);
        if (userId is null || restaurantId <= 0)
            return Task.FromResult(false);

        return _db.StaffMemberships.AnyAsync(m =>
            m.RestaurantId == restaurantId &&
            m.UserId == userId &&
            m.Role == StaffRoleNames.Administrator &&
            m.Status == MembershipStatus.Active, ct);
    }

    // Serialize membership mutations for one restaurant so two concurrent requests
    // cannot remove/demote its last administrator.
    public async Task<bool> LockRestaurantAsync(int restaurantId, CancellationToken ct = default)
    {
        var rows = await _db.Restaurants.FromSqlInterpolated(
            $"SELECT * FROM restaurants WHERE \"Id\" = {restaurantId} FOR UPDATE")
            .ToListAsync(ct);
        return rows.Count == 1;
    }
}

public static class RestaurantRoles
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        StaffRoleNames.Administrator,
        StaffRoleNames.Waiter,
        StaffRoleNames.Chef,
        StaffRoleNames.ReceptionManager
    };

    public static bool IsSupported(string? role) =>
        role is not null && Supported.Contains(role);
}
