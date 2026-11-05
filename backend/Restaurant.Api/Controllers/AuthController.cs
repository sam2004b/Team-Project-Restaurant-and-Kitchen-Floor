
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Administration;
using Restaurant.Api.Models;
using Restaurant.Api.Security;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<IdentityUser> _users;
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly RestaurantDbContext _db;

    public AuthController(
        UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn,
        RestaurantDbContext db)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
    }

    // ==========================================
    // 1. STAFF LOGIN
    // ==========================================

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        // Find the Identity user.
        var user = await _users.FindByNameAsync(
            request.Username!);

        if (user is null || !user.EmailConfirmed)
        {
            return Unauthorized(new
            {
                error = "Invalid credentials or inactive account."
            });
        }

        // Verify password and lockout status.
        var result = await _signIn.CheckPasswordSignInAsync(
            user,
            request.Password!,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new
            {
                error = "Invalid credentials or inactive account."
            });
        }

        // Do not bypass two-factor authentication.
        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return StatusCode(403, new
            {
                error = "MFA login flow is not implemented."
            });
        }

        // Find active restaurant memberships.
        var memberships = await _db.StaffMemberships
            .AsNoTracking()
            .Where(m =>
                m.UserId == user.Id &&
                m.Status == MembershipStatus.Active)
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.RestaurantId,
                m.Role
            })
            .ToListAsync(ct);

        if (memberships.Count == 0)
        {
            return StatusCode(403, new
            {
                error = "No active restaurant membership."
            });
        }

        // Select the requested restaurant.
        var selected = request.RestaurantId.HasValue
            ? memberships.SingleOrDefault(
                m => m.RestaurantId ==
                     request.RestaurantId.Value)
            : memberships.Count == 1
                ? memberships[0]
                : null;

        if (selected is null)
        {
            if (request.RestaurantId.HasValue)
            {
                return StatusCode(403, new
                {
                    error = "No active membership in that restaurant."
                });
            }

            return Conflict(new
            {
                error = "Multiple restaurants. Provide restaurantId.",
                restaurants = memberships.Select(m => new
                {
                    m.RestaurantId,
                    m.Role
                })
            });
        }

        // ==========================================
        // 2. CREATE DATABASE-BACKED SESSION
        // ==========================================

        var session = new StaffSession
        {
            UserId = user.Id,
            RestaurantId = selected.RestaurantId,
            StaffMembershipId = selected.Id,
            SecurityStamp = user.SecurityStamp,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        _db.StaffSessions.Add(session);

        await _db.SaveChangesAsync(ct);

        // ==========================================
        // 3. CREATE AUTHENTICATED PRINCIPAL
        // ==========================================

        var principal =
            await _signIn.CreateUserPrincipalAsync(user);

        var identity = (ClaimsIdentity)principal.Identity!;

        // Remove old global Identity role claims.
        foreach (var roleClaim in
            identity.FindAll(identity.RoleClaimType).ToArray())
        {
            identity.RemoveClaim(roleClaim);
        }

        // Add the active restaurant-specific role.
        identity.AddClaim(
            new Claim(
                identity.RoleClaimType,
                selected.Role));

        // Add the session identifier.
        identity.AddClaim(
            new Claim(
                StaffSessionClaims.SessionId,
                session.Id.ToString()));

        // Add the restaurant identifier.
        identity.AddClaim(
            new Claim(
                StaffSessionClaims.RestaurantId,
                selected.RestaurantId.ToString()));

        // Add the staff membership identifier.
        identity.AddClaim(
            new Claim(
                StaffSessionClaims.MembershipId,
                selected.Id.ToString()));

        // Add the current restaurant role.
        identity.AddClaim(
            new Claim(
                StaffSessionClaims.RestaurantRole,
                selected.Role));

        // ==========================================
        // 4. ISSUE BEARER TOKEN
        // ==========================================

        await HttpContext.SignInAsync(
            IdentityConstants.BearerScheme,
            principal);

        return new EmptyResult();
    }

    // ==========================================
    // 5. GET CURRENT STAFF IDENTITY
    // ==========================================

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _users.GetUserAsync(User);

        if (user is null)
            return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.UserName,
            user.Email,

            restaurantId = User.FindFirstValue(
                StaffSessionClaims.RestaurantId),

            membershipId = User.FindFirstValue(
                StaffSessionClaims.MembershipId),

            role = User.FindFirstValue(
                StaffSessionClaims.RestaurantRole)
        });
    }

    // ==========================================
    // 6. LOGOUT AND REVOKE SESSION
    // ==========================================

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken ct)
    {
        var sessionIdText = User.FindFirstValue(
            StaffSessionClaims.SessionId);

        var userId = _users.GetUserId(User);

        if (!Guid.TryParse(
                sessionIdText,
                out var sessionId) ||
            userId is null)
        {
            return Unauthorized();
        }

        var session = await _db.StaffSessions
            .SingleOrDefaultAsync(
                s => s.Id == sessionId &&
                     s.UserId == userId,
                ct);

        if (session is null)
            return Unauthorized();

        session.RevokedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
