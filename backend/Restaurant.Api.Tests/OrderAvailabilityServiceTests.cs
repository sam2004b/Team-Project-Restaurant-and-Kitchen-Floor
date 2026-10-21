using Restaurant.Api.Dtos;
using Restaurant.Api.Services.Menu;
using Restaurant.Api.Services.Orders;
using Xunit;

namespace Restaurant.Api.Tests;

public class OrderAvailabilityServiceTests
{
    [Fact]
    public async Task AvailableDish_IsAccepted()
    {
        var menu = new FakeMenuReadService();
        menu.SetAvailability(1, true);

        var service = new OrderAvailabilityService(menu);

        var result = await service.ValidateAsync(
            new[] { new OrderItemCandidate(1, 2) });

        Assert.Equal(
            OrderValidationStatus.Valid,
            result.Status);
    }

    [Fact]
    public async Task UnavailableDish_IsRejected()
    {
        var menu = new FakeMenuReadService();
        menu.SetAvailability(1, false);

        var service = new OrderAvailabilityService(menu);

        var result = await service.ValidateAsync(
            new[] { new OrderItemCandidate(1, 1) });

        Assert.Equal(
            OrderValidationStatus.DishUnavailable,
            result.Status);
    }

    [Fact]
    public async Task NonexistentDish_IsRejected()
    {
        var service = new OrderAvailabilityService(
            new FakeMenuReadService());

        var result = await service.ValidateAsync(
            new[] { new OrderItemCandidate(999, 1) });

        Assert.Equal(
            OrderValidationStatus.DishNotFound,
            result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidQuantity_IsRejected(int quantity)
    {
        var menu = new FakeMenuReadService();
        menu.SetAvailability(1, true);

        var service = new OrderAvailabilityService(menu);

        var result = await service.ValidateAsync(
            new[] { new OrderItemCandidate(1, quantity) });

        Assert.Equal(
            OrderValidationStatus.InvalidQuantity,
            result.Status);
    }

    [Fact]
    public async Task BecomesUnavailable_IsRejectedOnNextCheck()
    {
        var menu = new FakeMenuReadService();
        menu.SetAvailability(1, true);

        var service = new OrderAvailabilityService(menu);
        var items = new[] { new OrderItemCandidate(1, 1) };

        var first = await service.ValidateAsync(items);
        Assert.True(first.IsValid);

        menu.SetAvailability(1, false);

        var second = await service.ValidateAsync(items);

        Assert.Equal(
            OrderValidationStatus.DishUnavailable,
            second.Status);
    }

    [Fact]
    public async Task AvailableAgain_IsAccepted()
    {
        var menu = new FakeMenuReadService();
        menu.SetAvailability(1, false);

        var service = new OrderAvailabilityService(menu);

        menu.SetAvailability(1, true);

        var result = await service.ValidateAsync(
            new[] { new OrderItemCandidate(1, 1) });

        Assert.True(result.IsValid);
    }

    private sealed class FakeMenuReadService : IMenuReadService
    {
        private readonly Dictionary<int, bool> _availability = new();

        public void SetAvailability(int id, bool available)
        {
            _availability[id] = available;
        }

        public Task<bool?> GetDishAvailabilityAsync(
            int dishId,
            CancellationToken cancellationToken = default)
        {
            bool? result = _availability.TryGetValue(
                dishId, out var available)
                ? available
                : null;

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<MenuDishDto>>
            GetAvailableDishesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MenuDishDto>>(
                Array.Empty<MenuDishDto>());
        }
    }
}