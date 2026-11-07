using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.Api.Models;
using Restaurant.Api.Security;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class StaffRoleAuthorizationTests
{
    private static ClaimsPrincipal Staff(string role, int restaurantId = 1)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "employee-id"),
            new Claim(ClaimTypes.Role, role),
            new Claim(StaffSessionClaims.RestaurantId, restaurantId.ToString()),
            new Claim(StaffSessionClaims.MembershipId, "10")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData(StaffRoleNames.Waiter, true, false)]
    [InlineData(StaffRoleNames.Chef, false, true)]
    [InlineData(StaffRoleNames.Administrator, false, false)]
    [InlineData(StaffRoleNames.ReceptionManager, false, false)]
    public async Task Policies_EnforceRoleSeparation(
        string role, bool canWait, bool canCook)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(StaffRolePolicies.Configure);
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = Staff(role);

        var waiter = await authorization.AuthorizeAsync(
            principal, null, StaffRolePolicies.WaiterOnly);
        var chef = await authorization.AuthorizeAsync(
            principal, null, StaffRolePolicies.ChefOnly);

        Assert.Equal(canWait, waiter.Succeeded);
        Assert.Equal(canCook, chef.Succeeded);
    }

    [Fact]
    public void SessionScope_RejectsOtherRestaurant()
    {
        var principal = Staff(StaffRoleNames.Waiter, restaurantId: 21);
        Assert.True(StaffSessionScope.Matches(principal, 21));
        Assert.False(StaffSessionScope.Matches(principal, 8));
    }

    [Fact]
    public void SessionScope_RejectsUnauthenticatedUser()
    {
        Assert.False(StaffSessionScope.Matches(new ClaimsPrincipal(), 21));
    }
}
