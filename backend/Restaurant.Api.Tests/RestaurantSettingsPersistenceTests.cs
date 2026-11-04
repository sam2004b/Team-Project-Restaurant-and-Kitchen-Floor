
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class RestaurantSettingsPersistenceTests
{
    private static DbContextOptions<RestaurantDbContext>
        CreateOptions()
    {
        return new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task RestaurantSettings_PersistAcrossContexts()
    {
        var options = CreateOptions();
        int restaurantId;

        await using (var db = new RestaurantDbContext(options))
        {
            var restaurant = new RestaurantAccount
            {
                Name = "Test Bistro",
                Address = "Test Street"
            };

            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync();

            restaurantId = restaurant.Id;

            db.RestaurantSettings.Add(new RestaurantSettings
            {
                RestaurantId = restaurantId,
                DefaultSeatCapacity = 4
            });

            await db.SaveChangesAsync();
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var settings = await db.RestaurantSettings
                .SingleAsync(
                    s => s.RestaurantId == restaurantId);

            Assert.Equal(4, settings.DefaultSeatCapacity);
        }
    }

    [Fact]
    public async Task TableSeatChange_PersistsAcrossContexts()
    {
        var options = CreateOptions();
        int tableId;

        await using (var db = new RestaurantDbContext(options))
        {
            var restaurant = new RestaurantAccount
            {
                Name = "Test Bistro",
                Address = "Test Street"
            };

            var table = new DiningTable
            {
                Restaurant = restaurant,
                TableNumber = 1,
                SeatCount = 4,
                IsActive = true
            };

            db.DiningTables.Add(table);
            await db.SaveChangesAsync();

            tableId = table.Id;
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var table = await db.DiningTables
                .SingleAsync(t => t.Id == tableId);

            table.SeatCount = 6;

            await db.SaveChangesAsync();
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var saved = await db.DiningTables
                .AsNoTracking()
                .SingleAsync(t => t.Id == tableId);

            Assert.Equal(1, saved.TableNumber);
            Assert.Equal(6, saved.SeatCount);
            Assert.True(saved.IsActive);
        }
    }

    [Fact]
    public async Task Tables_AreQueryableByRestaurant()
    {
        var options = CreateOptions();

        await using var db =
            new RestaurantDbContext(options);

        var bistro = new RestaurantAccount
        {
            Name = "Bistro 21",
            Address = "Address 1"
        };

        var cafe = new RestaurantAccount
        {
            Name = "Cafe 8",
            Address = "Address 2"
        };

        db.DiningTables.AddRange(
            new DiningTable
            {
                Restaurant = bistro,
                TableNumber = 1,
                SeatCount = 4
            },
            new DiningTable
            {
                Restaurant = cafe,
                TableNumber = 1,
                SeatCount = 2
            });

        await db.SaveChangesAsync();

        var bistroTables = await db.DiningTables
            .Where(t => t.RestaurantId == bistro.Id)
            .ToListAsync();

        Assert.Single(bistroTables);
        Assert.Equal(4, bistroTables[0].SeatCount);
    }
}
