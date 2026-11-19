namespace Restaurant.Api.Models;

/// <summary>One dish and its own preparation instructions.</summary>
public sealed class RestaurantOrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public RestaurantOrder Order { get; set; } = null!;

    // Null = draft line. Non-null = already submitted; never mutate this line.
    public int? KitchenSubmissionId { get; set; }
    public KitchenSubmission? KitchenSubmission { get; set; }

    public int DishId { get; set; }
    public Dish Dish { get; set; } = null!;

    // Stable one-based preparation order within the parent order.
    public int SequenceNumber { get; set; }
    public int Quantity { get; set; }
    public string? SpecialRequest { get; set; }

    // Preparation progress belongs to this line, never the entire order.
    // Draft lines and newly submitted lines start Pending.
    public string PreparationStatus { get; set; } = DishPreparationStates.Pending;

    // Null until kitchen staff starts / finishes this specific line.
    public DateTime? PreparationStartedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }

    // Service progress is distinct from kitchen preparation progress.
    // Each order line is served independently; unserved lines have no time.
    public string ServiceStatus { get; set; } = DishServiceStates.Unserved;
    public DateTime? ServedAtUtc { get; set; }

    // Snapshots preserve preparation and pricing details if the menu changes.
    public string DishNameSnapshot { get; set; } = string.Empty;
    public decimal UnitPriceSnapshot { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
