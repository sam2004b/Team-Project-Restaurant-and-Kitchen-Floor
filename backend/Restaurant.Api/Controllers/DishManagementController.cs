using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos;
using Restaurant.Api.Models;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("dishes")]
public sealed class DishManagementController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public DishManagementController(
        RestaurantDbContext db,
        IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    // GET /dishes/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DishDetailsDto>> GetDishById(
        int id,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return ManagementDisabled();

        if (id <= 0)
            return BadRequest(new { error = "Invalid dish ID." });

        var dish = await _db.Dishes
            .AsNoTracking()
            .Include(d => d.Ingredients)
            .SingleOrDefaultAsync(
                d => d.Id == id,
                cancellationToken);

        if (dish == null)
            return NotFound(new { error = "Dish not found." });

        return Ok(ToDto(dish));
    }

    // POST /dishes
    [HttpPost]
    public async Task<ActionResult<DishDetailsDto>> CreateDish(
        [FromBody] SaveDishRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return ManagementDisabled();

        if (!ValidateDishRequest(request))
            return BadRequest(ModelState);

        var dish = new Dish
        {
            Name = request.Name.Trim(),
            Price = request.Price,
            Description = request.Description?.Trim(),
            Category = request.Category.Trim(),
            IsAvailable = request.IsAvailable,
            Ingredients = request.Ingredients
                .Select(name => new DishIngredient
                {
                    Name = name.Trim()
                })
                .ToList()
        };

        _db.Dishes.Add(dish);

        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetDishById),
            new { id = dish.Id },
            ToDto(dish));
    }

    // PUT /dishes/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DishDetailsDto>> UpdateDish(
        int id,
        [FromBody] SaveDishRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return ManagementDisabled();

        if (id <= 0)
            return BadRequest(new { error = "Invalid dish ID." });

        if (!ValidateDishRequest(request))
            return BadRequest(ModelState);

        var dish = await _db.Dishes
            .Include(d => d.Ingredients)
            .SingleOrDefaultAsync(
                d => d.Id == id,
                cancellationToken);

        if (dish == null)
            return NotFound(new { error = "Dish not found." });

        dish.Name = request.Name.Trim();
        dish.Price = request.Price;
        dish.Description = request.Description?.Trim();
        dish.Category = request.Category.Trim();
        dish.IsAvailable = request.IsAvailable;

        // Synchronize the ingredient collection.
        var requestedNames = request.Ingredients
            .Select(name => name.Trim())
            .ToArray();

        var existingNames = dish.Ingredients
            .Select(i => i.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove ingredients no longer present.
        foreach (var ingredient in dish.Ingredients.ToList())
        {
            if (!requestedNames.Contains(
                ingredient.Name,
                StringComparer.OrdinalIgnoreCase))
            {
                _db.DishIngredients.Remove(ingredient);
            }
        }

        // Add newly provided ingredients.
        foreach (var name in requestedNames)
        {
            if (!existingNames.Contains(name))
            {
                dish.Ingredients.Add(new DishIngredient
                {
                    Name = name
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Reload from the database for an accurate response.
        var updated = await _db.Dishes
            .AsNoTracking()
            .Include(d => d.Ingredients)
            .SingleAsync(
                d => d.Id == id,
                cancellationToken);

        return Ok(ToDto(updated));
    }

    private bool ValidateDishRequest(SaveDishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(
                "name", "Dish name is required.");

        if (request.Name?.Trim().Length > 150)
            ModelState.AddModelError(
                "name", "Name cannot exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(request.Category))
            ModelState.AddModelError(
                "category", "Category is required.");

        if (request.Category?.Trim().Length > 80)
            ModelState.AddModelError(
                "category", "Category cannot exceed 80 characters.");

        if (request.Description?.Length > 1000)
            ModelState.AddModelError(
                "description", "Description is too long.");

        if (request.Price <= 0 ||
            request.Price > 99999999.99m ||
            decimal.Round(request.Price, 2) != request.Price)
        {
            ModelState.AddModelError(
                "price",
                "Price must be positive, within the database range, " +
                "and have at most two decimal places.");
        }

        if (request.Ingredients == null)
        {
            ModelState.AddModelError(
                "ingredients", "Ingredients cannot be null.");
        }
        else
        {
            if (request.Ingredients.Any(
                name => string.IsNullOrWhiteSpace(name) ||
                        name.Trim().Length > 150))
            {
                ModelState.AddModelError(
                    "ingredients",
                    "Ingredient names must be nonblank and at most " +
                    "150 characters long.");
            }
            else
            {
                var names = request.Ingredients
                    .Select(name => name.Trim())
                    .ToArray();

                if (names.Distinct(
                    StringComparer.OrdinalIgnoreCase).Count()
                    != names.Length)
                {
                    ModelState.AddModelError(
                        "ingredients",
                        "Duplicate ingredients are not allowed.");
                }
            }
        }

        return ModelState.IsValid;
    }

    private ObjectResult ManagementDisabled()
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            new
            {
                error = "Dish management is disabled outside " +
                        "Development until administrator " +
                        "authentication is configured."
            });
    }

    private static DishDetailsDto ToDto(Dish dish)
    {
        return new DishDetailsDto(
            dish.Id,
            dish.Name,
            dish.Price,
            dish.Description,
            dish.Category,
            dish.IsAvailable,
            dish.Ingredients
                .OrderBy(i => i.Id)
                .Select(i => i.Name)
                .ToArray());
    }
}