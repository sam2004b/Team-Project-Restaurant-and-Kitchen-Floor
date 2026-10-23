using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Restaurant.Api.Controllers;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

public class DishManagementControllerTests
{
    private static RestaurantDbContext CreateDb()
    {
        var options =
            new DbContextOptionsBuilder<RestaurantDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new RestaurantDbContext(options);
    }

    private static DishManagementController CreateController(
    RestaurantDbContext db,
    string environment = "Development")
{
    return new DishManagementController(
        db,
        new TestEnvironment
        {
            EnvironmentName = environment
        });
}

    private static SaveDishRequest MakeRequest(
        decimal price = 450m,
        string secondIngredient = "Eggs")
    {
        return new SaveDishRequest
        {
            Name = "Carbonara",
            Price = price,
            Description = "Italian pasta",
            Category = "Pasta",
            IsAvailable = true,
            Ingredients = new List<string>
            {
                "Spaghetti",
                secondIngredient,
                "Pecorino Romano",
                "Guanciale"
            }
        };
    }

    [Fact]
    public async Task CreateDish_SavesFourIngredients()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var response = await controller.CreateDish(
            MakeRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(
            response.Result);

        var result = Assert.IsType<DishDetailsDto>(
            created.Value);

        Assert.True(result.Id > 0);
        Assert.Equal(450m, result.Price);
        Assert.Equal(4, result.Ingredients.Count);

        var saved = await db.Dishes
            .Include(d => d.Ingredients)
            .SingleAsync(d => d.Id == result.Id);

        Assert.Equal(4, saved.Ingredients.Count);
    }

    [Fact]
    public async Task UpdateDish_ChangesPriceAndIngredient()
    {
        await using var db = CreateDb();

        var dish = new Dish
        {
            Name = "Carbonara",
            Price = 450m,
            Category = "Pasta",
            Ingredients = new List<DishIngredient>
            {
                new() { Name = "Spaghetti" },
                new() { Name = "Eggs" },
                new() { Name = "Pecorino Romano" },
                new() { Name = "Guanciale" }
            }
        };

        db.Dishes.Add(dish);
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var response = await controller.UpdateDish(
            dish.Id,
            MakeRequest(500m, "Egg Yolks"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(
            response.Result);

        var updated = Assert.IsType<DishDetailsDto>(
            ok.Value);

        Assert.Equal(500m, updated.Price);
        Assert.Equal(4, updated.Ingredients.Count);
        Assert.Contains("Egg Yolks", updated.Ingredients);
        Assert.DoesNotContain("Eggs", updated.Ingredients);
    }

    [Fact]
    public async Task UpdateDish_UnknownIdReturns404()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var response = await controller.UpdateDish(
            999999,
            MakeRequest(),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(
            response.Result);
    }

    [Fact]
    public async Task CreateDish_InvalidPriceReturns400()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var response = await controller.CreateDish(
            MakeRequest(0m),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(
            response.Result);

        Assert.Empty(await db.Dishes.ToListAsync());
    }

    [Fact]
    public async Task CreateDish_BlankNameReturns400()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var request = MakeRequest();
        request.Name = "   ";

        var response = await controller.CreateDish(
            request,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(
            response.Result);

        Assert.Empty(await db.Dishes.ToListAsync());
    }

    [Fact]
    public async Task CreateDish_ProductionIsForbidden()
    {
        await using var db = CreateDb();

        var controller = CreateController(
            db, Environments.Production);

        var response = await controller.CreateDish(
            MakeRequest(),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(
            response.Result);

        Assert.Equal(403, forbidden.StatusCode);
        Assert.Empty(await db.Dishes.ToListAsync());
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; }
            = Environments.Development;

        public string ApplicationName { get; set; }
            = "Restaurant.Api.Tests";

        public string ContentRootPath { get; set; }
            = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; }
            = new NullFileProvider();

        public string WebRootPath { get; set; }
            = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; }
            = new NullFileProvider();
    }
}