namespace Restaurant.Api.Dtos;

public sealed record OrderItemDto(
    int DishId,
    string DishName,
    decimal UnitPrice,
    int Quantity);

public sealed record OrderDto(
    int Id,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderItemDto> Items);
