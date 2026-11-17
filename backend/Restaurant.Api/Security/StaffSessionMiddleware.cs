using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Restaurant.Api.Security;

namespace Restaurant.Api.Security;

public sealed class StaffSessionMiddleware
{
    private readonly RequestDelegate _next;

    public StaffSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        StaffSessionValidator staffValidator,
        PlatformAdminSessionValidator platformValidator)
    {
        if (string.Equals(
                context.Request.Path.Value,
                "/auth/login",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                context.Request.Path.Value,
                "/invitations/accept",
                StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            await context.ChallengeAsync(
                IdentityConstants.BearerScheme);
            return;
        }

        // ==========================================
        // GLOBAL ADMIN SESSION
        // ==========================================

        if (context.User.HasClaim(
                StaffSessionClaims.PlatformAdmin,
                "true"))
        {
            var sessionText = context.User.FindFirstValue(
                StaffSessionClaims.PlatformAdminSessionId);

            if (!Guid.TryParse(sessionText, out var sessionId))
            {
                await context.ChallengeAsync(
                    IdentityConstants.BearerScheme);
                return;
            }

            var valid = await platformValidator.IsValidAsync(
                sessionId,
                userId,
                context.RequestAborted);

            if (!valid)
            {
                await context.ChallengeAsync(
                    IdentityConstants.BearerScheme);
                return;
            }

            var identity = (ClaimsIdentity)context.User.Identity!;

            ClearRestaurantClaims(identity);

            // Global Admin can use global endpoints
            // without selecting a restaurant.
            var restaurantHeader =
                context.Request.Headers["X-Restaurant-Id"]
                    .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(restaurantHeader))
            {
                if (!int.TryParse(
                        restaurantHeader,
                        out var restaurantId) ||
                    restaurantId <= 0)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "Invalid X-Restaurant-Id." });
                    return;
                }

                var selected =
                    await platformValidator.SelectRestaurantAsync(
                        userId,
                        restaurantId,
                        context.RequestAborted);

                if (selected == null)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "Restaurant not found." });
                    return;
                }

                identity.AddClaim(new Claim(
                    identity.RoleClaimType,
                    selected.Role));

                identity.AddClaim(new Claim(
                    StaffSessionClaims.RestaurantId,
                    selected.RestaurantId.ToString()));

                identity.AddClaim(new Claim(
                    StaffSessionClaims.MembershipId,
                    selected.MembershipId.ToString()));

                identity.AddClaim(new Claim(
                    StaffSessionClaims.RestaurantRole,
                    selected.Role));
            }

            await _next(context);
            return;
        }

        // ==========================================
        // NORMAL RESTAURANT STAFF SESSION
        // ==========================================

        var staffSessionText = context.User.FindFirstValue(
            StaffSessionClaims.SessionId);

        if (!Guid.TryParse(
                staffSessionText,
                out var staffSessionId))
        {
            await context.ChallengeAsync(
                IdentityConstants.BearerScheme);
            return;
        }

        var current = await staffValidator.GetCurrentAsync(
            staffSessionId,
            userId,
            context.RequestAborted);

        if (current == null)
        {
            await context.ChallengeAsync(
                IdentityConstants.BearerScheme);
            return;
        }

        var staffIdentity =
            (ClaimsIdentity)context.User.Identity!;

        ClearRestaurantClaims(staffIdentity);

        staffIdentity.AddClaim(new Claim(
            staffIdentity.RoleClaimType,
            current.Role));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.RestaurantId,
            current.RestaurantId.ToString()));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.MembershipId,
            current.StaffMembershipId.ToString()));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.RestaurantRole,
            current.Role));

        await _next(context);
    }

    private static void ClearRestaurantClaims(
        ClaimsIdentity identity)
    {
        foreach (var claim in
            identity.FindAll(identity.RoleClaimType).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        foreach (var type in new[]
        {
            StaffSessionClaims.RestaurantId,
            StaffSessionClaims.MembershipId,
            StaffSessionClaims.RestaurantRole
        })
        {
            foreach (var claim in
                identity.FindAll(type).ToArray())
            {
                identity.RemoveClaim(claim);
            }
        }
    }
}
