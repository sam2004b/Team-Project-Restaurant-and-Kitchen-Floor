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

        var submission = new KitchenSubmission
        {
            RestaurantId = restaurant.Id,
            OrderId = order.Id,
            SequenceNumber = 1,
            SentAtUtc = order.SentAtUtc!.Value
        };
        db.KitchenSubmissions.Add(submission);
        await db.SaveChangesAsync();

        var item = new RestaurantOrderItem
        {
            KitchenSubmissionId = submission.Id,
            OrderId = order.Id,
            DishId = dish.Id,
            SequenceNumber = 1,
            Quantity = 2,
            DishNameSnapshot = "Pasta",
            UnitPriceSnapshot = 10m,
            PreparationStatus = DishPreparationStates.Ready,
            PreparationStartedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            ReadyAtUtc = DateTime.UtcNow.AddMinutes(-2),
            ServiceStatus = DishServiceStates.Served,
            ServedAtUtc = DateTime.UtcNow.AddMinutes(-1)
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

        // A partial payment must leave the order open.
        Assert.Equal(BillingOperationStatus.Success, first.Status);
        Assert.Equal(RestaurantOrderStates.Sent,
            (await db.Orders.SingleAsync(o => o.Id == f.OrderId)).Status);

        var retry = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill.Id,
            10m, PaymentRecordMethods.Cash, "receipt-001");
        var final = await service.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill.Id,
            10m, PaymentRecordMethods.Card, "receipt-002");

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

    [Fact]
    public async Task BillCannotBeCreatedBeforeKitchenDishIsServed()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var item = await db.OrderItems.SingleAsync(i => i.Id == f.ItemId);
        item.ServiceStatus = DishServiceStates.Unserved;
        item.ServedAtUtc = null;
        await db.SaveChangesAsync();

        var result = await new WaiterBillingService(db).CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");

        Assert.Equal(BillingOperationStatus.OrderNotPayable, result.Status);
        Assert.Contains("served", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.Bills.ToListAsync());
    }

    [Fact]
    public async Task BillRequiresEveryDishLineToBeServed()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var firstItem = await db.OrderItems.SingleAsync(i => i.Id == f.ItemId);
        var secondItem = new RestaurantOrderItem
        {
            OrderId = f.OrderId,
            KitchenSubmissionId = firstItem.KitchenSubmissionId,
            DishId = firstItem.DishId,
            SequenceNumber = 2,
            Quantity = 1,
            DishNameSnapshot = "Pasta",
            UnitPriceSnapshot = 10m,
            PreparationStatus = DishPreparationStates.Ready,
            PreparationStartedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            ReadyAtUtc = DateTime.UtcNow.AddMinutes(-2),
            ServiceStatus = DishServiceStates.Unserved
        };
        db.OrderItems.Add(secondItem);
        await db.SaveChangesAsync();
        var billing = new WaiterBillingService(db);

        var blocked = await billing.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");
        Assert.Equal(BillingOperationStatus.OrderNotPayable, blocked.Status);

        secondItem.ServiceStatus = DishServiceStates.Served;
        secondItem.ServedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var result = await billing.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");

        Assert.Equal(BillingOperationStatus.Success, result.Status);
        Assert.Equal(30m, result.Bill!.TotalAmount);
    }

    [Fact]
    public async Task WaiterOrderDetailsExposeIndependentServedState()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);

        var details = await new Restaurant.Api.Services.Orders.OrderDetailsQueryService(db)
            .GetForWaiterAsync(f.RestaurantId, f.WaiterId, f.OrderId);

        var item = Assert.Single(details!.Items);
        Assert.Equal(DishServiceStates.Served, item.ServiceStatus);
        Assert.NotNull(item.ServedAtUtc);
    }

    [Fact]
    public async Task ExistingBillCannotBePaidIfDishIsNoLongerServed()
    {
        var f = await CreateFixtureAsync();
        await using var db = new RestaurantDbContext(f.Options);
        var billing = new WaiterBillingService(db);
        var bill = await billing.CreateBillAsync(
            f.RestaurantId, f.WaiterId, f.OrderId, "USD");
        var item = await db.OrderItems.SingleAsync(i => i.Id == f.ItemId);
        item.ServiceStatus = DishServiceStates.Unserved;
        item.ServedAtUtc = null;
        await db.SaveChangesAsync();

        var payment = await billing.RecordPaymentAsync(
            f.RestaurantId, f.WaiterId, bill.Bill!.Id,
            20m, PaymentRecordMethods.Cash, "unserved-safeguard");

        Assert.Equal(BillingOperationStatus.OrderNotPayable, payment.Status);
        Assert.Empty(await db.PaymentRecords.ToListAsync());
        Assert.Equal(RestaurantOrderStates.Sent,
            (await db.Orders.SingleAsync(o => o.Id == f.OrderId)).Status);
    }
}
