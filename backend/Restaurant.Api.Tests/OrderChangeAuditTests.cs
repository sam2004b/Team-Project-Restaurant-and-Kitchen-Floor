using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

/// <summary>S3-US14-T01: amendment/cancellation audit schema and snapshots.</summary>
public sealed class OrderChangeAuditTests
{
    private static DbContextOptions<RestaurantDbContext> NewOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("order-change-audit-" + Guid.NewGuid().ToString("N"))
            .Options;

    private static RestaurantOrder ExampleOrder() => new()
    {
        Id = 42,
        RestaurantId = 11,
        DiningTableId = 21,
        SeatNumber = 2,
        CreatedByStaffMembershipId = 51,
        Status = RestaurantOrderStates.Sent,
        SentAtUtc = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc),
        CreatedAtUtc = new DateTime(2026, 11, 20, 8, 50, 0, DateTimeKind.Utc)
    };

    private static RestaurantOrderItem ExampleLine() => new()
    {
        Id = 81,
        OrderId = 42,
        DishId = 7,
        SequenceNumber = 1,
        Quantity = 2,
        DishNameSnapshot = "Pizza",
        UnitPriceSnapshot = 15.5m,
        SpecialRequest = "No onions",
        KitchenSubmissionId = 4
    };

    [Fact]
    public void OriginalSnapshotRetainsDetailsAfterAnAmendment()
    {
        var order = ExampleOrder();
        var item = ExampleLine();
        var before = OrderAuditSnapshot.Capture(order, new[] { item });

        item.Quantity = 3;
        item.SpecialRequest = "Extra cheese";
        order.Status = RestaurantOrderStates.Sent;
        var after = OrderAuditSnapshot.Capture(order, new[] { item });

        Assert.Equal(2, before.Items[0].Quantity);
        Assert.Equal("No onions", before.Items[0].SpecialRequest);
        Assert.Equal(15.5m, before.Items[0].UnitPriceSnapshot);
        Assert.Equal(4, before.Items[0].KitchenSubmissionId);
        Assert.Equal(3, after.Items[0].Quantity);
        Assert.Equal(11, before.RestaurantId);
        Assert.Equal(21, before.DiningTableId);
        Assert.Equal(2, before.SeatNumber);
        Assert.Contains("No onions", before.ToJson());
    }

    [Fact]
    public void CapturingAnotherOrdersLineIsRejected()
    {
        var otherLine = ExampleLine();
        otherLine.OrderId = 99;
        Assert.Throws<ArgumentException>(() =>
            OrderAuditSnapshot.Capture(ExampleOrder(), new[] { otherLine }));
    }

    [Fact]
    public async Task AmendmentPersistsActorReasonAndBothSnapshots()
    {
        var options = NewOptions();
        var original = OrderAuditSnapshot.Capture(ExampleOrder(), new[] { ExampleLine() });
        var changed = original with
        {
            Items = new[] { original.Items[0] with { Quantity = 3 } }
        };
        long id;
        await using (var db = new RestaurantDbContext(options))
        {
            var audit = new OrderChangeAudit
            {
                RestaurantId = 11,
                DiningTableId = 21,
                OrderId = 42,
                OrderItemId = 81,
                ChangedByStaffMembershipId = 51,
                ChangeType = OrderChangeTypes.Amended,
                Reason = "Customer requested another pizza",
                BeforeSnapshotJson = original.ToJson(),
                AfterSnapshotJson = changed.ToJson()
            };
            db.OrderChangeAudits.Add(audit);
            await db.SaveChangesAsync();
            id = audit.Id;
        }
        await using var verify = new RestaurantDbContext(options);
        var saved = await verify.OrderChangeAudits.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(51, saved.ChangedByStaffMembershipId);
        Assert.Equal("Customer requested another pizza", saved.Reason);
        Assert.Equal(OrderChangeTypes.Amended, saved.ChangeType);
        using var before = JsonDocument.Parse(saved.BeforeSnapshotJson);
        using var after = JsonDocument.Parse(saved.AfterSnapshotJson!);
        Assert.Equal(2, before.RootElement.GetProperty("Items")[0].GetProperty("Quantity").GetInt32());
        Assert.Equal(3, after.RootElement.GetProperty("Items")[0].GetProperty("Quantity").GetInt32());
    }

    [Fact]
    public async Task CancellationRetainsDeletedItemsOriginalDetails()
    {
        var options = NewOptions();
        var lineBeforeRemoval = ExampleLine();
        lineBeforeRemoval.KitchenSubmissionId = null; // a cancellable draft
        var original = OrderAuditSnapshot.Capture(ExampleOrder(), new[] { lineBeforeRemoval });
        await using (var db = new RestaurantDbContext(options))
        {
            var line = ExampleLine();
            line.KitchenSubmissionId = null;
            db.OrderItems.Add(line);
            await db.SaveChangesAsync();

            // Draft deletion destroys the current line, not its audit snapshot.
            db.OrderChangeAudits.Add(new OrderChangeAudit
            {
                RestaurantId = 11,
                DiningTableId = 21,
                OrderId = 42,
                OrderItemId = 81,
                ChangedByStaffMembershipId = 51,
                ChangeType = OrderChangeTypes.Cancelled,
                Reason = "Customer cancelled the dish",
                BeforeSnapshotJson = original.ToJson()
            });
            db.OrderItems.Remove(line);
            await db.SaveChangesAsync();
        }
        await using var verify = new RestaurantDbContext(options);
        Assert.Empty(await verify.OrderItems.AsNoTracking().ToListAsync());
        var saved = await verify.OrderChangeAudits.AsNoTracking().SingleAsync();
        Assert.Null(saved.AfterSnapshotJson);
        Assert.Equal(OrderChangeTypes.Cancelled, saved.ChangeType);
        using var originalJson = JsonDocument.Parse(saved.BeforeSnapshotJson);
        Assert.Equal("Pizza", originalJson.RootElement.GetProperty("Items")[0]
            .GetProperty("DishNameSnapshot").GetString());
        Assert.Equal(2, originalJson.RootElement.GetProperty("Items")[0]
            .GetProperty("Quantity").GetInt32());
    }

    [Fact]
    public void SchemaContainsTenantSafeReferencesConstraintsAndHistoryIndexes()
    {
        using var db = new RestaurantDbContext(NewOptions());
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(OrderChangeAudit))!;

        Assert.Contains(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrder) &&
            fk.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "RestaurantId", "DiningTableId", "OrderId" }));
        Assert.Contains(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(StaffMembership) &&
            fk.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "RestaurantId", "ChangedByStaffMembershipId" }));
        // Important: deleting a draft line must not erase historical evidence.
        Assert.DoesNotContain(entity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrderItem));

        var constraints = entity.GetCheckConstraints().Select(c => c.Name).ToHashSet();
        Assert.Contains("CK_order_change_audits_type", constraints);
        Assert.Contains("CK_order_change_audits_reason", constraints);
        Assert.Contains("CK_order_change_audits_snapshots", constraints);
        Assert.Contains(entity.GetIndexes(), i =>
            i.GetDatabaseName() == "IX_order_change_audits_order_history");
        var columnType = entity.FindProperty("BeforeSnapshotJson")!
            .FindAnnotation("Relational:ColumnType")?.Value as string;

        Assert.Equal("jsonb", columnType);
    }
}
