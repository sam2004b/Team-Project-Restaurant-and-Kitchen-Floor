namespace Restaurant.Api.Models;

/// <summary>Order header associated with a restaurant table.</summary>
public sealed class RestaurantOrder
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    public DiningTable DiningTable { get; set; } = null!;

    // Historical creator: table reassignment never rewrites this ID.
    public int CreatedByStaffMembershipId { get; set; }
    public StaffMembership CreatedByStaffMembership { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Open = editable draft; Sent = durable kitchen-ready state;
    // Legacy = older order headers created before this feature.
    public string Status { get; set; } = RestaurantOrderStates.Open;
    public DateTime? SentAtUtc { get; set; }

    public ICollection<RestaurantOrderItem> Items { get; set; }
        = new List<RestaurantOrderItem>();
}

public static class RestaurantOrderStates
{
    public const string Legacy = "Legacy";
    public const string Open = "Open";
    public const string Sent = "Sent";
}
