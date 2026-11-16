using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;

namespace Restaurant.Api.Security;

public sealed record PlatformAdminRestaurantContext(
    int RestaurantId,
    int MembershipId,
    string Role
);

public sealed class PlatformAdminSessionValidator
{
    private readonly RestaurantDbContext _db;

    public PlatformAdminSessionValidator(
        RestaurantDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsValidAsync(
        Guid sessionId,
        string userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        return await _db.PlatformAdminSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.Id == sessionId &&
                s.UserId == userId &&
                s.RevokedAtUtc == null &&
                s.ExpiresAtUtc > now &&
                s.SecurityStamp == s.User.SecurityStamp &&
                s.User.EmailConfirmed &&
                (s.User.LockoutEnd == null ||
                 s.User.LockoutEnd <= nowOffset),
                ct);
    }

    public async Task<PlatformAdminRestaurantContext?>
        SelectRestaurantAsync(
            string userId,
            int restaurantId,
            CancellationToken ct = default)
    {
        if (restaurantId <= 0)
            return null;

        var restaurantExists = await _db.Restaurants
            .AnyAsync(r => r.Id == restaurantId, ct);

        if (!restaurantExists)
            return null;

        var membership = await _db.StaffMemberships
            .SingleOrDefaultAsync(m =>
                m.RestaurantId == restaurantId &&
                m.UserId == userId,
                ct);

        if (membership == null)
        {
            membership = new StaffMembership
            {
                RestaurantId = restaurantId,
                UserId = userId,
                Role = StaffRoleNames.Administrator,
                Status = MembershipStatus.Active
            };

            _db.StaffMemberships.Add(membership);
        }
        else
        {
            // The Global Admin must have an active
            // Administrator membership in this context.
            membership.Role = StaffRoleNames.Administrator;
            membership.Status = MembershipStatus.Active;
        }

        await _db.SaveChangesAsync(ct);

        return new PlatformAdminRestaurantContext(
            restaurantId,
            membership.Id,
            StaffRoleNames.Administrator
        );
    }
}
