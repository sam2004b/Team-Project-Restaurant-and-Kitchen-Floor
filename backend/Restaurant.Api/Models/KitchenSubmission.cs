namespace Restaurant.Api.Models;

/// <summary>One immutable batch sent to the kitchen for an ongoing order.</summary>
public sealed class KitchenSubmission
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public RestaurantOrder Order { get; set; } = null!;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<RestaurantOrderItem> Items { get; set; }
        = new List<RestaurantOrderItem>();
}
