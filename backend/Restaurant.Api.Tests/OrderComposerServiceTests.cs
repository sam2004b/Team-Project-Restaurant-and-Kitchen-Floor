using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Orders;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class OrderComposerServiceTests
{
    private sealed record Fixture(
        DbContextOptions<RestaurantDbContext> Options,
        int RestaurantId, int OtherRestaurantId, int TableId,
        int WaiterId, int OtherWaiterId, int DishId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("composer-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new RestaurantDbContext(options);
        var restaurant = new RestaurantAccount { Name = "Bistro 21", Address = "Street 1" };
        var otherRestaurant = new RestaurantAccount { Name = "Cafe 8", Address = "Street 2" };
        db.Restaurants.AddRange(restaurant, otherRestaurant);
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
        var secondWaiter = new StaffMembership
        {
            RestaurantId = restaurant.Id,
            UserId = maria.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        db.StaffMemberships.AddRange(waiter, secondWaiter);
        var table = new DiningTable
        {
            RestaurantId = restaurant.Id,
            TableNumber = 12,
            SeatCount = 4,
            IsActive = true
        };
        db.DiningTables.Add(table);
        var soup = new Dish
        {
            Name = "Soup", Price = 8.50m, Category = "Soups", IsAvailable = true
        };
        db.Dishes.Add(soup);
        await db.SaveChangesAsync();
        db.TableStaffAssignments.Add(new TableStaffAssignment
        {
            RestaurantId = restaurant.Id,
            DiningTableId = table.Id,
            StaffMembershipId = waiter.Id
        });
        await db.SaveChangesAsync();
        return new Fixture(options, restaurant.Id, otherRestaurant.Id,
            table.Id, waiter.Id, secondWaiter.Id, soup.Id);
    }

    [Fact]
    public async Task EditLinePersistsQuantityAndDishSpecificNote()
    {
        var f = await CreateFixtureAsync();
        int orderId, itemId;
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var service = new OrderDraftPersistenceService(db);
            orderId = (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
            itemId = (await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.DishId, 1,
                "no onions")).ItemId!.Value;
            var result = await service.UpdateItemAsync(
                f.RestaurantId, f.WaiterId, orderId, itemId, 2,
                " extra pepper ");
            Assert.Equal(OrderWriteStatus.ItemUpdated, result.Status);
        }
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var detail = await new OrderDetailsQueryService(db).GetForWaiterAsync(
                f.RestaurantId, f.WaiterId, orderId);
            Assert.NotNull(detail);
            Assert.Equal(RestaurantOrderStates.Open, detail.Status);
            var item = Assert.Single(detail.Items);
            Assert.Equal(2, item.Quantity);
            Assert.Equal("extra pepper", item.SpecialRequest);
            Assert.Equal("Soup", item.DishName);
            Assert.Equal(8.50m, item.UnitPrice);
        }
    }

    [Fact]
    public async Task RemoveLineMakesOrderEmptyAndSendFails()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        var itemId = (await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, null)).ItemId!.Value;
        Assert.Equal(OrderWriteStatus.ItemRemoved, (await service.RemoveItemAsync(
            f.RestaurantId, f.WaiterId, orderId, itemId)).Status);
        Assert.Equal(OrderWriteStatus.EmptyOrder, (await service.SendAsync(
            f.RestaurantId, f.WaiterId, orderId)).Status);
        Assert.Empty(await db.OrderItems.ToListAsync());
    }

    [Fact]
    public async Task InvalidUpdateAndUnavailableDishAreRejected()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        var itemId = (await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, null)).ItemId!.Value;
        Assert.Equal(OrderWriteStatus.InvalidItem, (await service.UpdateItemAsync(
            f.RestaurantId, f.WaiterId, orderId, itemId, 0, null)).Status);
        var dish = await db.Dishes.SingleAsync(x => x.Id == f.DishId);
        dish.IsAvailable = false;
        await db.SaveChangesAsync();
        Assert.Equal(OrderWriteStatus.DishUnavailable, (await service.UpdateItemAsync(
            f.RestaurantId, f.WaiterId, orderId, itemId, 2, null)).Status);
        Assert.Equal(1, (await db.OrderItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task RepeatedSendProducesOneDurableKitchenOrderAfterReconnect()
    {
        var f = await CreateFixtureAsync();
        int orderId;
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var service = new OrderDraftPersistenceService(db);
            orderId = (await service.GetOrCreateOpenAsync(
                f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
            await service.AddItemAsync(
                f.RestaurantId, f.WaiterId, orderId, f.DishId, 2, "No onions");
            Assert.Equal(OrderWriteStatus.Sent, (await service.SendAsync(
                f.RestaurantId, f.WaiterId, orderId)).Status);
            Assert.Equal(OrderWriteStatus.AlreadySent, (await service.SendAsync(
                f.RestaurantId, f.WaiterId, orderId)).Status);
        }
        // New DbContext simulates kitchen reconnect / API process restart.
        await using (var db = new RestaurantDbContext(f.Options))
        {
            var queries = new OrderDetailsQueryService(db);
            var feed = await queries.GetKitchenFeedAsync(f.RestaurantId);
            var ticket = Assert.Single(feed);
            Assert.Equal(orderId, ticket.Id);
            Assert.Equal(RestaurantOrderStates.Sent, ticket.Status);
            Assert.NotNull(ticket.SentAtUtc);
            var line = Assert.Single(ticket.Items);
            Assert.Equal(2, line.Quantity);
            Assert.Equal("No onions", line.SpecialRequest);
        }
    }

    [Fact]
    public async Task OpenDraftDoesNotAppearInKitchenFeed()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        await service.GetOrCreateOpenAsync(f.RestaurantId, f.WaiterId, f.TableId);
        Assert.Empty(await new OrderDetailsQueryService(db)
            .GetKitchenFeedAsync(f.RestaurantId));
    }

    [Fact]
    public async Task ReassignmentTransfersDraftAccessWithoutChangingCreator()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        int orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, null);
        var assignment = await db.TableStaffAssignments.SingleAsync();
        assignment.StaffMembershipId = f.OtherWaiterId;
        await db.SaveChangesAsync();
        var queries = new OrderDetailsQueryService(db);
        Assert.Null(await queries.GetForWaiterAsync(
            f.RestaurantId, f.WaiterId, orderId));
        var detail = await queries.GetForWaiterAsync(
            f.RestaurantId, f.OtherWaiterId, orderId);
        Assert.NotNull(detail);
        Assert.Equal(f.WaiterId, detail.CreatedByStaffMembershipId);
        Assert.Equal(OrderWriteStatus.NotAccessible, (await service.SendAsync(
            f.RestaurantId, f.WaiterId, orderId)).Status);
        Assert.Equal(OrderWriteStatus.Sent, (await service.SendAsync(
            f.RestaurantId, f.OtherWaiterId, orderId)).Status);
    }

    [Fact]
    public async Task KitchenFeedIsStrictlyRestaurantScoped()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        int orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        await service.AddItemAsync(f.RestaurantId, f.WaiterId,
            orderId, f.DishId, 1, "no salt");
        await service.SendAsync(f.RestaurantId, f.WaiterId, orderId);
        var queries = new OrderDetailsQueryService(db);
        Assert.Empty(await queries.GetKitchenFeedAsync(f.OtherRestaurantId));
        Assert.Null(await queries.GetKitchenOrderAsync(f.OtherRestaurantId, orderId));
        Assert.Single(await queries.GetKitchenFeedAsync(f.RestaurantId));
    }

    [Fact]
    public async Task SentItemsCannotBeEditedButNewPendingItemsCanBeUpdatedOrDeleted()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        int orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        int itemId = (await service.AddItemAsync(f.RestaurantId, f.WaiterId,
            orderId, f.DishId, 1, null)).ItemId!.Value;
        await service.SendAsync(f.RestaurantId, f.WaiterId, orderId);
        Assert.Equal(OrderWriteStatus.ItemAlreadySubmitted, (await service.UpdateItemAsync(
            f.RestaurantId, f.WaiterId, orderId, itemId, 2, null)).Status);
        Assert.Equal(OrderWriteStatus.ItemAlreadySubmitted, (await service.RemoveItemAsync(
            f.RestaurantId, f.WaiterId, orderId, itemId)).Status);

        var newItem = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, "extra");
        Assert.Equal(OrderWriteStatus.ItemAdded, newItem.Status);
        Assert.Equal(OrderWriteStatus.ItemUpdated, (await service.UpdateItemAsync(
            f.RestaurantId, f.WaiterId, orderId, newItem.ItemId!.Value, 2, null)).Status);
        Assert.Equal(OrderWriteStatus.ItemRemoved, (await service.RemoveItemAsync(
            f.RestaurantId, f.WaiterId, orderId, newItem.ItemId.Value)).Status);
    }

    [Fact]
    public async Task SecondKitchenBatchDoesNotLeakPendingItemsOrResendOldItems()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        var queries = new OrderDetailsQueryService(db);
        int orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        var firstLine = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 2, "first");
        var firstSend = await service.SendAsync(f.RestaurantId, f.WaiterId, orderId);
        var pendingLine = await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, "extra");

        var waiterOrder = await queries.GetForWaiterAsync(f.RestaurantId, f.WaiterId, orderId);
        Assert.NotNull(waiterOrder);
        Assert.Equal(2, waiterOrder.Items.Count);
        Assert.Single((await queries.GetKitchenOrderAsync(f.RestaurantId, orderId))!.Items);
        var secondSend = await service.SendAsync(f.RestaurantId, f.WaiterId, orderId);
        Assert.NotEqual(firstSend.SubmissionId, secondSend.SubmissionId);
        var batches = await queries.GetKitchenSubmissionsAsync(f.RestaurantId, orderId);
        Assert.Equal(2, batches.Count);
        Assert.Equal(firstLine.ItemId, Assert.Single(batches[0].Items).Id);
        Assert.Equal(pendingLine.ItemId, Assert.Single(batches[1].Items).Id);
        Assert.Equal(2, (await queries.GetKitchenOrderAsync(f.RestaurantId, orderId))!.Items.Count);
        Assert.Equal(OrderWriteStatus.AlreadySent, (await service.SendAsync(
            f.RestaurantId, f.WaiterId, orderId)).Status);
        Assert.Equal(2, await db.KitchenSubmissions.CountAsync());
    }

    [Fact]
    public async Task PaidClosedOrderCannotAcceptMoreItemsAndRemainsInKitchenFeed()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new OrderDraftPersistenceService(db);
        int orderId = (await service.GetOrCreateOpenAsync(
            f.RestaurantId, f.WaiterId, f.TableId)).OrderId!.Value;
        await service.AddItemAsync(f.RestaurantId, f.WaiterId, orderId, f.DishId, 2, null);
        await service.SendAsync(f.RestaurantId, f.WaiterId, orderId);

        // Payment workflow isn't implemented in Sprint 2; emulate its final state.
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        order.Status = RestaurantOrderStates.Closed;
        order.PaidAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(OrderWriteStatus.OrderNotOpen, (await service.AddItemAsync(
            f.RestaurantId, f.WaiterId, orderId, f.DishId, 1, null)).Status);
        Assert.Equal(OrderWriteStatus.OrderNotOpen, (await service.SendAsync(
            f.RestaurantId, f.WaiterId, orderId)).Status);
        var ticket = await new OrderDetailsQueryService(db)
            .GetKitchenOrderAsync(f.RestaurantId, orderId);
        Assert.NotNull(ticket);
        Assert.Equal(RestaurantOrderStates.Closed, ticket.Status);
        Assert.Single(ticket.Items);
        var newOrder = await service.GetOrCreateOpenAsync(f.RestaurantId, f.WaiterId, f.TableId);
        Assert.NotEqual(orderId, newOrder.OrderId);
    }
}
