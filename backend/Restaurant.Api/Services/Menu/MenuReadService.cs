
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Services.Menu;

public sealed class MenuReadService : IMenuReadService
{
    private readonly RestaurantDbContext _context;

    public MenuReadService(RestaurantDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MenuDishDto>>
    GetAvailableDishesAsync(
        CancellationToken cancellationToken = default)
{
    var dishes = await _context.Dishes
        .AsNoTracking()
        .Where(d => d.IsAvailable)
        .Include(d => d.Ingredients)
        .OrderBy(d => d.Name)
        .ThenBy(d => d.Id)
        .ToListAsync(cancellationToken);

    return dishes
        .Select(d => new MenuDishDto(
            d.Id,
            d.Name,
            d.Price,
            d.Description,
            d.Category,
            d.IsAvailable)
        {
            Ingredients = d.Ingredients
                .OrderBy(i => i.Id)
                .Select(i => i.Name)
                .ToArray()
        })
        .ToArray();
}


    public async Task<bool?> GetDishAvailabilityAsync(
        int dishId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Dishes
            .AsNoTracking()
            .Where(d => d.Id == dishId)
            .Select(d => (bool?)d.IsAvailable)
            .SingleOrDefaultAsync(cancellationToken);
    }

}
