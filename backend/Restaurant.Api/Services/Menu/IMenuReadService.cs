
using Restaurant.Api.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Menu;

public interface IMenuReadService
{
    Task<IReadOnlyList<MenuDishDto>>
        GetAvailableDishesAsync(
            CancellationToken cancellationToken = default);
}