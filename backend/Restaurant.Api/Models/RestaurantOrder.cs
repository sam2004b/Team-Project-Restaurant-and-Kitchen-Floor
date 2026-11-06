using System;

namespace Restaurant.Api.Models;

/// <summary>
/// Persistent order header tied to a restaurant table.
/// Order items, kitchen status and submission are added by the ordering tasks.
/// </summary>
public sealed class RestaurantOrder
{
    public int Id { get; set; }

    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public DiningTable DiningTable { get; set; } = null!;

    // Historical author: reassignment never changes this value.
    public int CreatedByStaffMembershipId { get; set; }
    public StaffMembership CreatedByStaffMembership { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
