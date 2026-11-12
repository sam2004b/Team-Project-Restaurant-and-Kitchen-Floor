using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Waiter;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class WaiterTableQueryServiceTests
{
    private sealed record TestData(
        DbContextOptions<RestaurantDbContext> Options,
        int BistroId, int CafeId,
        int IvanMembershipId, int MariaMembershipId, int CafeMembershipId,
        int Table3Id, int Table8Id, int Table12Id, int CafeTableId,
        int Table8OrderId, int Table12OrderId);

    private static async Task<TestData> CreateDataAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase($"table-scope-{Guid.NewGuid():N}")
            .Options;

        await using var db = new RestaurantDbContext(options);

        var bistro = new RestaurantAccount
        { Name = "Bistro 21", Address = "Bistro Street" };
        var cafe = new RestaurantAccount
        { Name = "Cafe 8", Address = "Cafe Street" };
        db.Restaurants.AddRange(bistro, cafe);

        var ivanUser = new IdentityUser { UserName = "ivan" };
        var mariaUser = new IdentityUser { UserName = "maria" };
        var cafeUser = new IdentityUser { UserName = "cafe-waiter" };
        db.Users.AddRange(ivanUser, mariaUser, cafeUser);
        await db.SaveChangesAsync();

        var ivan = new StaffMembership
        {
            RestaurantId = bistro.Id,
            UserId = ivanUser.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        var maria = new StaffMembership
        {
            RestaurantId = bistro.Id,
            UserId = mariaUser.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        var cafeWaiter = new StaffMembership
        {
            RestaurantId = cafe.Id,
            UserId = cafeUser.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        db.StaffMemberships.AddRange(ivan, maria, cafeWaiter);

        var t3 = new DiningTable
        { RestaurantId = bistro.Id, TableNumber = 3, SeatCount = 4 };
        var t8 = new DiningTable
        { RestaurantId = bistro.Id, TableNumber = 8, SeatCount = 4 };
        var t12 = new DiningTable
        { RestaurantId = bistro.Id, TableNumber = 12, SeatCount = 2 };
        var tCafe = new DiningTable
        { RestaurantId = cafe.Id, TableNumber = 3, SeatCount = 2 };
        db.DiningTables.AddRange(t3, t8, t12, tCafe);
        await db.SaveChangesAsync();

        db.TableStaffAssignments.AddRange(
            new TableStaffAssignment
            {
                RestaurantId = bistro.Id,
                DiningTableId = t3.Id,
                StaffMembershipId = ivan.Id
            },
            new TableStaffAssignment
            {
                RestaurantId = bistro.Id,
                DiningTableId = t12.Id,
                StaffMembershipId = ivan.Id
            },
            new TableStaffAssignment
            {
                RestaurantId = bistro.Id,
                DiningTableId = t8.Id,
                StaffMembershipId = maria.Id
            },
            new TableStaffAssignment
            {
                RestaurantId = cafe.Id,
                DiningTableId = tCafe.Id,
                StaffMembershipId = cafeWaiter.Id
            });

        var order = new RestaurantOrder
        {
            RestaurantId = bistro.Id,
            DiningTableId = t12.Id,
            CreatedByStaffMembershipId = ivan.Id
        };
        var order8 = new RestaurantOrder
        {
            RestaurantId = bistro.Id,
            DiningTableId = t8.Id,
            CreatedByStaffMembershipId = maria.Id
        };
        var cafeOrder = new RestaurantOrder
        {
            RestaurantId = cafe.Id,
            DiningTableId = tCafe.Id,
            CreatedByStaffMembershipId = cafeWaiter.Id
        };
        db.Orders.AddRange(order, order8, cafeOrder);
        await db.SaveChangesAsync();

        return new TestData(
            options, bistro.Id, cafe.Id,
            ivan.Id, maria.Id, cafeWaiter.Id,
            t3.Id, t8.Id, t12.Id, tCafe.Id, order8.Id, order.Id);
    }

    [Fact]
    public async Task Ivan_OnlySeesHisTablesInBistro21()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(db);

        var tables = await service.GetAssignedTablesAsync(
            t.BistroId, t.IvanMembershipId);
        Assert.Equal(new[] { 3, 12 },
            tables.Select(x => x.TableNumber).ToArray());
        Assert.DoesNotContain(tables, x => x.Id == t.CafeTableId);
    }

    [Fact]
    public async Task ReassigningTable12_TransfersItsOrderVisibility()
    {
        var t = await CreateDataAsync();
        await using (var db = new RestaurantDbContext(t.Options))
        {
            var assignment = await db.TableStaffAssignments.SingleAsync(a =>
                a.RestaurantId == t.BistroId && a.DiningTableId == t.Table12Id);
            assignment.StaffMembershipId = t.MariaMembershipId;
            assignment.AssignedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = new RestaurantDbContext(t.Options))
        {
            var service = new WaiterTableQueryService(db);
            var ivanTables = await service.GetAssignedTablesAsync(
                t.BistroId, t.IvanMembershipId);
            Assert.Single(ivanTables);
            Assert.Equal(t.Table3Id, ivanTables[0].Id);
            Assert.Null(await service.GetVisibleOrdersAsync(
                t.BistroId, t.IvanMembershipId, t.Table12Id));

            var mariaTables = await service.GetAssignedTablesAsync(
                t.BistroId, t.MariaMembershipId);
            Assert.Equal(new[] { 8, 12 },
                mariaTables.Select(x => x.TableNumber).ToArray());
            Assert.Contains(mariaTables, x => x.Id == t.Table12Id);
            var mariaOrders = await service.GetVisibleOrdersAsync(
                t.BistroId, t.MariaMembershipId, t.Table12Id);
            Assert.NotNull(mariaOrders);
            Assert.Contains(mariaOrders!, x => x.Id == t.Table12OrderId);

            // Reassignment changes current visibility, not historical authorship.
            var storedOrder = await db.Orders.SingleAsync(o =>
                o.Id == t.Table12OrderId);
            Assert.Equal(t.IvanMembershipId,
                storedOrder.CreatedByStaffMembershipId);
        }
    }

    [Fact]
    public async Task OtherRestaurantOrdersAreNotVisible()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(db);

        Assert.Null(await service.GetVisibleOrdersAsync(
            t.BistroId, t.IvanMembershipId, t.CafeTableId));
        Assert.Empty(await service.GetAssignedTablesAsync(
            t.CafeId, t.IvanMembershipId));
    }

    [Fact]
    public async Task RevokedWaiterLosesTablesAndOrders()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var member = await db.StaffMemberships.SingleAsync(m =>
            m.Id == t.IvanMembershipId);
        member.Status = MembershipStatus.Revoked;
        await db.SaveChangesAsync();

        var service = new WaiterTableQueryService(db);
        Assert.Empty(await service.GetAssignedTablesAsync(
            t.BistroId, t.IvanMembershipId));
        Assert.Null(await service.GetVisibleOrdersAsync(
            t.BistroId, t.IvanMembershipId, t.Table12Id));
    }

    [Fact]
    public async Task DirectTableLookup_DoesNotExposeAnotherWaitersOrRestaurantsTable()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(db);

        Assert.NotNull(await service.GetAssignedTableAsync(
            t.BistroId, t.IvanMembershipId, t.Table3Id));
        Assert.Null(await service.GetAssignedTableAsync(
            t.BistroId, t.IvanMembershipId, t.Table8Id));
        Assert.Null(await service.GetAssignedTableAsync(
            t.BistroId, t.IvanMembershipId, t.CafeTableId));
    }

    [Fact]
    public async Task AllOrdersList_OnlyIncludesOrdersFromAssignedTables()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(db);

        var ivanOrders = await service.GetAssignedOrdersAsync(
            t.BistroId, t.IvanMembershipId);
        Assert.Single(ivanOrders);
        Assert.Equal(t.Table12OrderId, ivanOrders[0].Id);

        var mariaOrders = await service.GetAssignedOrdersAsync(
            t.BistroId, t.MariaMembershipId);
        Assert.Single(mariaOrders);
        Assert.Equal(t.Table8OrderId, mariaOrders[0].Id);

        Assert.Empty(await service.GetAssignedOrdersAsync(
            t.CafeId, t.IvanMembershipId));
    }

    [Fact]
    public async Task DirectOrderLookup_PreventsGuessedOrderIds()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(db);

        Assert.NotNull(await service.GetVisibleOrderAsync(
            t.BistroId, t.IvanMembershipId, t.Table12OrderId));
        Assert.Null(await service.GetVisibleOrderAsync(
            t.BistroId, t.IvanMembershipId, t.Table8OrderId));
        Assert.Null(await service.GetVisibleOrderAsync(
            t.BistroId, t.IvanMembershipId, 999999));
    }

    [Fact]
    public async Task Reassignment_ImmediatelyChangesBothListsAndDirectIdAccess()
    {
        var t = await CreateDataAsync();
        await using (var db = new RestaurantDbContext(t.Options))
        {
            var assignment = await db.TableStaffAssignments.SingleAsync(a =>
                a.RestaurantId == t.BistroId &&
                a.DiningTableId == t.Table12Id);
            assignment.StaffMembershipId = t.MariaMembershipId;
            assignment.AssignedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var readDb = new RestaurantDbContext(t.Options);
        var service = new WaiterTableQueryService(readDb);
        var ivanTables = await service.GetAssignedTablesAsync(
            t.BistroId, t.IvanMembershipId);
        Assert.Single(ivanTables);
        Assert.Equal(3, ivanTables[0].TableNumber);
        Assert.Null(await service.GetAssignedTableAsync(
            t.BistroId, t.IvanMembershipId, t.Table12Id));

        var ivanOrders = await service.GetAssignedOrdersAsync(
            t.BistroId, t.IvanMembershipId);
        Assert.Empty(ivanOrders);
        Assert.Null(await service.GetVisibleOrderAsync(
            t.BistroId, t.IvanMembershipId, t.Table12OrderId));

        var mariaTables = await service.GetAssignedTablesAsync(
            t.BistroId, t.MariaMembershipId);
        Assert.Equal(new[] { 8, 12 },
            mariaTables.Select(x => x.TableNumber).ToArray());
        var mariaOrders = await service.GetAssignedOrdersAsync(
            t.BistroId, t.MariaMembershipId);
        Assert.Equal(2, mariaOrders.Count);
        Assert.Contains(mariaOrders, o => o.Id == t.Table12OrderId);
        Assert.NotNull(await service.GetVisibleOrderAsync(
            t.BistroId, t.MariaMembershipId, t.Table12OrderId));

        // Keep the original order creator unchanged.
        var original = await readDb.Orders.SingleAsync(o =>
            o.Id == t.Table12OrderId);
        Assert.Equal(t.IvanMembershipId,
            original.CreatedByStaffMembershipId);
    }

    [Fact]
    public async Task RevokedWaiter_CannotSeeAllOrdersOrDirectOrder()
    {
        var t = await CreateDataAsync();
        await using var db = new RestaurantDbContext(t.Options);
        var member = await db.StaffMemberships.SingleAsync(m =>
            m.Id == t.IvanMembershipId);
        member.Status = MembershipStatus.Revoked;
        await db.SaveChangesAsync();

        var service = new WaiterTableQueryService(db);
        Assert.Empty(await service.GetAssignedOrdersAsync(
            t.BistroId, t.IvanMembershipId));
        Assert.Null(await service.GetVisibleOrderAsync(
            t.BistroId, t.IvanMembershipId, t.Table12OrderId));
    }

    [Fact]
    public void Responses_KeepStableJsonFieldNames()
    {
        var options = new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web);

        using var table = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(
                new AssignedTableView(1, 3, 4), options));
        Assert.Equal(3, table.RootElement.EnumerateObject().Count());
        Assert.Equal(1, table.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(3, table.RootElement.GetProperty("tableNumber").GetInt32());
        Assert.Equal(4, table.RootElement.GetProperty("seatCount").GetInt32());

        using var order = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(
                new TableOrderView(9, 1, DateTime.UtcNow), options));
        Assert.Equal(4, order.RootElement.EnumerateObject().Count());
        Assert.Equal(9, order.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(1, order.RootElement.GetProperty("diningTableId").GetInt32());
        Assert.True(order.RootElement.TryGetProperty("createdAtUtc", out _));
        Assert.Equal(
            RestaurantOrderStates.Legacy,
            order.RootElement.GetProperty("status").GetString());
        Assert.False(order.RootElement.TryGetProperty("seatNumber", out _));

        using var seatedOrder = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(
                new TableOrderView(10, 1, DateTime.UtcNow,
                    RestaurantOrderStates.Open, 2), options));
        Assert.Equal(2, seatedOrder.RootElement.GetProperty("seatNumber").GetInt32());
    }
}
