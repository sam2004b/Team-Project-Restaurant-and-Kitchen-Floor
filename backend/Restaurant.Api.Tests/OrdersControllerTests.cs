using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Controllers;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Menu;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Tests;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task CreateOrder_AvailableDish_SavesOrderAndCanBeRetrieved()
    {
        var options = CreateOptions();
        int orderId;

        await using (var db = new RestaurantDbContext(options))
        {
            db.Dishes.Add(new Dish
            {
                Name = "Carbonara",
                Price = 14.50m,
                Category = "Pasta",
                IsAvailable = true
            });
            await db.SaveChangesAsync();

            var controller = CreateController(db);
            var response = await controller.CreateOrder(
                new CreateOrderRequest(1, 2),
                CancellationToken.None);

            var created = Assert.IsType<CreatedAtActionResult>(
                response.Result);
            var order = Assert.IsType<OrderDto>(created.Value);
            orderId = order.Id;

            Assert.Equal(2, order.Items.Single().Quantity);
            Assert.Equal(14.50m, order.Items.Single().UnitPrice);
        }

        await using var reloadedDb = new RestaurantDbContext(options);
        var getController = CreateController(reloadedDb);
        var savedOrder = await getController.GetOrder(
            orderId,
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(savedOrder.Result);
        var reloadedOrder = Assert.IsType<OrderDto>(result.Value);
        Assert.Equal(orderId, reloadedOrder.Id);
        Assert.Equal("Carbonara", reloadedOrder.Items.Single().DishName);
    }

    [Fact]
    public async Task CreateOrder_DishBecameUnavailable_ReturnsConflictAndDoesNotSave()
    {
        var options = CreateOptions();

        await using var db = new RestaurantDbContext(options);
        db.Dishes.Add(new Dish
        {
            Name = "Carbonara",
            Price = 14.50m,
            Category = "Pasta",
            IsAvailable = false
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var response = await controller.CreateOrder(
            new CreateOrderRequest(1),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.OrderItems.CountAsync());
        Assert.NotNull(conflict.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateOrder_InvalidQuantity_ReturnsBadRequest(
        int quantity)
    {
        await using var db = new RestaurantDbContext(CreateOptions());
        db.Dishes.Add(new Dish
        {
            Name = "Carbonara",
            Price = 14.50m,
            Category = "Pasta",
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var response = await controller.CreateOrder(
            new CreateOrderRequest(1, quantity),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    private static OrdersController CreateController(
        RestaurantDbContext db)
    {
        var menuService = new MenuReadService(db);
        var availabilityService = new OrderAvailabilityService(menuService);
        return new OrdersController(db, availabilityService);
    }

    private static DbContextOptions<RestaurantDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
}
