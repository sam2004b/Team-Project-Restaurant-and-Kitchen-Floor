namespace Restaurant.Api.Models;

/// <summary>One immutable batch sent to the kitchen for an ongoing order.</summary>
public sealed class KitchenSubmission
{
    public int Id { get; set; }
    // Stored explicitly to support tenant-scoped feed indexes and a
    // composite FK that forbids submission/order restaurant mismatches.
    public int RestaurantId { get; set; }
    public int OrderId { get; set; }
    public RestaurantOrder Order { get; set; } = null!;

    // One-based position in THIS order's kitchen submission history.
    public int SequenceNumber { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<RestaurantOrderItem> Items { get; set; }
        = new List<RestaurantOrderItem>();
}
