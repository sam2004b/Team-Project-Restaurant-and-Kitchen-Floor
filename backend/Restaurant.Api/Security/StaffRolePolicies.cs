using Microsoft.AspNetCore.Authorization;
using Restaurant.Api.Models;

namespace Restaurant.Api.Security;

/// <summary>Dedicated policies for staff-only resources.</summary>
public static class StaffRolePolicies
{
    public const string WaiterOnly = "WaiterOnly";
    public const string ChefOnly = "ChefOnly";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(WaiterOnly, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole(StaffRoleNames.Waiter));

        options.AddPolicy(ChefOnly, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole(StaffRoleNames.Chef));
    }
}
