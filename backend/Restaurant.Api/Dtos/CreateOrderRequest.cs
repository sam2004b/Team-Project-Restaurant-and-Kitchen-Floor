namespace Restaurant.Api.Dtos;

public sealed record CreateOrderRequest(
    int DishId,
    int Quantity = 1);
