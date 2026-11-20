using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Orders;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class OrderDraftPersistenceServiceTests
{
    private sealed record Fixture(
        DbContextOptions<RestaurantDbContext> Options,
        int RestaurantId,
        int TableId,
        int WaiterId,
        int OtherWaiterId,
        int CarbonaraId,
        int SoupId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("order-draft-" + Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new RestaurantDbContext(options);
        var restaurant = new RestaurantAccount
        {
            Name = "Bistro 21",
            Address = "Test street"
        };
        db.Restaurants.Add(restaurant);

        var ivan = new IdentityUser { UserName = "ivan" };
        var maria = new IdentityUser { UserName = "maria" };
        db.Users.AddRange(ivan, maria);
        await db.SaveChangesAsync();

        var waiter = new StaffMembership
        {
            RestaurantId = restaurant.Id,
            UserId = ivan.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        var anotherWaiter = new StaffMembership
        {
            RestaurantId = restaurant.Id,
            UserId = maria.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        db.StaffMemberships.AddRange(waiter, anotherWaiter);

        var table = new DiningTable
        {
            RestaurantId = restaurant.Id,
            TableNumber = 12,
            SeatCount = 4,
            IsActive = true
        };
        db.DiningTables.Add(table);

        var carbonara = new Dish
        {
            Name = "Carbonara", Price = 14.50m,
            Category = "Pasta", IsAvailable = true
        };
        var soup = new Dish
        {
            Name = "Soup", Price = 8m,
            Category = "Soups", IsAvailable = true
        };
        db.Dishes.AddRange(carbonara, soup);
        await db.SaveChangesAsync();

        db.TableStaffAssignments.Add(new TableStaffAssignment
        {
            RestaurantId = restaurant.Id,
            DiningTableId = table.Id,
            StaffMembershipId = waiter.Id
        });
        await db.SaveChangesAsync();

        return new(options, restaurant.Id, table.Id,
            waiter.Id, anotherWaiter.Id, carbonara.Id, soup.Id);
    }

    [Fact]
    public async Task DifferentSeatsCanHaveSeparateActiveOrdersOnSameTable()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var seat1 = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 1);
        var seat2 = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 2);
        Assert.Equal(OrderWriteStatus.Created, seat1.Status);
        Assert.Equal(OrderWriteStatus.Created, seat2.Status);
        Assert.NotEqual(seat1.OrderId, seat2.OrderId);
        Assert.Equal(2, await db.Orders.CountAsync());

        await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, seat1.OrderId!.Value, f.SoupId, 1, null);
        Assert.Equal(OrderWriteStatus.Sent,
            (await service.SendAsync(f.RestaurantId, f.WaiterId, seat1.OrderId.Value)).Status);

        var reopen = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 1);
        Assert.Equal(OrderWriteStatus.ExistingOpen, reopen.Status);
        Assert.Equal(seat1.OrderId, reopen.OrderId);
        Assert.Equal(OrderWriteStatus.ItemAdded,
            (await service.AddItemAsync(f.RestaurantId, f.WaiterId,
                reopen.OrderId!.Value, f.CarbonaraId, 1, null)).Status);

        var view = await new Restaurant.Api.Services.Waiter.WaiterTableQueryService(db)
            .GetVisibleOrdersAsync(f.RestaurantId, f.WaiterId, f.TableId);
        Assert.NotNull(view);
        Assert.Equal(new[] { 1, 2 }, view.Select(o => o.SeatNumber!.Value).OrderBy(x => x));
        Assert.Equal(OrderWriteStatus.SeatRequired,
            (await service.GetOrCreateOpenAsync(f.RestaurantId, f.WaiterId, f.TableId)).Status);
    }

    [Fact]
    public async Task InvalidSeatsAndAmbiguousOldOrdersReturnErrorsNotWrongOrder()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        Assert.Equal(OrderWriteStatus.InvalidSeat,
            (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 0)).Status);
        Assert.Equal(OrderWriteStatus.InvalidSeat,
            (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 5)).Status);

        db.Orders.AddRange(
            new RestaurantOrder { RestaurantId = f.RestaurantId,
                DiningTableId = f.TableId, CreatedByStaffMembershipId = f.WaiterId },
            new RestaurantOrder { RestaurantId = f.RestaurantId,
                DiningTableId = f.TableId, CreatedByStaffMembershipId = f.WaiterId });
        await db.SaveChangesAsync();

        Assert.Equal(OrderWriteStatus.AmbiguousActiveOrders,
            (await service.GetOrCreateOpenAsync(f.RestaurantId, f.WaiterId, f.TableId)).Status);
        Assert.Equal(2, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task CreateReturnsSameOpenOrderForTheSameTable()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);

        var first = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        var second = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);

        Assert.Equal(OrderWriteStatus.Created, first.Status);
        Assert.Equal(OrderWriteStatus.ExistingOpen, second.Status);
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Single(await db.Orders.ToListAsync());

        var stored = await db.Orders.SingleAsync();
        Assert.Equal(RestaurantOrderStates.Open, stored.Status);
        Assert.Equal(f.WaiterId, stored.CreatedByStaffMembershipId);
        Assert.Equal(f.RestaurantId, stored.RestaurantId);
        Assert.Equal(f.TableId, stored.DiningTableId);
    }

    [Fact]
    public async Task LineItemsPersistWithIndividualQuantitiesRequestsAndSnapshots()
    {
        var f = await CreateFixtureAsync();
        int orderId;
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var service = new OrderDraftPersistenceService(db);
            orderId = (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;

            var first = await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId,
                f.CarbonaraId, 2, null);
            var second = await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId,
                f.SoupId, 1, " no onions ");

            Assert.Equal(OrderWriteStatus.ItemAdded, first.Status);
            Assert.Equal(OrderWriteStatus.ItemAdded, second.Status);
        }

        await using (var db = new RestaurantDbContext(f.Options))
        {
            var lines = await db.OrderItems.AsNoTracking()
                .Where(x => x.OrderId == orderId)
                .OrderBy(x => x.DishId).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, x => x.DishId == f.CarbonaraId &&
                x.Quantity == 2 && x.SpecialRequest == null &&
                x.DishNameSnapshot == "Carbonara" &&
                x.UnitPriceSnapshot == 14.50m);
            Assert.Contains(lines, x => x.DishId == f.SoupId &&
                x.Quantity == 1 && x.SpecialRequest == "no onions" &&
                x.DishNameSnapshot == "Soup");
        }
    }

    [Fact]
    public async Task InvalidQuantityAndUnassignedWaiterAreRejected()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);

        var forbidden = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.OtherWaiterId, f.TableId);
        Assert.Equal(OrderWriteStatus.NotAccessible, forbidden.Status);

        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        var invalid = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.SoupId, 0, "no onions");
        Assert.Equal(OrderWriteStatus.InvalidItem, invalid.Status);
        Assert.Empty(await db.OrderItems.ToListAsync());
    }

    [Fact]
    public async Task EmptyDraftCannotBeSent()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);

        var sent = await service.SendAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value);

        Assert.Equal(OrderWriteStatus.EmptyOrder, sent.Status);
        Assert.Equal(RestaurantOrderStates.Open,
            (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task SendIsDurableIdempotentAndAllowsAdditionalPendingItems()
    {
        var f = await CreateFixtureAsync();
        int orderId;
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var service = new OrderDraftPersistenceService(db);
            orderId = (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
            await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.CarbonaraId, 2, null);
            var first = await service.SendAsync(
                f.RestaurantId, f.WaiterId, orderId);
            var second = await service.SendAsync(
                f.RestaurantId, f.WaiterId, orderId);
            var lateAdd = await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.SoupId, 1, null);

            Assert.Equal(OrderWriteStatus.Sent, first.Status);
            Assert.NotNull(first.SubmissionId);
            Assert.Equal(OrderWriteStatus.AlreadySent, second.Status);
            Assert.Equal(first.SubmissionId, second.SubmissionId);
            Assert.Equal(OrderWriteStatus.ItemAdded, lateAdd.Status);
            Assert.Equal(OrderWriteStatus.ExistingOpen,
                (await service.GetOrCreateOpenAsync(
                    f.RestaurantId, f.WaiterId, f.TableId)).Status);
            Assert.Equal(OrderWriteStatus.Sent, (await service.SendAsync(
                f.RestaurantId, f.WaiterId, orderId)).Status);
        }

        await using (var db = new RestaurantDbContext(f.Options))
        {
            var saved = await db.Orders.AsNoTracking()
                .SingleAsync(x => x.Id == orderId);
            Assert.Equal(RestaurantOrderStates.Sent, saved.Status);
            Assert.NotNull(saved.SentAtUtc);
            Assert.True(saved.SentAtUtc!.Value >= saved.CreatedAtUtc);
            Assert.Equal(2, await db.OrderItems.CountAsync());
            Assert.Equal(2, await db.KitchenSubmissions.CountAsync());
            Assert.All(await db.OrderItems.ToListAsync(),
                i => Assert.NotNull(i.KitchenSubmissionId));
        }
    }

    [Fact]
    public async Task DishUnavailableAtSendTimeKeepsOrderOpen()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.SoupId, 1, null);

        var dish = await db.Dishes.SingleAsync(d => d.Id == f.SoupId);
        dish.IsAvailable = false;
        await db.SaveChangesAsync();

        var result = await service.SendAsync(
            f.RestaurantId, f.WaiterId, order.OrderId.Value);
        Assert.Equal(OrderWriteStatus.DishUnavailable, result.Status);
        Assert.Equal(RestaurantOrderStates.Open,
            (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReassignmentTransfersDraftControlButPreservesCreator()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);

        var assignment = await db.TableStaffAssignments.SingleAsync();
        assignment.StaffMembershipId = f.OtherWaiterId;
        await db.SaveChangesAsync();

        var oldAccess = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.SoupId, 1, null);
        var newAccess = await service.AddItemAsync(
            f.RestaurantId, f.OtherWaiterId, order.OrderId.Value,
            f.SoupId, 1, "no onions");

        Assert.Equal(OrderWriteStatus.NotAccessible, oldAccess.Status);
        Assert.Equal(OrderWriteStatus.ItemAdded, newAccess.Status);
        Assert.Equal(f.WaiterId,
            (await db.Orders.SingleAsync()).CreatedByStaffMembershipId);
    }

    [Fact]
    public async Task AmendmentsAreAuditedWithBeforeAndAfterSnapshots()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        var added = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.CarbonaraId, 2, "no cheese", reason: "Customer added pasta");
        Assert.Equal(OrderWriteStatus.ItemAdded, added.Status);

        var updated = await service.UpdateItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId.Value,
            added.ItemId!.Value, 3, "extra cheese", reason: "Customer changed quantity");
        Assert.Equal(OrderWriteStatus.ItemUpdated, updated.Status);

        var audits = await db.OrderChangeAudits.AsNoTracking()
            .OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal(OrderChangeTypes.Amended, a.ChangeType));
        Assert.Equal(f.WaiterId, audits[1].ChangedByStaffMembershipId);
        using var before = System.Text.Json.JsonDocument.Parse(audits[1].BeforeSnapshotJson);
        using var after = System.Text.Json.JsonDocument.Parse(audits[1].AfterSnapshotJson!);
        Assert.Equal(2, before.RootElement.GetProperty("Items")[0].GetProperty("Quantity").GetInt32());
        Assert.Equal(3, after.RootElement.GetProperty("Items")[0].GetProperty("Quantity").GetInt32());
    }

    [Fact]
    public async Task CancellationRequiresConfirmationAndPersistsAuditSnapshot()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.SoupId, 1, null);

        var notConfirmed = await service.CancelAsync(
            f.RestaurantId, f.WaiterId, order.OrderId.Value,
            false, "Customer changed their mind");
        Assert.Equal(OrderWriteStatus.CancellationNotConfirmed, notConfirmed.Status);

        var cancelled = await service.CancelAsync(
            f.RestaurantId, f.WaiterId, order.OrderId.Value,
            true, "Customer changed their mind");
        Assert.Equal(OrderWriteStatus.OrderCancelled,cancelled.Status);
        Assert.Equal(RestaurantOrderStates.Closed,
            (await db.Orders.SingleAsync()).Status);
        var audit = await db.OrderChangeAudits.SingleAsync();
        Assert.Equal(OrderChangeTypes.Cancelled, audit.ChangeType);
        Assert.Null(audit.AfterSnapshotJson);
        using var snapshot = System.Text.Json.JsonDocument.Parse(audit.BeforeSnapshotJson);
        Assert.Equal("Soup", snapshot.RootElement.GetProperty("Items")[0]
            .GetProperty("DishNameSnapshot").GetString());
    }

    [Fact]
    public async Task SubmittedKitchenOrderCannotBeCancelledThroughWaiterDraftApi()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var order = await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId);
        await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, order.OrderId!.Value,
            f.SoupId, 1, null);
        Assert.Equal(OrderWriteStatus.Sent,
            (await service.SendAsync(f.RestaurantId, f.WaiterId, order.OrderId.Value)).Status);

        var result = await service.CancelAsync(
            f.RestaurantId, f.WaiterId, order.OrderId.Value,
            true, "Customer cancelled");
        Assert.Equal(OrderWriteStatus.OrderHasKitchenItems, result.Status);
        Assert.Empty(await db.OrderChangeAudits.ToListAsync());
    }

    [Fact]
    public async Task KitchenBatchesKeepTenantSequenceAndPreparationInstructions()
    {
        var f = await CreateFixtureAsync();
        int otherRestaurantId;
        int orderId;

        await using (var db = new RestaurantDbContext(f.Options))
        {
            var other = new RestaurantAccount
            {
                Name = "Different Kitchen", Address = "Different street"
            };
            db.Restaurants.Add(other);
            await db.SaveChangesAsync();
            otherRestaurantId = other.Id;

            var service = new OrderDraftPersistenceService(db);
            orderId = (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId, seatNumber: 2))
                .OrderId!.Value;

            await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.CarbonaraId, 2,
                "no cheese");
            await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.SoupId, 1,
                "extra hot");
            Assert.Equal(OrderWriteStatus.Sent,
                (await service.SendAsync(f.RestaurantId, f.WaiterId, orderId)).Status);

            // Waiter can add another kitchen submission without closing the order.
            await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.SoupId, 3,
                "no salt");
            Assert.Equal(OrderWriteStatus.Sent,
                (await service.SendAsync(f.RestaurantId, f.WaiterId, orderId)).Status);
        }

        // A new DbContext verifies that every field really was persisted.
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var batches = await db.KitchenSubmissions.AsNoTracking()
                .Where(s => s.OrderId == orderId)
                .OrderBy(s => s.SequenceNumber).ToListAsync();
            Assert.Equal(new[] { 1, 2 }, batches.Select(s => s.SequenceNumber));
            Assert.All(batches, s => Assert.Equal(f.RestaurantId, s.RestaurantId));

            var lines = await db.OrderItems.AsNoTracking()
                .Where(i => i.OrderId == orderId)
                .OrderBy(i => i.SequenceNumber).ToListAsync();
            Assert.Equal(new[] { 1, 2, 3 }, lines.Select(i => i.SequenceNumber));
            Assert.Equal(new[] { 2, 1, 3 }, lines.Select(i => i.Quantity));
            Assert.Equal(new[] { "no cheese", "extra hot", "no salt" },
                lines.Select(i => i.SpecialRequest));

            var queries = new OrderDetailsQueryService(db);
            Assert.Equal(2, (await queries.GetKitchenSubmissionsAsync(
                f.RestaurantId, orderId)).Count);
            Assert.Empty(await queries.GetKitchenSubmissionsAsync(otherRestaurantId));
        }
    }

    [Fact]
    public async Task KitchenModelHasTenantSafeCompositeForeignKeysAndSequenceIndexes()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var kitchen = db.Model.FindEntityType(typeof(KitchenSubmission))!;
        var items = db.Model.FindEntityType(typeof(RestaurantOrderItem))!;

        var kitchenToOrder = kitchen.GetForeignKeys().Single(fk =>
            fk.PrincipalEntityType.ClrType == typeof(RestaurantOrder));
        Assert.Equal(new[] { "RestaurantId", "OrderId" },
            kitchenToOrder.Properties.Select(p => p.Name));

        var itemToSubmission = items.GetForeignKeys().Single(fk =>
            fk.PrincipalEntityType.ClrType == typeof(KitchenSubmission));
        Assert.Equal(new[] { "OrderId", "KitchenSubmissionId" },
            itemToSubmission.Properties.Select(p => p.Name));

        Assert.Contains(kitchen.GetIndexes(), ix => ix.IsUnique &&
            ix.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "OrderId", "SequenceNumber" }));
        Assert.Contains(items.GetIndexes(), ix => ix.IsUnique &&
            ix.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "OrderId", "SequenceNumber" }));
    }
}
