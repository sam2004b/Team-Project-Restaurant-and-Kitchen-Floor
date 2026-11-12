
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Services.Menu;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Dishes;

/// <summary>
/// Reads and updates the shared availability flag
/// stored in the existing dishes table.
/// </summary>
public sealed class DishAvailabilityService
    : IDishAvailabilityService
{
    private readonly RestaurantDbContext _db;
    private readonly IMenuUpdateNotifier _menuNotifier;

    public DishAvailabilityService(
        RestaurantDbContext db,
        IMenuUpdateNotifier menuNotifier)
    {
        _db = db;
        _menuNotifier = menuNotifier;
    }

    public async Task<AvailabilityUpdateResult>
        SetAvailabilityAsync(
            int dishId,
            bool isAvailable,
            CancellationToken cancellationToken = default)
    {
        // 1. Find the existing dish.
        var dish = await _db.Dishes
            .SingleOrDefaultAsync(
                d => d.Id == dishId,
                cancellationToken);

        // 2. Do not insert or create missing dishes.
        if (dish is null)
        {
            return AvailabilityUpdateResult.NotFound;
        }

        // 3. Avoid unnecessary database writes
        // and duplicate notifications.
        if (dish.IsAvailable == isAvailable)
        {
            return AvailabilityUpdateResult.Unchanged;
        }

        // 4. Change only the availability state.
        dish.IsAvailable = isAvailable;

        // 5. Persist the change to the existing row.
        await _db.SaveChangesAsync(cancellationToken);

        // 6. Notify connected customer menus
        // after a successful database save.
        await _menuNotifier.NotifyMenuChangedAsync(
            cancellationToken);

        return AvailabilityUpdateResult.Updated;
    }
}
