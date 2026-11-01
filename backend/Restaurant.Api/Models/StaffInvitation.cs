
using System;

namespace Restaurant.Api.Models;

public class StaffInvitation
{
    public int Id { get; set; }

    public int RestaurantId { get; set; }

    public RestaurantAccount Restaurant { get; set; }
        = null!;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; private set; }
        = string.Empty;

    public string Role { get; set; } =
        StaffRoleNames.Waiter;

    public InvitationStatus Status { get; set; }
        = InvitationStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;
}
