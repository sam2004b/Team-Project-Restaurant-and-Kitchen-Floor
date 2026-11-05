using System.Security.Claims;

namespace Restaurant.Api.Security;

/// <summary>
/// Enforces the restaurant chosen for this authenticated session.
/// A user may belong to multiple restaurants but may act in only the
/// one selected at login until they establish a different session.
/// </summary>
public static class StaffSessionScope
{
    public static bool Matches(ClaimsPrincipal principal, int restaurantId)
    {
        return restaurantId > 0 &&
            principal.Identity?.IsAuthenticated == true &&
            int.TryParse(principal.FindFirstValue(
                StaffSessionClaims.RestaurantId), out int current) &&
            current == restaurantId;
    }
}
