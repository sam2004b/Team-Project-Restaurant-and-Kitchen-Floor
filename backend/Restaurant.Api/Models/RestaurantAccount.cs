using System;
using System.Collections.Generic;

namespace Restaurant.Api.Models;

public class RestaurantAccount
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string NormalizedName { get; private set; }
        = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public ICollection<StaffMembership> StaffMembers { get; set; }
        = new List<StaffMembership>();

    public ICollection<StaffInvitation> Invitations { get; set; }
        = new List<StaffInvitation>();
}
