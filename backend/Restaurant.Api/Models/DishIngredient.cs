
namespace Restaurant.Api.Models;

public class DishIngredient
{
    public int Id { get; set; }

    public int DishId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Dish Dish { get; set; } = null!;
}
