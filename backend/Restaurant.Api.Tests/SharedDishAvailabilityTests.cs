
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Dishes;
using Restaurant.Api.Services.Menu;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public class SharedDishAvailabilityTests
{
    private static DbContextOptions<RestaurantDbContext>
        CreateOptions()
    {
        return new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private sealed class TestMenuNotifier
        : IMenuUpdateNotifier
    {
        public int NotificationCount { get; private set; }

        public Task NotifyMenuChangedAsync(
            CancellationToken cancellationToken = default)
        {
            NotificationCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task MarkUnavailable_PersistsAcrossContexts()
    {
        var options = CreateOptions();
        int dishId;

        await using (var db = new RestaurantDbContext(options))
        {
            var dish = new Dish
            {
                Name = "Carbonara",
                Price = 14.50m,
                Category = "Pasta",
                IsAvailable = true
            };

            db.Dishes.Add(dish);
            await db.SaveChangesAsync();

            dishId = dish.Id;
        }

        var notifier = new TestMenuNotifier();

        await using (var db = new RestaurantDbContext(options))
        {
            var service = new DishAvailabilityService(
                db, notifier);

            var result = await service.SetAvailabilityAsync(
                dishId, false);

            Assert.Equal(
                AvailabilityUpdateResult.Updated,
                result);
        }

        // Read from a fresh context.
        await using (var db = new RestaurantDbContext(options))
        {
            var saved = await db.Dishes
                .AsNoTracking()
                .SingleAsync(d => d.Id == dishId);

            Assert.False(saved.IsAvailable);
            Assert.Equal("Carbonara", saved.Name);
            Assert.Equal(14.50m, saved.Price);
        }

        Assert.Equal(1, notifier.NotificationCount);
    }

    [Fact]
    public async Task RepeatedUpdate_DoesNotCreateDuplicates()
    {
        var options = CreateOptions();

        await using var db =
            new RestaurantDbContext(options);

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 14.50m,
            Category = "Pasta",
            IsAvailable = true
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var notifier = new TestMenuNotifier();
        var service = new DishAvailabilityService(
            db, notifier);

        var first = await service.SetAvailabilityAsync(
            dish.Id, false);

        var second = await service.SetAvailabilityAsync(
            dish.Id, false);

        Assert.Equal(
            AvailabilityUpdateResult.Updated, first);

        Assert.Equal(
            AvailabilityUpdateResult.Unchanged, second);

        Assert.Equal(1, await db.Dishes.CountAsync());
        Assert.Equal(1, notifier.NotificationCount);
    }

    [Fact]
    public async Task ReenableDish_UsesSameRecord()
    {
        var options = CreateOptions();

        await using var db =
            new RestaurantDbContext(options);

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 14.50m,
            Category = "Pasta",
            IsAvailable = true
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        int originalId = dish.Id;

        var notifier = new TestMenuNotifier();
        var service = new DishAvailabilityService(
            db, notifier);

        await service.SetAvailabilityAsync(
            originalId, false);

        await service.SetAvailabilityAsync(
            originalId, true);

        var saved = await db.Dishes.SingleAsync();

        Assert.Equal(originalId, saved.Id);
        Assert.True(saved.IsAvailable);
        Assert.Equal(1, await db.Dishes.CountAsync());
        Assert.Equal(2, notifier.NotificationCount);
    }

    [Fact]
    public async Task AvailabilityChange_PreservesDishDetails()
    {
        var options = CreateOptions();

        await using var db =
            new RestaurantDbContext(options);

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 14.50m,
            Description = "Italian pasta",
            Category = "Pasta",
            IsAvailable = true,
            ImageUrl = "/images/dishes/carbonara.jpg",
            Ingredients = new List<DishIngredient>
            {
                new() { Name = "Spaghetti" },
                new() { Name = "Eggs" }
            }
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var service = new DishAvailabilityService(
            db, new TestMenuNotifier());

        await service.SetAvailabilityAsync(
            dish.Id, false);

        var saved = await db.Dishes
            .Include(d => d.Ingredients)
            .SingleAsync(d => d.Id == dish.Id);

        Assert.False(saved.IsAvailable);
        Assert.Equal("Carbonara", saved.Name);
        Assert.Equal(14.50m, saved.Price);
        Assert.Equal("Italian pasta", saved.Description);
        Assert.Equal("Pasta", saved.Category);
        Assert.Equal(
            "/images/dishes/carbonara.jpg",
            saved.ImageUrl);
        Assert.Equal(2, saved.Ingredients.Count);
    }

    [Fact]
    public async Task MissingDish_DoesNotCreateNewRecord()
    {
        var options = CreateOptions();

        await using var db =
            new RestaurantDbContext(options);

        var notifier = new TestMenuNotifier();

        var service = new DishAvailabilityService(
            db, notifier);

        var result = await service.SetAvailabilityAsync(
            99999, false);

        Assert.Equal(
            AvailabilityUpdateResult.NotFound,
            result);

        Assert.Empty(await db.Dishes.ToListAsync());
        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task CustomerMenu_ReflectsSharedAvailability()
    {
        var options = CreateOptions();
        int dishId;

        await using (var db = new RestaurantDbContext(options))
        {
            var dish = new Dish
            {
                Name = "Carbonara",
                Price = 14.50m,
                Category = "Pasta",
                IsAvailable = true
            };

            db.Dishes.Add(dish);
            await db.SaveChangesAsync();
            dishId = dish.Id;
        }

        var notifier = new TestMenuNotifier();

        await using (var db = new RestaurantDbContext(options))
        {
            var service = new DishAvailabilityService(
                db, notifier);

            await service.SetAvailabilityAsync(dishId, false);
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var menuService = new MenuReadService(db);

            var menu = await menuService
                .GetAvailableDishesAsync();

            Assert.Empty(menu);

            Assert.Equal(
                false,
                await menuService.GetDishAvailabilityAsync(
                    dishId));
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var service = new DishAvailabilityService(
                db, notifier);

            await service.SetAvailabilityAsync(dishId, true);
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var menuService = new MenuReadService(db);

            var menu = await menuService
                .GetAvailableDishesAsync();

            Assert.Single(menu);
            Assert.Equal(dishId, menu[0].Id);
            Assert.True(menu[0].IsAvailable);
        }

        Assert.Equal(2, notifier.NotificationCount);
    }
}
