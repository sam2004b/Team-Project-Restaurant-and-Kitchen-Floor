using Restaurant.Api.Services.Menu;
namespace Restaurant.Api.Services.Orders;

public sealed class OrderAvailabilityService
    : IOrderAvailabilityService
{
    private readonly IMenuReadService _menuReadService;

    public OrderAvailabilityService(
        IMenuReadService menuReadService)
    {
        _menuReadService = menuReadService;
    }

    public async Task<OrderValidationResult> ValidateAsync(
        IReadOnlyCollection<OrderItemCandidate>? items,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return new OrderValidationResult(
                OrderValidationStatus.EmptyOrder);
        }

        foreach (var item in items)
        {
            if (item.DishId <= 0)
            {
                return new OrderValidationResult(
                    OrderValidationStatus.InvalidDishId,
                    item.DishId);
            }

            if (item.Quantity <= 0)
            {
                return new OrderValidationResult(
                    OrderValidationStatus.InvalidQuantity,
                    item.DishId);
            }

            bool? available =
                await _menuReadService
                    .GetDishAvailabilityAsync(
                        item.DishId,
                        cancellationToken);

            if (available == null)
            {
                return new OrderValidationResult(
                    OrderValidationStatus.DishNotFound,
                    item.DishId);
            }

            if (available == false)
            {
                return new OrderValidationResult(
                    OrderValidationStatus.DishUnavailable,
                    item.DishId);
            }
        }

        return new OrderValidationResult(
            OrderValidationStatus.Valid);
    }
}