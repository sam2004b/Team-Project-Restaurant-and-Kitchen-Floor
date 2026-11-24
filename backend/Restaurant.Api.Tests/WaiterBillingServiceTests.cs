using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Dtos.Billing;
using Restaurant.Api.Services.Billing;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class WaiterBillingServiceTests
{
    private sealed record Fixture(
        DbContextOptions<RestaurantDbContext> Options,
        int RestaurantId, int WaiterId, int TableId, int OrderId, int ItemId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("waiter-billing-" + Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new RestaurantDbContext(options);
        var restaurant = new RestaurantAccount
        {
            Name = "Billing Bistro",
            Address = "Billing Street"
        };
        db.Restaurants.Add(restaurant);
        var user = new IdentityUser { UserName = "billing-waiter" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var waiter = new StaffMembership
        {
            RestaurantId = restaurant.Id,
            UserId = user.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        var table = new DiningTable
        {
            RestaurantId = restaurant.Id,
            TableNumber = 7,
            SeatCount = 4,
            IsActive = true
        };
        var dish = new Dish
        {
            Name = "Pasta",
            Price = 12.50m,
            Category = "Main",
            IsAvailable = true
        };
        db.StaffMemberships.Add(waiter);
        db.DiningTables.Add(table);
        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        db.TableStaffAssignments.Add(new TableStaffAssignment
        {
            RestaurantId = restaurant.Id,
            DiningTableId = table.Id,
            StaffMembershipId = waiter.Id
        });
        var order = new RestaurantOrder
        {
            RestaurantId = restaurant.Id,
            DiningTableId = table.Id,
            CreatedByStaffMembershipId = waiter.Id,
            Status = RestaurantOrderStates.Sent,
            SentAtUtc = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = new RestaurantOrderItem
        {
            OrderId = order.Id,
            DishId = dish.Id,
            SequenceNumber = 1,
            Quantity = 2,
            DishNameSnapshot = "Pasta",
            UnitPriceSnapshot = 10m,
            PreparationStatus = DishPreparationStates.Ready
        };
        db.OrderItems.Add(item);
        await db.SaveChangesAsync();

        return new(options, restaurant.Id, waiter.Id, table.Id, order.Id, item.Id);
    }

    [Fact]
    public async Task CreateBillUsesAgreedOrderPriceAndIsIdempotent()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new WaiterBillingService(db);

        var created = await service.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "usd");
        var retry = await service.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");

        Assert.Equal(BillingOperationStatus.Success, created.Status);
        Assert.Equal(BillingOperationStatus.AlreadyExists, retry.Status);
        Assert.Equal(created.Bill!.Id, retry.Bill!.Id);
        Assert.Equal(20m, created.Bill.SubtotalAmount);
        Assert.Equal(20m, created.Bill.TotalAmount);
        Assert.Equal("USD", created.Bill.CurrencyCode);
        Assert.Equal(2, Assert.Single(created.Bill.Items).Quantity);
    }

    [Fact]
    public async Task PaymentIsIdempotentAndClosesOrderWhenFullyPaid()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new WaiterBillingService(db);
        var bill = await service.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");

        var first = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill!.Id,
            10m, PaymentRecordMethods.Cash, "receipt-001");
        var retry = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill.Id,
            10m, PaymentRecordMethods.Cash, "receipt-001");
        var final = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill.Id,
            10m, PaymentRecordMethods.Card, "receipt-002");

        Assert.Equal(BillingOperationStatus.Success, first.Status);
        Assert.Equal(BillingOperationStatus.AlreadyExists, retry.Status);
        Assert.Equal(first.Payment!.Id, retry.Payment!.Id);
        Assert.Equal(BillingOperationStatus.Success, final.Status);
        Assert.Equal("Paid", final.Bill!.PaymentStatus);
        var order = await db.Orders.SingleAsync(o => o.Id == f.OrderId);
        Assert.Equal(RestaurantOrderStates.Closed, order.Status);
        Assert.NotNull(order.PaidAtUtc);
        Assert.Equal(2, await db.PaymentRecords.CountAsync());
    }

    [Fact]
    public async Task PaymentCannotExceedOutstandingBalance()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new WaiterBillingService(db);
        var bill = await service.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");

        var result = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill!.Id,
            20.01m, PaymentRecordMethods.Cash, "receipt-over");

        Assert.Equal(BillingOperationStatus.Overpayment, result.Status);
        Assert.Empty(await db.PaymentRecords.ToListAsync());
    }

    [Fact]
    public async Task CancelledOrderCannotAcceptPayment()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var service = new WaiterBillingService(db);
        var bill = await service.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");
        var order = await db.Orders.SingleAsync(o => o.Id == f.OrderId);
        order.Status = RestaurantOrderStates.Cancelled;
        await db.SaveChangesAsync();

        var result = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill!.Id,
            10m, PaymentRecordMethods.Cash, "cancelled-order-payment");

        Assert.Equal(BillingOperationStatus.OrderNotPayable, result.Status);
        Assert.Empty(await db.PaymentRecords.ToListAsync());
    }
}
