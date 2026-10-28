namespace Restaurant.Api.Models;

public sealed class RestaurantOrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public RestaurantOrder Order { get; set; } = null!;

    public int DishId { get; set; }

    public Dish Dish { get; set; } = null!;

    public string DishName { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }
}
