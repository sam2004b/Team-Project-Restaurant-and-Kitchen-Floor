using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Dtos.Administration;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<IdentityUser> _users;
    private readonly SignInManager<IdentityUser> _signIn;

    public AuthController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _users.FindByNameAsync(request.Username!);
        if (user is null)
            return Unauthorized(new { error = "Invalid credentials." });

        var result = await _signIn.CheckPasswordSignInAsync(
            user, request.Password!, lockoutOnFailure: true);

        if (!result.Succeeded)
            return Unauthorized(new { error = "Invalid credentials or account unavailable." });

        // CheckPasswordSignInAsync only verifies a password; it does not finish MFA.
        if (await _users.GetTwoFactorEnabledAsync(user))
            return StatusCode(403, new { error = "MFA login flow is not implemented." });

        var principal = await _signIn.CreateUserPrincipalAsync(user);
        await HttpContext.SignInAsync(IdentityConstants.BearerScheme, principal);
        return new EmptyResult(); // Bearer handler writes accessToken + expiry.
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(new
        {
            user.Id,
            user.UserName,
            user.Email
        });
    }
}
