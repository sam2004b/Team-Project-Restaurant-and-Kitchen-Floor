using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Orders;
using Xunit;

namespace Restaurant.Api.Tests;

/// <summary>Persistence and model checks for S3-US4-T01 (backend only).</summary>
public sealed class DishPreparationStatusTests
{
    private static DbContextOptions<RestaurantDbContext> NewOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("dish-progress-" + Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task NewLinesStartPendingWithoutInventedTimestamps()
    {
        var options = NewOptions();
        await using (var db = new RestaurantDbContext(options))
        {
            var item = new RestaurantOrderItem
            {
                Order = new RestaurantOrder
                {
                    RestaurantId = 11,
                    DiningTableId = 21,
                    CreatedByStaffMembershipId = 31
                },
                DishId = 41,
                SequenceNumber = 1,
                Quantity = 2,
                DishNameSnapshot = "Pizza",
                UnitPriceSnapshot = 12m
            };
            db.OrderItems.Add(item);
            await db.SaveChangesAsync();
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var line = await db.OrderItems.AsNoTracking().SingleAsync();
            Assert.Equal(DishPreparationStates.Pending, line.PreparationStatus);
            Assert.Null(line.PreparationStartedAtUtc);
            Assert.Null(line.ReadyAtUtc);
            Assert.Equal(2, line.Quantity);
        }
    }

    [Fact]
    public async Task DifferentDishLinesKeepIndependentStatusesAndTimestamps()
    {
        var options = NewOptions();
        var startedAt = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        var readyAt = startedAt.AddMinutes(12);
        int firstId;
        int secondId;

        await using (var db = new RestaurantDbContext(options))
        {
            var order = new RestaurantOrder
            {
                RestaurantId = 11,
                DiningTableId = 21,
                CreatedByStaffMembershipId = 31,
                Status = RestaurantOrderStates.Sent,
                SentAtUtc = startedAt.AddMinutes(-5)
            };
            var batch = new KitchenSubmission
            {
                Order = order,
                RestaurantId = 11,
                SequenceNumber = 1,
                SentAtUtc = startedAt.AddMinutes(-5)
            };
            var first = new RestaurantOrderItem
            {
                Order = order,
                KitchenSubmission = batch,
                DishId = 41,
                SequenceNumber = 1,
                Quantity = 1,
                DishNameSnapshot = "Pizza",
                UnitPriceSnapshot = 12m,
                PreparationStatus = DishPreparationStates.Preparing,
                PreparationStartedAtUtc = startedAt
            };
            var second = new RestaurantOrderItem
            {
                Order = order,
                KitchenSubmission = batch,
                DishId = 42,
                SequenceNumber = 2,
                Quantity = 1,
                DishNameSnapshot = "Salad",
                UnitPriceSnapshot = 8m,
                PreparationStatus = DishPreparationStates.Ready,
                PreparationStartedAtUtc = startedAt,
                ReadyAtUtc = readyAt
            };
            db.OrderItems.AddRange(first, second);
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var items = await db.OrderItems.AsNoTracking()
                .OrderBy(i => i.SequenceNumber).ToListAsync();
            Assert.Equal(2, items.Count);
            Assert.Equal(firstId, items[0].Id);
            Assert.Equal(DishPreparationStates.Preparing, items[0].PreparationStatus);
            Assert.Equal(startedAt, items[0].PreparationStartedAtUtc);
            Assert.Null(items[0].ReadyAtUtc);

            Assert.Equal(secondId, items[1].Id);
            Assert.Equal(DishPreparationStates.Ready, items[1].PreparationStatus);
            Assert.Equal(startedAt, items[1].PreparationStartedAtUtc);
            Assert.Equal(readyAt, items[1].ReadyAtUtc);

            var order = await db.Orders.AsNoTracking().SingleAsync();
            Assert.Equal(RestaurantOrderStates.Sent, order.Status);
            Assert.Equal(11, order.RestaurantId);
        }
    }

    [Fact]
    public async Task KitchenFeedKeepsRestaurantDataSeparated()
    {
        var options = NewOptions();
        await using var db = new RestaurantDbContext(options);

        for (var restaurantId = 11; restaurantId <= 12; restaurantId++)
        {
            var order = new RestaurantOrder
            {
                RestaurantId = restaurantId,
                DiningTableId = restaurantId * 10,
                CreatedByStaffMembershipId = restaurantId * 100,
                Status = RestaurantOrderStates.Sent,
                SentAtUtc = DateTime.UtcNow
            };
            var batch = new KitchenSubmission
            {
                Order = order,
                RestaurantId = restaurantId,
                SequenceNumber = 1
            };
            db.OrderItems.Add(new RestaurantOrderItem
            {
                Order = order,
                KitchenSubmission = batch,
                DishId = restaurantId,
                SequenceNumber = 1,
                Quantity = 1,
                DishNameSnapshot = "Dish for tenant " + restaurantId,
                UnitPriceSnapshot = 10m
            });
        }
        await db.SaveChangesAsync();

        var feed = new OrderDetailsQueryService(db);
        var firstRestaurant = await feed.GetKitchenFeedAsync(11);
        var secondRestaurant = await feed.GetKitchenFeedAsync(12);

        Assert.Single(firstRestaurant);
        Assert.Single(secondRestaurant);
        Assert.Equal(11, firstRestaurant[0].RestaurantId);
        Assert.Equal(12, secondRestaurant[0].RestaurantId);
        Assert.NotEqual(firstRestaurant[0].Id, secondRestaurant[0].Id);
    }

    [Fact]
    public void SchemaDefinesSafeDefaultConstraintsAndSameOrderSubmission()
    {
        using var db = new RestaurantDbContext(NewOptions());
        var model = db.GetService<IDesignTimeModel>().Model
                .FindEntityType(typeof(RestaurantOrderItem))!;
        var property = model.FindProperty(nameof(RestaurantOrderItem.PreparationStatus))!;

        Assert.Equal(DishPreparationStates.Pending, property.GetDefaultValue());
        Assert.Equal(16, property.GetMaxLength());
        Assert.False(property.IsNullable);
        Assert.Contains(model.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(KitchenSubmission) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "OrderId", "KitchenSubmissionId" }));

        var constraints = model.GetCheckConstraints()
            .Select(c => c.Name).ToHashSet();
        Assert.Contains("CK_order_items_preparation_status", constraints);
        Assert.Contains("CK_order_items_preparation_timestamps", constraints);
        Assert.Contains("CK_order_items_preparation_requires_submission", constraints);
    }
}
