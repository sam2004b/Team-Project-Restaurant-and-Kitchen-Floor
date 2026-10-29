using Microsoft.AspNetCore.Mvc;
using Restaurant.Api.Controllers;
using Restaurant.Api.Dtos;
using Restaurant.Api.Services.Menu;
using Xunit;

namespace Restaurant.Api.Tests;

public class DishesControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(50)]
    public async Task GetDishes_ReturnsCorrectCount(int count)
    {
        var controller = CreateController(count);

        var response = await controller.GetAvailableDishes(
            null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(
            response.Result);

        var dishes = Assert.IsAssignableFrom<
            IReadOnlyList<MenuDishDto>>(ok.Value);

        Assert.Equal(count, dishes.Count);
    }

    [Fact]
    public async Task GetDishes_AppliesLimit()
    {
        var controller = CreateController(12);

        var response = await controller.GetAvailableDishes(
            5, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(
            response.Result);

        var dishes = Assert.IsAssignableFrom<
            IReadOnlyList<MenuDishDto>>(ok.Value);

        Assert.Equal(5, dishes.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetDishes_InvalidLimit_Returns400(
        int limit)
    {
        var controller = CreateController(12);

        var response = await controller.GetAvailableDishes(
            limit, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(
            response.Result);
    }

    private static DishesController CreateController(int count)
    {
        IReadOnlyList<MenuDishDto> dishes =
            Enumerable.Range(1, count)
                .Select(i => new MenuDishDto(
                    i,
                    $"Dish {i}",
                    10.00m,
                    "Test description",
                    "Main Course",
                    true
                ))
                .ToArray();

        return new DishesController(
            new FakeMenuReadService(dishes));
    }

    private sealed class FakeMenuReadService
        : IMenuReadService
    {
        private readonly IReadOnlyList<MenuDishDto> _dishes;

        public FakeMenuReadService(
            IReadOnlyList<MenuDishDto> dishes)
        {
            _dishes = dishes;
        }

        public Task<IReadOnlyList<MenuDishDto>>
            GetAvailableDishesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_dishes);
        }

        public Task<bool?> GetDishAvailabilityAsync(
            int dishId,
            CancellationToken cancellationToken = default)
        {
            bool? available = _dishes
                .FirstOrDefault(d => d.Id == dishId)
                ?.IsAvailable;

            return Task.FromResult(available);
        }
    }
}