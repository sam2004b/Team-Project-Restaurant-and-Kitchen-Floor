using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;

namespace Restaurant.Api.Security;

public sealed record CurrentStaffContext(
    int RestaurantId,
    int StaffMembershipId,
    string Role);

/// <summary>
/// Checks the server-side session, current membership and Identity account.
/// A token's role claim is never treated as the final authority.
/// </summary>
public sealed class StaffSessionValidator
{
    private readonly RestaurantDbContext _db;

    public StaffSessionValidator(RestaurantDbContext db) => _db = db;

    public Task<CurrentStaffContext?> GetCurrentAsync(
        Guid sessionId, string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        return _db.StaffSessions.AsNoTracking()
            .Where(s => s.Id == sessionId &&
                        s.UserId == userId &&
                        s.RevokedAtUtc == null &&
                        s.ExpiresAtUtc > now &&
                        s.User.EmailConfirmed &&
                        (s.User.LockoutEnd == null ||
                         s.User.LockoutEnd <= nowOffset) &&
                        s.SecurityStamp == s.User.SecurityStamp &&
                        s.StaffMembership.UserId == userId &&
                        s.StaffMembership.RestaurantId == s.RestaurantId &&
                        s.StaffMembership.Status == MembershipStatus.Active)
            .Select(s => new CurrentStaffContext(
                s.RestaurantId,
                s.StaffMembershipId,
                s.StaffMembership.Role))
            .SingleOrDefaultAsync(ct);
    }
}
