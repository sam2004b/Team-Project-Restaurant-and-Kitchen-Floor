using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

/// <summary>S3-US16-T01: immutable, restaurant-scoped order lifecycle events.</summary>
public sealed class OrderHistoryEventTests
{
    private static DbContextOptions<RestaurantDbContext> NewOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("order-history-" + Guid.NewGuid().ToString("N"))
            .Options;

    private static OrderHistoryEvent ExampleEvent() => new()
    {
        RestaurantId = 11,
        DiningTableId = 21,
        OrderId = 42,
        OrderItemId = 81,
        ActorStaffMembershipId = 51,
        EventType = OrderHistoryEventTypes.DishPreparationChanged,
        OccurredAtUtc = new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc),
        BeforeValueJson = JsonSerializer.Serialize(new { PreparationStatus = "Pending" }),
        AfterValueJson = JsonSerializer.Serialize(new { PreparationStatus = "Preparing" })
    };

    [Fact]
    public void SeparateBeforeAndAfterValuesPreserveTheOriginalState()
    {
        var evt = ExampleEvent();
        using var before = JsonDocument.Parse(evt.BeforeValueJson!);
        using var after = JsonDocument.Parse(evt.AfterValueJson!);

        Assert.Equal("Pending", before.RootElement
            .GetProperty("PreparationStatus").GetString());
        Assert.Equal("Preparing", after.RootElement
            .GetProperty("PreparationStatus").GetString());
        Assert.Equal(81, evt.OrderItemId);
        Assert.Equal(DateTimeKind.Utc, evt.OccurredAtUtc.Kind);
    }

    [Fact]
    public async Task EventPersistsActorTimestampTenantAndBothValues()
    {
        var options = NewOptions();
        long eventId;
        await using (var db = new RestaurantDbContext(options))
        {
            var evt = ExampleEvent();
            db.OrderHistoryEvents.Add(evt);
            await db.SaveChangesAsync();
            eventId = evt.Id;
        }

        await using var verify = new RestaurantDbContext(options);
        var saved = await verify.OrderHistoryEvents.AsNoTracking()
            .SingleAsync(e => e.Id == eventId);
        Assert.Equal(11, saved.RestaurantId);
        Assert.Equal(21, saved.DiningTableId);
        Assert.Equal(42, saved.OrderId);
        Assert.Equal(51, saved.ActorStaffMembershipId);
        Assert.Equal(OrderHistoryEventTypes.DishPreparationChanged, saved.EventType);
        Assert.Equal(new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc),
            saved.OccurredAtUtc);
        Assert.Contains("Pending", saved.BeforeValueJson);
        Assert.Contains("Preparing", saved.AfterValueJson);
    }

    [Fact]
    public async Task CreatedAndCancelledEventsCanHaveOneSidedValues()
    {
        var options = NewOptions();
        await using (var db = new RestaurantDbContext(options))
        {
            var created = ExampleEvent();
            created.EventType = OrderHistoryEventTypes.OrderCreated;
            created.BeforeValueJson = null;
            created.AfterValueJson = "{\"Status\":\"Open\"}";

            var cancelled = ExampleEvent();
            cancelled.EventType = OrderHistoryEventTypes.OrderCancelled;
            cancelled.AfterValueJson = null;
            cancelled.BeforeValueJson = "{\"Status\":\"Open\"}";
            db.OrderHistoryEvents.AddRange(created, cancelled);
            await db.SaveChangesAsync();
        }

        await using var verify = new RestaurantDbContext(options);
        var saved = await verify.OrderHistoryEvents.AsNoTracking().ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, e => e.EventType == OrderHistoryEventTypes.OrderCreated &&
            e.BeforeValueJson is null && e.AfterValueJson is not null);
        Assert.Contains(saved, e => e.EventType == OrderHistoryEventTypes.OrderCancelled &&
            e.BeforeValueJson is not null && e.AfterValueJson is null);
    }

    [Fact]
    public async Task OrderTimelineCanBeQueriedWithinOneRestaurant()
    {
        var options = NewOptions();
        await using (var db = new RestaurantDbContext(options))
        {
            var first = ExampleEvent();
            first.OccurredAtUtc = first.OccurredAtUtc.AddMinutes(-5);
            var second = ExampleEvent();
            second.EventType = OrderHistoryEventTypes.DishServed;
            second.AfterValueJson = "{\"ServiceStatus\":\"Served\"}";
            var anotherRestaurant = ExampleEvent();
            anotherRestaurant.RestaurantId = 12;
            db.OrderHistoryEvents.AddRange(second, first, anotherRestaurant);
            await db.SaveChangesAsync();
        }

        await using var verify = new RestaurantDbContext(options);
        var timeline = await verify.OrderHistoryEvents.AsNoTracking()
            .Where(e => e.RestaurantId == 11 && e.OrderId == 42)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => e.EventType)
            .ToArrayAsync();

        Assert.Equal(new[]
        {
            OrderHistoryEventTypes.DishPreparationChanged,
            OrderHistoryEventTypes.DishServed
        }, timeline);
    }

    [Fact]
    public void SchemaDefinesTenantSafeReferencesJsonChecksAndHistoryIndexes()
    {
        using var db = new RestaurantDbContext(NewOptions());
        // Relational check constraints are available from the design-time
        // model, not EF Core's optimized InMemory runtime model.
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(OrderHistoryEvent))!;

        Assert.Contains(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrder) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "RestaurantId", "DiningTableId", "OrderId" }));
        Assert.Contains(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(StaffMembership) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "RestaurantId", "ActorStaffMembershipId" }));
        // Preserve removed draft-item IDs without cascading audit deletion.
        Assert.DoesNotContain(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrderItem));

        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToHashSet();
        Assert.Contains("CK_order_history_events_type_not_blank", constraints);
        Assert.Contains("CK_order_history_events_item_positive", constraints);
        Assert.Contains("CK_order_history_events_values", constraints);
        Assert.Contains(entity.GetIndexes(), i =>
            i.GetDatabaseName() == "IX_order_history_events_order_timeline");
        Assert.Contains(entity.GetIndexes(), i =>
            i.GetDatabaseName() == "IX_order_history_events_actor_timeline");

        Assert.Equal("jsonb", entity.FindProperty("BeforeValueJson")!
            .FindAnnotation("Relational:ColumnType")?.Value as string);
        Assert.Equal("jsonb", entity.FindProperty("AfterValueJson")!
            .FindAnnotation("Relational:ColumnType")?.Value as string);
    }
}
