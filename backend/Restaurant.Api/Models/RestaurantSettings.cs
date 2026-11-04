
using System;

namespace Restaurant.Api.Models;

/// <summary>
/// Configuration belonging to one restaurant.
/// </summary>
public class RestaurantSettings
{
    // Also serves as the primary key.
    public int RestaurantId { get; set; }

    public RestaurantAccount Restaurant { get; set; }
        = null!;

    // Default capacity for newly configured tables.
    public int DefaultSeatCapacity { get; set; } = 4;

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;
}
