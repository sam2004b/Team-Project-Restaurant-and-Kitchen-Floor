namespace Restaurant.Api.Models;

/// <summary>Order header associated with a restaurant table.</summary>
public sealed class RestaurantOrder
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int DiningTableId { get; set; }
    // Optional for historical orders. New per-seat orders use 1..DiningTable.SeatCount.
    public int? SeatNumber { get; set; }
    public DiningTable DiningTable { get; set; } = null!;

    // Historical creator: table reassignment never rewrites this ID.
    public int CreatedByStaffMembershipId { get; set; }
    public StaffMembership CreatedByStaffMembership { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Sent is still an active, editable order. Only payment closes the order.
    // Legacy = older order headers created before this feature.
    public string Status { get; set; } = RestaurantOrderStates.Open;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public ICollection<KitchenSubmission> KitchenSubmissions { get; set; }
        = new List<KitchenSubmission>();

    public ICollection<RestaurantOrderItem> Items { get; set; }
        = new List<RestaurantOrderItem>();
}

public static class RestaurantOrderStates
{
    public const string Legacy = "Legacy";
    public const string Open = "Open";
    public const string Sent = "Sent";
    public const string Closed = "Closed";

    public static bool IsActive(string status) =>
        status is Open or Sent;
}
