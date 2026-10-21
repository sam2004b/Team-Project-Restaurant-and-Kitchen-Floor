namespace Restaurant.Api.Services.Orders;

public sealed record OrderItemCandidate(
    int DishId,
    int Quantity
);

public enum OrderValidationStatus
{
    Valid,
    EmptyOrder,
    InvalidDishId,
    InvalidQuantity,
    DishNotFound,
    DishUnavailable
}

public sealed record OrderValidationResult(
    OrderValidationStatus Status,
    int? DishId = null)
{
    public bool IsValid =>
        Status == OrderValidationStatus.Valid;
}