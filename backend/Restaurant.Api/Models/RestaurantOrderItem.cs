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

    public int Quantity { get; set; }
    public string? SpecialRequest { get; set; }

    // Snapshots preserve preparation and pricing details if the menu changes.
    public string DishNameSnapshot { get; set; } = string.Empty;
    public decimal UnitPriceSnapshot { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
