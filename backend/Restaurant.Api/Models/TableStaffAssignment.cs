
using System;

namespace Restaurant.Api.Models;

/// <summary>
/// Current staff assignment for one dining table.
/// </summary>
public class TableStaffAssignment
{
    public int Id { get; set; }

    public int RestaurantId { get; set; }

    public int DiningTableId { get; set; }

    public DiningTable DiningTable { get; set; }
        = null!;

    // References the existing staff_memberships table.
    public int StaffMembershipId { get; set; }

    public StaffMembership StaffMembership { get; set; }
        = null!;

    public DateTime AssignedAtUtc { get; set; }
        = DateTime.UtcNow;
}
