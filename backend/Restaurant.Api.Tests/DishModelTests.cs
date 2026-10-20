
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public class DishModelTests
{
    private static RestaurantDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<RestaurantDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new RestaurantDbContext(options);
    }

    [Fact]
    public async Task CreatesDishWithFourIngredients()
    {
        await using var db = CreateContext();

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 450m,
            Description = "Italian pasta",
            Category = "Pasta",
            IsAvailable = true,

            Ingredients = new List<DishIngredient>
            {
                new() { Name = "Spaghetti" },
                new() { Name = "Eggs" },
                new() { Name = "Pecorino Romano" },
                new() { Name = "Guanciale" }
            }
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var saved = await db.Dishes
            .AsNoTracking()
            .Include(d => d.Ingredients)
            .SingleAsync(d => d.Id == dish.Id);

        Assert.Equal("Carbonara", saved.Name);
        Assert.Equal(450m, saved.Price);
        Assert.Equal(4, saved.Ingredients.Count);
    }

    [Fact]
    public async Task UpdatesPriceAndIngredient()
    {
        await using var db = CreateContext();

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 450m,
            Category = "Pasta",
            Ingredients = new List<DishIngredient>
            {
                new() { Name = "Spaghetti" },
                new() { Name = "Eggs" },
                new() { Name = "Pecorino Romano" },
                new() { Name = "Guanciale" }
            }
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        // Simulate administrator editing the dish
        dish.Price = 500m;

        var ingredient = dish.Ingredients
            .Single(i => i.Name == "Eggs");

        ingredient.Name = "Egg Yolks";

        await db.SaveChangesAsync();

        // Reload saved data
        var updated = await db.Dishes
            .AsNoTracking()
            .Include(d => d.Ingredients)
            .SingleAsync(d => d.Id == dish.Id);

        Assert.Equal(500m, updated.Price);
        Assert.Equal(4, updated.Ingredients.Count);

        Assert.Contains(
            updated.Ingredients,
            i => i.Name == "Egg Yolks");

        Assert.DoesNotContain(
            updated.Ingredients,
            i => i.Name == "Eggs");
    }
}
