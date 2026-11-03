using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;

namespace Restaurant.Api.Services.Dishes;

public sealed record OrderDishSelection(
    int DishId,
    int Quantity
);

public sealed record OrderAvailabilityCheck(
    bool IsValid,
    string? Error
);

public sealed class OrderDishAvailabilityValidator
{
    private readonly RestaurantDbContext _db;

    public OrderDishAvailabilityValidator(
        RestaurantDbContext db)
    {
        _db = db;
    }

    public async Task<OrderAvailabilityCheck> ValidateAsync(
        IReadOnlyCollection<OrderDishSelection> items,
        CancellationToken ct = default)
    {
        // 1. Reject empty orders.
        if (items.Count == 0)
        {
            return new(
                false,
                "Order must contain at least one dish.");
        }

        // 2. Reject invalid IDs and quantities.
        if (items.Any(i =>
            i.DishId <= 0 ||
            i.Quantity <= 0))
        {
            return new(
                false,
                "Dish IDs and quantities must be positive.");
        }

        // 3. Find distinct requested dish IDs.
        var dishIds = items
            .Select(i => i.DishId)
            .Distinct()
            .ToArray();

        // 4. Read currently available dishes
        // from PostgreSQL, not the frontend.
        var availableIds = await _db.Dishes
            .AsNoTracking()
            .Where(d =>
                dishIds.Contains(d.Id) &&
                d.IsAvailable)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var availableSet = availableIds.ToHashSet();

        // 5. Find unavailable or missing dishes.
        var unavailableIds = dishIds
            .Where(id => !availableSet.Contains(id))
            .ToArray();

        if (unavailableIds.Length > 0)
        {
            return new(
                false,
                "Unavailable or nonexistent dish IDs: " +
                string.Join(", ", unavailableIds));
        }

        return new(true, null);
    }
}