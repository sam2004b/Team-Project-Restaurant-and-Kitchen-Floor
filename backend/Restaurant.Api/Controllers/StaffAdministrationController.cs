using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Administration;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Controllers;

[ApiController]
[Authorize]
[Route("restaurants/{restaurantId:int}")]
public sealed class StaffAdministrationController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly RestaurantAccessService _access;
    private readonly InvitationDeliveryService _delivery;
    private readonly UserManager<IdentityUser> _users;

    public StaffAdministrationController(
        RestaurantDbContext db,
        RestaurantAccessService access,
        InvitationDeliveryService delivery,
        UserManager<IdentityUser> users)
    {
        _db = db;
        _access = access;
        _delivery = delivery;
        _users = users;
    }

    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff(int restaurantId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct)) return Forbid();

        var members = await _db.StaffMemberships.AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.RestaurantId,
                m.UserId,
                m.User.UserName,
                m.User.Email,
                m.Role,
                m.Status
            }).ToListAsync(ct);
        return Ok(members);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> Invite(
        int restaurantId, [FromBody] InviteStaffRequest request, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct)) return Forbid();

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 ||
            !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { error = "Invalid email address." });
        if (!RestaurantRoles.IsSupported(request.Role))
            return BadRequest(new { error = "Unsupported role." });

        var normalized = _users.NormalizeEmail(email);
        var existingUser = await _users.FindByEmailAsync(email);
        if (existingUser is not null && await _db.StaffMemberships.AnyAsync(
                m => m.RestaurantId == restaurantId && m.UserId == existingUser.Id &&
                     m.Status == MembershipStatus.Active, ct))
            return Conflict(new { error = "Employee is already active in this restaurant." });

        if (await _db.StaffInvitations.AnyAsync(i =>
                i.RestaurantId == restaurantId &&
                i.NormalizedEmail == normalized &&
                i.Status == InvitationStatus.Pending, ct))
            return Conflict(new { error = "A pending invitation already exists." });

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var invitation = new StaffInvitation
        {
            RestaurantId = restaurantId,
            Email = email,
            Role = request.Role!,
            Status = InvitationStatus.Pending,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(2)
        };
        _db.StaffInvitations.Add(invitation);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { error = "Pending invitation already exists." });
        }

        try
        {
            await _delivery.SendAsync(email, token, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Mail.SmtpException)
        {
            // Prevent an undeliverable invitation from blocking a later retry.
            invitation.Status = InvitationStatus.Revoked;
            invitation.TokenHash = null;
            await _db.SaveChangesAsync(ct);
            return StatusCode(503, new { error = "Invitation delivery unavailable. Retry later." });
        }

        return StatusCode(201, new
        {
            invitation.Id,
            invitation.RestaurantId,
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.ExpiresAtUtc,
            // Only shown when the operator explicitly enables local dev testing.
            developmentToken = _delivery.DevelopmentTokenResponseEnabled ? token : null
        });
    }

    // Administrators can cancel a pending invitation (including an old
    // S2-US5-T01 seed invitation without a token) and issue a fresh one.
    [HttpPatch("invitations/{invitationId:int}/revoke")]
    public async Task<IActionResult> RevokeInvitation(
        int restaurantId, int invitationId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct)) return Forbid();
        var invitation = await _db.StaffInvitations.SingleOrDefaultAsync(
            i => i.RestaurantId == restaurantId && i.Id == invitationId, ct);
        if (invitation is null) return NotFound();
        if (invitation.Status != InvitationStatus.Pending)
            return Conflict(new { error = "Only pending invitations can be revoked." });

        invitation.Status = InvitationStatus.Revoked;
        invitation.TokenHash = null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { invitation.Id, invitation.RestaurantId, invitation.Status });
    }

    [HttpPatch("staff/{memberId:int}/role")]
    public Task<IActionResult> ChangeRole(
        int restaurantId, int memberId, [FromBody] ChangeStaffRoleRequest request,
        CancellationToken ct) =>
        ChangeMembershipAsync(restaurantId, memberId, request.Role, null, ct);

    [HttpPatch("staff/{memberId:int}/activate")]
    public Task<IActionResult> Activate(int restaurantId, int memberId, CancellationToken ct) =>
        ChangeMembershipAsync(restaurantId, memberId, null, MembershipStatus.Active, ct);

    [HttpPatch("staff/{memberId:int}/revoke")]
    public Task<IActionResult> Revoke(int restaurantId, int memberId, CancellationToken ct) =>
        ChangeMembershipAsync(restaurantId, memberId, null, MembershipStatus.Revoked, ct);

    private async Task<IActionResult> ChangeMembershipAsync(
        int restaurantId, int memberId, string? newRole,
        MembershipStatus? newStatus, CancellationToken ct)
    {
        if (newRole is not null && !RestaurantRoles.IsSupported(newRole))
            return BadRequest(new { error = "Unsupported role." });

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct)) return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct)) return Forbid();

        var membership = await _db.StaffMemberships.SingleOrDefaultAsync(
            m => m.RestaurantId == restaurantId && m.Id == memberId, ct);
        if (membership is null) return NotFound();

        if (newRole is not null && membership.Status != MembershipStatus.Active)
            return Conflict(new { error = "Cannot change the role of a revoked membership." });

        var effectiveRole = newRole ?? membership.Role;
        var effectiveStatus = newStatus ?? membership.Status;
        var removingAdmin =
            membership.Role == StaffRoleNames.Administrator &&
            membership.Status == MembershipStatus.Active &&
            (effectiveRole != StaffRoleNames.Administrator ||
             effectiveStatus != MembershipStatus.Active);

        if (removingAdmin)
        {
            var activeAdminCount = await _db.StaffMemberships.CountAsync(
                m => m.RestaurantId == restaurantId &&
                     m.Role == StaffRoleNames.Administrator &&
                     m.Status == MembershipStatus.Active, ct);
            if (activeAdminCount <= 1)
                return Conflict(new { error = "Cannot remove the last active administrator." });
        }

        membership.Role = effectiveRole;
        membership.Status = effectiveStatus;
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
