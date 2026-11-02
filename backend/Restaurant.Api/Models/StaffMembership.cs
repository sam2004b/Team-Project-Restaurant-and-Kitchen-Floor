
using Microsoft.AspNetCore.Identity;
using System;

namespace Restaurant.Api.Models;

public class StaffMembership
{
    public int Id { get; set; }

    public int RestaurantId { get; set; }

    public RestaurantAccount Restaurant { get; set; }
        = null!;

    public string UserId { get; set; } = string.Empty;

    public IdentityUser User { get; set; } = null!;

    public string Role { get; set; } =
        StaffRoleNames.Waiter;

    public MembershipStatus Status { get; set; }
        = MembershipStatus.Active;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;
}
