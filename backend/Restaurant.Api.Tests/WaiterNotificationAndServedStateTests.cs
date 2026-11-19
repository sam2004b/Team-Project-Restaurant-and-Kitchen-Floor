using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

/// <summary>S3-US13-T01: durable waiter notification and per-line served state.</summary>
public sealed class WaiterNotificationAndServedStateTests
{
    private static DbContextOptions<RestaurantDbContext> NewOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("waiter-notifications-" + Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task NewDishLinesRemainUnservedWithNoInventedTimestamp()
    {
        var options = NewOptions();
        await using (var db = new RestaurantDbContext(options))
        {
            db.OrderItems.Add(new RestaurantOrderItem
            {
                Order = new RestaurantOrder
                {
                    RestaurantId = 11,
                    DiningTableId = 21,
                    CreatedByStaffMembershipId = 31
                },
                DishId = 41,
                Quantity = 1,
                SequenceNumber = 1,
                DishNameSnapshot = "Pizza",
                UnitPriceSnapshot = 10m
            });
            await db.SaveChangesAsync();
        }

        await using var verification = new RestaurantDbContext(options);
        var line = await verification.OrderItems.AsNoTracking().SingleAsync();
        Assert.Equal(DishServiceStates.Unserved, line.ServiceStatus);
        Assert.Null(line.ServedAtUtc);
    }

    [Fact]
    public async Task OneReadyDishCanBeServedWithoutChangingOtherDishOrOrder()
    {
        var options = NewOptions();
        var started = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var ready = started.AddMinutes(5);
        var served = ready.AddMinutes(3);

        await using (var db = new RestaurantDbContext(options))
        {
            var order = new RestaurantOrder
            {
                RestaurantId = 11,
                DiningTableId = 21,
                CreatedByStaffMembershipId = 31,
                Status = RestaurantOrderStates.Sent,
                SentAtUtc = started.AddMinutes(-5)
            };
            var submission = new KitchenSubmission
            {
                Order = order,
                RestaurantId = 11,
                SequenceNumber = 1,
                SentAtUtc = started.AddMinutes(-5)
            };
            db.OrderItems.AddRange(
                new RestaurantOrderItem
                {
                    Order = order,
                    KitchenSubmission = submission,
                    DishId = 41,
                    SequenceNumber = 1,
                    Quantity = 1,
                    DishNameSnapshot = "Pizza",
                    UnitPriceSnapshot = 12m,
                    PreparationStatus = DishPreparationStates.Ready,
                    PreparationStartedAtUtc = started,
                    ReadyAtUtc = ready,
                    ServiceStatus = DishServiceStates.Served,
                    ServedAtUtc = served
                },
                new RestaurantOrderItem
                {
                    Order = order,
                    KitchenSubmission = submission,
                    DishId = 42,
                    SequenceNumber = 2,
                    Quantity = 1,
                    DishNameSnapshot = "Salad",
                    UnitPriceSnapshot = 9m,
                    PreparationStatus = DishPreparationStates.Ready,
                    PreparationStartedAtUtc = started,
                    ReadyAtUtc = ready
                });
            await db.SaveChangesAsync();
        }

        await using var verification = new RestaurantDbContext(options);
        var lines = await verification.OrderItems.AsNoTracking()
            .OrderBy(i => i.SequenceNumber).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(DishServiceStates.Served, lines[0].ServiceStatus);
        Assert.Equal(served, lines[0].ServedAtUtc);
        Assert.Equal(DishServiceStates.Unserved, lines[1].ServiceStatus);
        Assert.Null(lines[1].ServedAtUtc);
        Assert.Equal(RestaurantOrderStates.Sent,
            (await verification.Orders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task NotificationReadAcknowledgmentPersistsForOneWaiterAndDish()
    {
        var options = NewOptions();
        var created = new DateTime(2026, 11, 19, 11, 0, 0, DateTimeKind.Utc);
        int notificationId;

        await using (var db = new RestaurantDbContext(options))
        {
            var waiter = new StaffMembership
            {
                Id = 51,
                RestaurantId = 11,
                UserId = "waiter-user",
                Role = StaffRoleNames.Waiter
            };
            var order = new RestaurantOrder
            {
                RestaurantId = 11,
                DiningTableId = 21,
                CreatedByStaffMembershipId = 51,
                Status = RestaurantOrderStates.Sent,
                SentAtUtc = created.AddMinutes(-20)
            };
            var submission = new KitchenSubmission
            {
                Order = order,
                RestaurantId = 11,
                SequenceNumber = 1,
                SentAtUtc = created.AddMinutes(-20)
            };
            var line = new RestaurantOrderItem
            {
                Order = order,
                KitchenSubmission = submission,
                DishId = 41,
                SequenceNumber = 1,
                Quantity = 1,
                DishNameSnapshot = "Soup",
                UnitPriceSnapshot = 8m,
                PreparationStatus = DishPreparationStates.Ready,
                PreparationStartedAtUtc = created.AddMinutes(-12),
                ReadyAtUtc = created.AddMinutes(-1)
            };
            var notification = new WaiterNotification
            {
                RestaurantId = 11,
                DiningTableId = 21,
                Order = order,
                OrderItem = line,
                WaiterStaffMembership = waiter,
                CreatedAtUtc = created
            };
            db.WaiterNotifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        await using (var db = new RestaurantDbContext(options))
        {
            var notification = await db.WaiterNotifications.SingleAsync(n => n.Id == notificationId);
            Assert.False(notification.IsRead);
            Assert.Null(notification.ReadAtUtc);
            Assert.Equal(11, notification.RestaurantId);
            Assert.Equal(21, notification.DiningTableId);
            Assert.Equal(51, notification.WaiterStaffMembershipId);
            Assert.True(notification.OrderId > 0);
            Assert.True(notification.OrderItemId > 0);
            notification.ReadAtUtc = created.AddSeconds(20);
            await db.SaveChangesAsync();
        }

        await using var verification = new RestaurantDbContext(options);
        var saved = await verification.WaiterNotifications.AsNoTracking()
            .SingleAsync(n => n.Id == notificationId);
        Assert.True(saved.IsRead);
        Assert.Equal(created.AddSeconds(20), saved.ReadAtUtc);
    }

    [Fact]
    public void SchemaContainsTenantSafeForeignKeysUnreadIndexAndServiceConstraints()
    {
        using var db = new RestaurantDbContext(NewOptions());
        var schema = db.GetService<IDesignTimeModel>().Model;
        var notification = schema.FindEntityType(typeof(WaiterNotification))!;
        var orderLine = schema.FindEntityType(typeof(RestaurantOrderItem))!;

        Assert.Contains(notification.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrder) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "RestaurantId", "DiningTableId", "OrderId" }));
        Assert.Contains(notification.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrderItem) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "OrderId", "OrderItemId" }));
        Assert.Contains(notification.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(StaffMembership) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "RestaurantId", "WaiterStaffMembershipId" }));

        var idx = notification.GetIndexes().Single(i =>
            i.GetDatabaseName() == "IX_waiter_notifications_unread_inbox");
        Assert.Contains("ReadAtUtc", idx.GetFilter()!);
        Assert.Contains(notification.GetIndexes(), i =>
            i.IsUnique && i.GetDatabaseName() == "UX_waiter_notifications_item_waiter");

        var names = orderLine.GetCheckConstraints().Select(c => c.Name).ToHashSet();
        Assert.Contains("CK_order_items_service_status", names);
        Assert.Contains("CK_order_items_service_consistency", names);
        Assert.Equal(DishServiceStates.Unserved,
            orderLine.FindProperty(nameof(RestaurantOrderItem.ServiceStatus))!.GetDefaultValue());
        Assert.Contains(notification.GetCheckConstraints(), c =>
            c.Name == "CK_waiter_notifications_read_after_created");
    }
}
