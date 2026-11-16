
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
    // LOGIN
    // ==========================================

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(
            request.Username!);

        if (user == null || !user.EmailConfirmed)
        {
            return Unauthorized(new
            {
                error = "Invalid credentials or inactive account."
            });
        }

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

        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return StatusCode(403, new
            {
                error = "MFA login flow is not implemented."
            });
        }

        // ==========================================
        // GLOBAL ADMIN LOGIN
        // ==========================================

        var userClaims = await _users.GetClaimsAsync(user);

        var isPlatformAdmin = userClaims.Any(c =>
            c.Type == "permission" &&
            c.Value == "restaurants:create");

        if (isPlatformAdmin)
        {
            var platformSession = new PlatformAdminSession
            {
                UserId = user.Id,
                SecurityStamp = user.SecurityStamp,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            };

            _db.PlatformAdminSessions.Add(platformSession);

            await _db.SaveChangesAsync(ct);

            var principal =
                await _signIn.CreateUserPrincipalAsync(user);

            var identity = (ClaimsIdentity)principal.Identity!;

            // Do not inherit restaurant-specific roles
            // into the global administrator session.
            foreach (var claim in
                identity.FindAll(identity.RoleClaimType).ToArray())
            {
                identity.RemoveClaim(claim);
            }

            identity.AddClaim(new Claim(
                StaffSessionClaims.PlatformAdmin,
                "true"));

            identity.AddClaim(new Claim(
                StaffSessionClaims.PlatformAdminSessionId,
                platformSession.Id.ToString()));

            await HttpContext.SignInAsync(
                IdentityConstants.BearerScheme,
                principal);

            return new EmptyResult();
        }

        // ==========================================
        // NORMAL RESTAURANT STAFF LOGIN
        // ==========================================

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
            return Unauthorized(new
            {
                error = "No active restaurant membership."
            });
        }

        var selected = request.RestaurantId.HasValue
            ? memberships.SingleOrDefault(m =>
                m.RestaurantId == request.RestaurantId.Value)
            : memberships.Count == 1
                ? memberships[0]
                : null;

        if (selected == null)
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

        var staffPrincipal =
            await _signIn.CreateUserPrincipalAsync(user);

        var staffIdentity =
            (ClaimsIdentity)staffPrincipal.Identity!;

        foreach (var roleClaim in
            staffIdentity.FindAll(
                staffIdentity.RoleClaimType).ToArray())
        {
            staffIdentity.RemoveClaim(roleClaim);
        }

        staffIdentity.AddClaim(new Claim(
            staffIdentity.RoleClaimType,
            selected.Role));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.SessionId,
            session.Id.ToString()));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.RestaurantId,
            selected.RestaurantId.ToString()));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.MembershipId,
            selected.Id.ToString()));

        staffIdentity.AddClaim(new Claim(
            StaffSessionClaims.RestaurantRole,
            selected.Role));

        await HttpContext.SignInAsync(
            IdentityConstants.BearerScheme,
            staffPrincipal);

        return new EmptyResult();
    }

    // ==========================================
    // CURRENT USER
    // ==========================================

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _users.GetUserAsync(User);

        if (user == null)
            return Unauthorized();

        var isPlatformAdmin =
            User.HasClaim(
                StaffSessionClaims.PlatformAdmin,
                "true");

        return Ok(new
        {
            user.Id,
            user.UserName,
            user.Email,

            isPlatformAdmin,

            restaurantId = User.FindFirstValue(
                StaffSessionClaims.RestaurantId),

            membershipId = User.FindFirstValue(
                StaffSessionClaims.MembershipId),

            role = isPlatformAdmin
                ? "PlatformAdmin"
                : User.FindFirstValue(
                    StaffSessionClaims.RestaurantRole)
        });
    }

    // ==========================================
    // SELECT RESTAURANT CONTEXT
    // ==========================================

    [Authorize]
    [HttpPost("select-restaurant/{restaurantId:int}")]
    public async Task<IActionResult> SelectRestaurant(
        int restaurantId,
        CancellationToken ct)
    {
        if (!User.HasClaim(
                StaffSessionClaims.PlatformAdmin,
                "true"))
        {
            return Forbid();
        }

        // Restaurant selection is applied per request
        // through the X-Restaurant-Id header.
        var exists = await _db.Restaurants
            .AnyAsync(r => r.Id == restaurantId, ct);

        if (!exists)
        {
            return NotFound(new
            {
                error = "Restaurant not found."
            });
        }

        return Ok(new
        {
            restaurantId,
            message = "Restaurant selected. Send X-Restaurant-Id with subsequent requests."
        });
    }

    // ==========================================
    // LOGOUT
    // ==========================================

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken ct)
    {
        var userId = _users.GetUserId(User);

        if (userId == null)
            return Unauthorized();

        // Global Admin logout
        if (User.HasClaim(
                StaffSessionClaims.PlatformAdmin,
                "true"))
        {
            var platformSessionText =
                User.FindFirstValue(
                    StaffSessionClaims.PlatformAdminSessionId);

            if (!Guid.TryParse(
                    platformSessionText,
                    out var platformSessionId))
            {
                return Unauthorized();
            }

            var platformSession =
                await _db.PlatformAdminSessions
                    .SingleOrDefaultAsync(s =>
                        s.Id == platformSessionId &&
                        s.UserId == userId,
                        ct);

            if (platformSession == null)
                return Unauthorized();

            platformSession.RevokedAtUtc =
                DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        // Normal staff logout
        var sessionText = User.FindFirstValue(
            StaffSessionClaims.SessionId);

        if (!Guid.TryParse(sessionText, out var sessionId))
            return Unauthorized();

        var session = await _db.StaffSessions
            .SingleOrDefaultAsync(s =>
                s.Id == sessionId &&
                s.UserId == userId,
                ct);

        if (session == null)
            return Unauthorized();

        session.RevokedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
