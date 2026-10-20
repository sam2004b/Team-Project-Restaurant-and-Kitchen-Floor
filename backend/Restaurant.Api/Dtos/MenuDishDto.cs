
namespace Restaurant.Api.Dtos;

public sealed record MenuDishDto(
    int Id,
    string Name,
    decimal Price,
    string? Description,
    string Category,
    bool IsAvailable
);