using System.Collections.Generic;

namespace Restaurant.Api.Models
{
    public class Dish
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string? Description { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsAvailable { get; set; } = true;

        public string? ImageUrl { get; set; }

        public ICollection<DishIngredient> Ingredients { get; set; }
            = new List<DishIngredient>();

    }
}
