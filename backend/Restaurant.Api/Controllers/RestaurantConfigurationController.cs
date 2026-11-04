using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos.Settings;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Administration;

namespace Restaurant.Api.Controllers;

/// <summary>
/// Administration of a restaurant's profile, settings, dining tables,
/// and current waiter-to-table assignments.
/// All routes are scoped to one restaurant and to its active administrator.
/// </summary>
[ApiController]
[Authorize]
[Route("restaurants/{restaurantId:int}")]
public sealed class RestaurantConfigurationController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly RestaurantAccessService _access;

    public RestaurantConfigurationController(
        RestaurantDbContext db,
        RestaurantAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ---------------- RESTAURANT PROFILE ----------------

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(int restaurantId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var restaurant = await _db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Id, r.Name, r.Address })
            .SingleOrDefaultAsync(ct);

        return restaurant is null ? NotFound() : Ok(restaurant);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        int restaurantId, [FromBody] UpdateRestaurantProfileRequest request,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var address = request.Address?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 ||
            string.IsNullOrWhiteSpace(address) || address.Length > 300)
            return BadRequest(new { error = "Valid restaurant name and address are required." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var restaurant = await _db.Restaurants.SingleAsync(r => r.Id == restaurantId, ct);
        restaurant.Name = name;
        restaurant.Address = address;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex, "IX_restaurants_NormalizedName"))
        {
            return Conflict(new { error = "Restaurant name already exists." });
        }
        await tx.CommitAsync(ct);
        return Ok(new { restaurant.Id, restaurant.Name, restaurant.Address });
    }

    // ---------------- RESTAURANT SETTINGS ----------------

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(int restaurantId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var settings = await _db.RestaurantSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.RestaurantId == restaurantId, ct);

        return settings is null ? NotFound() : Ok(ToSettingsView(settings));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> CreateSettings(
        int restaurantId, [FromBody] SaveRestaurantSettingsRequest request,
        CancellationToken ct)
    {
        if (request.DefaultSeatCapacity is null or <= 0)
            return BadRequest(new { error = "DefaultSeatCapacity must be positive." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        if (await _db.RestaurantSettings.AnyAsync(s => s.RestaurantId == restaurantId, ct))
            return Conflict(new { error = "Settings already exist. Use PUT to update them." });

        var settings = new RestaurantSettings
        {
            RestaurantId = restaurantId,
            DefaultSeatCapacity = request.DefaultSeatCapacity.Value,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettings.Add(settings);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex, "PK_restaurant_settings"))
        {
            return Conflict(new { error = "Restaurant settings already exist." });
        }
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(GetSettings), new { restaurantId }, ToSettingsView(settings));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        int restaurantId, [FromBody] SaveRestaurantSettingsRequest request,
        CancellationToken ct)
    {
        if (request.DefaultSeatCapacity is null or <= 0)
            return BadRequest(new { error = "DefaultSeatCapacity must be positive." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var settings = await _db.RestaurantSettings
            .SingleOrDefaultAsync(s => s.RestaurantId == restaurantId, ct);
        if (settings is null)
            return NotFound(new { error = "Settings not found. Create them first." });

        settings.DefaultSeatCapacity = request.DefaultSeatCapacity.Value;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(ToSettingsView(settings));
    }

    // ---------------- DINING TABLES ----------------

    [HttpGet("tables")]
    public async Task<IActionResult> ListTables(int restaurantId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var tables = await _db.DiningTables.AsNoTracking()
            .Where(t => t.RestaurantId == restaurantId)
            .Include(t => t.StaffAssignment)!
                .ThenInclude(a => a!.StaffMembership)
                    .ThenInclude(m => m.User)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        return Ok(tables.Select(ToTableView).ToList());
    }

    [HttpGet("tables/{tableId:int}")]
    public async Task<IActionResult> GetTable(
        int restaurantId, int tableId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();
        var table = await LoadTableViewAsync(restaurantId, tableId, ct);
        return table is null ? NotFound() : Ok(table);
    }

    [HttpPost("tables")]
    public async Task<IActionResult> CreateTable(
        int restaurantId, [FromBody] CreateDiningTableRequest request,
        CancellationToken ct)
    {
        if (request.TableNumber is null or <= 0 || request.SeatCount is null or <= 0 ||
            request.IsActive is null)
            return BadRequest(new { error = "Positive TableNumber and SeatCount and IsActive are required." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        if (await _db.DiningTables.AnyAsync(t =>
            t.RestaurantId == restaurantId &&
            t.TableNumber == request.TableNumber.Value, ct))
            return Conflict(new { error = "Table number already exists in this restaurant." });

        var table = new DiningTable
        {
            RestaurantId = restaurantId,
            TableNumber = request.TableNumber.Value,
            SeatCount = request.SeatCount.Value,
            IsActive = request.IsActive.Value
        };
        _db.DiningTables.Add(table);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex, "IX_dining_tables_RestaurantId_TableNumber"))
        {
            return Conflict(new { error = "Table number already exists in this restaurant." });
        }
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(GetTable),
            new { restaurantId, tableId = table.Id }, ToTableView(table));
    }

    [HttpPut("tables/{tableId:int}")]
    public async Task<IActionResult> UpdateTable(
        int restaurantId, int tableId, [FromBody] UpdateDiningTableRequest request,
        CancellationToken ct)
    {
        if (request.TableNumber is null or <= 0 || request.SeatCount is null or <= 0 ||
            request.IsActive is null)
            return BadRequest(new { error = "Positive TableNumber and SeatCount and IsActive are required." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var table = await _db.DiningTables.SingleOrDefaultAsync(t =>
            t.RestaurantId == restaurantId && t.Id == tableId, ct);
        if (table is null)
            return NotFound(new { error = "Table not found in this restaurant." });

        if (await _db.DiningTables.AnyAsync(t =>
            t.RestaurantId == restaurantId &&
            t.Id != tableId && t.TableNumber == request.TableNumber.Value, ct))
            return Conflict(new { error = "Table number already exists in this restaurant." });

        // Prevent disabling a table that is still assigned.
        // This is assignment-aware; reservation occupancy is not yet modeled.
        if (table.IsActive && !request.IsActive.Value &&
            await _db.TableStaffAssignments.AnyAsync(a =>
                a.RestaurantId == restaurantId && a.DiningTableId == tableId, ct))
            return Conflict(new { error = "Unassign staff before disabling this table." });

        table.TableNumber = request.TableNumber.Value;
        table.SeatCount = request.SeatCount.Value;
        table.IsActive = request.IsActive.Value;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex, "IX_dining_tables_RestaurantId_TableNumber"))
        {
            return Conflict(new { error = "Table number already exists in this restaurant." });
        }
        await tx.CommitAsync(ct);
        var view = await LoadTableViewAsync(restaurantId, tableId, ct);
        return Ok(view);
    }

    // ---------------- CURRENT STAFF ASSIGNMENT ----------------

    [HttpPut("tables/{tableId:int}/assignment")]
    public async Task<IActionResult> SaveAssignment(
        int restaurantId, int tableId, [FromBody] SaveTableAssignmentRequest request,
        CancellationToken ct)
    {
        if (request.StaffMembershipId is null or <= 0)
            return BadRequest(new { error = "A positive StaffMembershipId is required." });

        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var table = await _db.DiningTables.AsNoTracking().SingleOrDefaultAsync(t =>
            t.RestaurantId == restaurantId && t.Id == tableId, ct);
        if (table is null)
            return NotFound(new { error = "Table not found in this restaurant." });
        if (!table.IsActive)
            return Conflict(new { error = "Cannot assign staff to an inactive table." });

        var member = await _db.StaffMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.RestaurantId == restaurantId &&
                m.Id == request.StaffMembershipId.Value, ct);
        if (member is null)
            return NotFound(new { error = "Membership not found in this restaurant." });
        if (member.Status != MembershipStatus.Active || member.Role != StaffRoleNames.Waiter)
            return Conflict(new { error = "Only active waiters can be assigned to tables." });

        var assignment = await _db.TableStaffAssignments.SingleOrDefaultAsync(a =>
            a.RestaurantId == restaurantId && a.DiningTableId == tableId, ct);

        if (assignment is null)
        {
            _db.TableStaffAssignments.Add(new TableStaffAssignment
            {
                RestaurantId = restaurantId,
                DiningTableId = tableId,
                StaffMembershipId = member.Id,
                AssignedAtUtc = DateTime.UtcNow
            });
        }
        else if (assignment.StaffMembershipId != member.Id)
        {
            assignment.StaffMembershipId = member.Id;
            assignment.AssignedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var view = await LoadTableViewAsync(restaurantId, tableId, ct);
        return Ok(view);
    }

    [HttpDelete("tables/{tableId:int}/assignment")]
    public async Task<IActionResult> RemoveAssignment(
        int restaurantId, int tableId, CancellationToken ct)
    {
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (!await _access.LockRestaurantAsync(restaurantId, ct))
            return NotFound();
        if (!await _access.IsActiveAdminAsync(User, restaurantId, ct))
            return Forbid();

        var tableExists = await _db.DiningTables.AnyAsync(t =>
            t.RestaurantId == restaurantId && t.Id == tableId, ct);
        if (!tableExists)
            return NotFound();

        var assignment = await _db.TableStaffAssignments.SingleOrDefaultAsync(a =>
            a.RestaurantId == restaurantId && a.DiningTableId == tableId, ct);
        if (assignment is not null)
        {
            _db.TableStaffAssignments.Remove(assignment);
            await _db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return NoContent();
    }

    private async Task<DiningTableView?> LoadTableViewAsync(
        int restaurantId, int tableId, CancellationToken ct)
    {
        var table = await _db.DiningTables.AsNoTracking()
            .Where(t => t.RestaurantId == restaurantId && t.Id == tableId)
            .Include(t => t.StaffAssignment)!
                .ThenInclude(a => a!.StaffMembership)
                    .ThenInclude(m => m.User)
            .SingleOrDefaultAsync(ct);
        return table is null ? null : ToTableView(table);
    }

    private static RestaurantSettingsView ToSettingsView(RestaurantSettings settings) =>
        new(settings.RestaurantId, settings.DefaultSeatCapacity, settings.UpdatedAtUtc);

    private static DiningTableView ToTableView(DiningTable table)
    {
        var assignment = table.StaffAssignment;
        StaffAssignmentView? assigned = assignment is null ? null : new(
            assignment.Id,
            assignment.StaffMembershipId,
            assignment.StaffMembership.User.UserName,
            assignment.StaffMembership.Role,
            assignment.StaffMembership.Status.ToString(),
            assignment.AssignedAtUtc);

        return new DiningTableView(
            table.Id, table.RestaurantId, table.TableNumber,
            table.SeatCount, table.IsActive, assigned);
    }

    private static bool UniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException pg &&
        pg.SqlState == PostgresErrorCodes.UniqueViolation &&
        pg.ConstraintName == constraintName;
}
