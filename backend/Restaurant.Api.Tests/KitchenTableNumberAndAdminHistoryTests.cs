using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Orders;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class KitchenTableNumberAndAdminHistoryTests
{
    private static DbContextOptions<RestaurantDbContext> Options() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase($"kitchen-history-{Guid.NewGuid():N}").Options;

    [Fact]
    public async Task KitchenSubmissionExposesActualTableNumberOnlyForItsRestaurant()
    {
        await using var db = new RestaurantDbContext(Options());
        var sentAt = DateTime.UtcNow;
        db.DiningTables.AddRange(
            new DiningTable { Id = 201, RestaurantId = 1, TableNumber = 7, SeatCount = 4 },
            new DiningTable { Id = 202, RestaurantId = 2, TableNumber = 3, SeatCount = 4 });
        db.Orders.AddRange(
            new RestaurantOrder { Id = 51, RestaurantId = 1, DiningTableId = 201,
                CreatedByStaffMembershipId = 11, Status = RestaurantOrderStates.Sent, SentAtUtc = sentAt },
            new RestaurantOrder { Id = 52, RestaurantId = 2, DiningTableId = 202,
                CreatedByStaffMembershipId = 12, Status = RestaurantOrderStates.Sent, SentAtUtc = sentAt });
        db.KitchenSubmissions.AddRange(
            new KitchenSubmission { Id = 101, RestaurantId = 1, OrderId = 51, SequenceNumber = 1 },
            new KitchenSubmission { Id = 102, RestaurantId = 2, OrderId = 52, SequenceNumber = 1 });
        db.OrderItems.AddRange(
            new RestaurantOrderItem { Id = 301, OrderId = 51, KitchenSubmissionId = 101,
                DishId = 1, DishNameSnapshot = "Soup", Quantity = 2, SequenceNumber = 1 },
            new RestaurantOrderItem { Id = 302, OrderId = 52, KitchenSubmissionId = 102,
                DishId = 1, DishNameSnapshot = "Salad", Quantity = 1, SequenceNumber = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var tickets = await new OrderDetailsQueryService(db).GetKitchenSubmissionsAsync(1);
        var ticket = Assert.Single(tickets);
        Assert.Equal(201, ticket.DiningTableId);
        Assert.Equal(7, ticket.TableNumber);
        Assert.Equal(51, ticket.OrderId);
        Assert.Single(ticket.Items);
    }

    [Fact]
    public async Task AdministratorWaiterFilterIsAppliedBeforePagination()
    {
        await using var db = new RestaurantDbContext(Options());
        db.Users.AddRange(
            new IdentityUser { Id = "waiter-a", UserName = "Alice" },
            new IdentityUser { Id = "waiter-b", UserName = "Bob" });
        db.StaffMemberships.AddRange(
            new StaffMembership { Id = 11, RestaurantId = 1, UserId = "waiter-a", Role = StaffRoleNames.Waiter },
            new StaffMembership { Id = 12, RestaurantId = 1, UserId = "waiter-b", Role = StaffRoleNames.Waiter });
        db.DiningTables.Add(new DiningTable { Id = 201, RestaurantId = 1, TableNumber = 7, SeatCount = 4 });
        db.Orders.AddRange(
            new RestaurantOrder { Id = 51, RestaurantId = 1, DiningTableId = 201, CreatedByStaffMembershipId = 11 },
            new RestaurantOrder { Id = 52, RestaurantId = 1, DiningTableId = 201, CreatedByStaffMembershipId = 12 },
            new RestaurantOrder { Id = 53, RestaurantId = 1, DiningTableId = 201, CreatedByStaffMembershipId = 11 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var response = await new OrderHistoryQueryService(db).SearchAsync(
            restaurantId: 1, membershipId: 999, isAdministrator: true,
            page: 1, pageSize: 1, createdByStaffMembershipId: 11);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("totalPages").GetInt32());
        var items = json.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var first = items[0];
        Assert.Equal(11, first.GetProperty("CreatedByStaffMembershipId").GetInt32());
        Assert.Equal("Alice", first.GetProperty("createdByStaffName").GetString());
    }
}
