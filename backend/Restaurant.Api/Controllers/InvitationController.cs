using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Administration;
using Restaurant.Api.Models;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("invitations")]
public sealed class InvitationsController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public InvitationsController(RestaurantDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    [AllowAnonymous]
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(
        [FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
            return BadRequest(new { error = "Invalid invitation token." });

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        // Row lock makes acceptance single-use even under concurrent requests.
        var rows = await _db.StaffInvitations.FromSqlInterpolated(
            $"SELECT * FROM staff_invitations WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .ToListAsync(ct);
        var invitation = rows.SingleOrDefault();

        if (invitation is null || invitation.Status != InvitationStatus.Pending ||
            invitation.ExpiresAtUtc is null || invitation.ExpiresAtUtc <= DateTime.UtcNow)
            return BadRequest(new { error = "Invitation is invalid, expired, or already used." });

        IdentityUser? user;
        if (User.Identity?.IsAuthenticated == true)
        {
            user = await _users.GetUserAsync(User);
            if (user is null || !user.EmailConfirmed ||
                _users.NormalizeEmail(user.Email) != invitation.NormalizedEmail)
                return Forbid();
        }
        else
        {
            user = await _users.FindByEmailAsync(invitation.Email);
            if (user is not null)
                return Unauthorized(new { error = "Sign in with the invited account first." });

            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Username and password are required for a new employee." });

            // Token possession proves email control ONLY when the token was
            // delivered securely to the invited email address.
            user = new IdentityUser
            {
                UserName = request.Username.Trim(),
                Email = invitation.Email,
                EmailConfirmed = true
            };
            var created = await _users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
                return BadRequest(new
                {
                    errors = created.Errors.Select(e => e.Description).ToArray()
                });
        }

        var membership = await _db.StaffMemberships.SingleOrDefaultAsync(
            m => m.RestaurantId == invitation.RestaurantId && m.UserId == user.Id, ct);
        if (membership is null)
        {
            membership = new StaffMembership
            {
                RestaurantId = invitation.RestaurantId,
                UserId = user.Id,
                Role = invitation.Role,
                Status = MembershipStatus.Active
            };
            _db.StaffMemberships.Add(membership);
        }
        else if (membership.Status == MembershipStatus.Revoked)
        {
            // An explicit new invitation from an admin permits reactivation.
            membership.Status = MembershipStatus.Active;
            membership.Role = invitation.Role;
        }
        else
        {
            return Conflict(new { error = "Employee is already active." });
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAtUtc = DateTime.UtcNow;
        invitation.TokenHash = null;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new
        {
            membership.Id,
            membership.RestaurantId,
            membership.UserId,
            membership.Role,
            membership.Status
        });
    }
}
