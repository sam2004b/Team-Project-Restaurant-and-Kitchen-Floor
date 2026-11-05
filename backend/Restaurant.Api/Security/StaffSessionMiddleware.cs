using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Restaurant.Api.Security;

/// <summary>
/// Validates the current staff session on EVERY authenticated API request.
/// Also refreshes restaurant/role claims from the database on every request.
/// </summary>
public sealed class StaffSessionMiddleware
{
    private readonly RequestDelegate _next;

    public StaffSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        StaffSessionValidator validator)
    {
        // Let users sign in again even if an old bearer token is still attached.
        if (string.Equals(context.Request.Path.Value, "/auth/login", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.Request.Path.Value, "/invitations/accept", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessionIdText = context.User.FindFirstValue(StaffSessionClaims.SessionId);

            if (string.IsNullOrWhiteSpace(userId) ||
                !Guid.TryParse(sessionIdText, out var sessionId))
            {
                await context.ChallengeAsync(IdentityConstants.BearerScheme);
                return;
            }

            var current = await validator.GetCurrentAsync(
                sessionId, userId, context.RequestAborted);

            if (current is null)
            {
                await context.ChallengeAsync(IdentityConstants.BearerScheme);
                return;
            }

            var identity = (ClaimsIdentity)context.User.Identity;

            // Remove stale/global Identity role claims from this request.
            foreach (var claim in identity.FindAll(identity.RoleClaimType).ToArray())
                identity.RemoveClaim(claim);
            foreach (var type in new[] {
                StaffSessionClaims.RestaurantId,
                StaffSessionClaims.MembershipId,
                StaffSessionClaims.RestaurantRole })
            {
                foreach (var claim in identity.FindAll(type).ToArray())
                    identity.RemoveClaim(claim);
            }

            identity.AddClaim(new Claim(identity.RoleClaimType, current.Role));
            identity.AddClaim(new Claim(
                StaffSessionClaims.RestaurantId,
                current.RestaurantId.ToString()));
            identity.AddClaim(new Claim(
                StaffSessionClaims.MembershipId,
                current.StaffMembershipId.ToString()));
            identity.AddClaim(new Claim(
                StaffSessionClaims.RestaurantRole,
                current.Role));
        }

        await _next(context);
    }
}
