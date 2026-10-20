
using Restaurant.Api.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Menu;

public interface IMenuReadService
{
    // Retrieve all available menu dishes
    Task<IReadOnlyList<MenuDishDto>>
        GetAvailableDishesAsync(
            CancellationToken cancellationToken = default);

    // Check the availability of a specific dish
    Task<bool?> GetDishAvailabilityAsync(
        int dishId,
        CancellationToken cancellationToken = default);
}