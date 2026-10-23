namespace Restaurant.Api.Dtos;

public sealed record DishDetailsDto(
    int Id,
    string Name,
    decimal Price,
    string? Description,
    string Category,
    bool IsAvailable,
    IReadOnlyList<string> Ingredients
);