using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Security;

/// <summary>
/// Authorizes access to legacy full-dish management
/// using current restaurant Administrator membership.
/// </summary>
public sealed class MenuAdminGuard
    : IAsyncAuthorizationFilter
{
    private readonly RestaurantAccessService _access;
    private readonly IConfiguration _configuration;

    public MenuAdminGuard(
        RestaurantAccessService access,
        IConfiguration configuration)
    {
        _access = access;
        _configuration = configuration;
    }

    public async Task OnAuthorizationAsync(
        AuthorizationFilterContext context)
    {
        // Authentication is required.
        if (context.HttpContext.User.Identity
            ?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Determine which restaurant owns the
        // current legacy single-restaurant menu.
        int restaurantId =
            _configuration.GetValue<int>(
                "Menu:RestaurantId");

        if (restaurantId <= 0)
        {
            context.Result = new ObjectResult(new
            {
                error = "Menu restaurant is not configured."
            })
            {
                StatusCode = 503
            };

            return;
        }

        // Only an active Administrator may perform
        // full dish-management operations.
        bool allowed = await _access.IsActiveAdminAsync(
            context.HttpContext.User,
            restaurantId,
            context.HttpContext.RequestAborted);

        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}