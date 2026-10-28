namespace Restaurant.Api.Models;

public sealed class RestaurantOrder
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RestaurantOrderItem> Items { get; set; }
        = new List<RestaurantOrderItem>();
}
