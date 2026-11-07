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
    public async Task SendIsDurableIdempotentAndPreventsFurtherEdits()
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
            Assert.Equal(OrderWriteStatus.AlreadySent, second.Status);
            Assert.Equal(OrderWriteStatus.OrderNotOpen, lateAdd.Status);
        }

        await using (var db = new RestaurantDbContext(f.Options))
        {
            var saved = await db.Orders.AsNoTracking()
                .SingleAsync(x => x.Id == orderId);
            Assert.Equal(RestaurantOrderStates.Sent, saved.Status);
            Assert.NotNull(saved.SentAtUtc);
            Assert.True(saved.SentAtUtc!.Value >= saved.CreatedAtUtc);
            Assert.Single(await db.OrderItems.ToListAsync());
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
}
