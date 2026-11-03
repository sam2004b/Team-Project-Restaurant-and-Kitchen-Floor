using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Dishes;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class OrderDishAvailabilityValidatorTests
{
    private static RestaurantDbContext CreateDb()
    {
        var options =
            new DbContextOptionsBuilder<RestaurantDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new RestaurantDbContext(options);
    }

    [Fact]
    public async Task AvailableDish_IsAccepted()
    {
        await using var db = CreateDb();

        var dish = new Dish
        {
            Name = "Carbonara",
            Category = "Pasta",
            Price = 450m,
            IsAvailable = true
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            new[]
            {
                new OrderDishSelection(dish.Id, 2)
            });

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UnavailableDish_IsRejected()
    {
        await using var db = CreateDb();

        var dish = new Dish
        {
            Name = "Carbonara",
            Category = "Pasta",
            Price = 450m,
            IsAvailable = false
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            new[]
            {
                new OrderDishSelection(dish.Id, 1)
            });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MissingDish_IsRejected()
    {
        await using var db = CreateDb();

        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            new[]
            {
                new OrderDishSelection(99999, 1)
            });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task EmptyOrder_IsRejected()
    {
        await using var db = CreateDb();

        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            Array.Empty<OrderDishSelection>());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task InvalidQuantity_IsRejected()
    {
        await using var db = CreateDb();

        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            new[]
            {
                new OrderDishSelection(1, 0)
            });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task StaleAvailability_IsRejected()
    {
        await using var db = CreateDb();

        var dish = new Dish
        {
            Name = "Carbonara",
            Category = "Pasta",
            Price = 450m,
            IsAvailable = true
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        // Waiter selects Carbonara while available.
        int selectedDishId = dish.Id;

        // Chef marks Carbonara unavailable.
        dish.IsAvailable = false;
        await db.SaveChangesAsync();

        // Waiter attempts to submit the stale selection.
        var validator =
            new OrderDishAvailabilityValidator(db);

        var result = await validator.ValidateAsync(
            new[]
            {
                new OrderDishSelection(selectedDishId, 1)
            });

        Assert.False(result.IsValid);
    }
}