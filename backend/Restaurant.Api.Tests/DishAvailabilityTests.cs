
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Menu;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public class DishAvailabilityTests
{
	private static RestaurantDbContext CreateContext()
	{
		var options =
			new DbContextOptionsBuilder<RestaurantDbContext>()
				.UseInMemoryDatabase(
					Guid.NewGuid().ToString())
				.Options;

		return new RestaurantDbContext(options);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReturnsCorrectAvailability(
		bool expectedAvailability)
	{
		await using var db = CreateContext();

		var dish = new Dish
		{
			Name = "Carbonara",
			Price = 14.50m,
			Category = "Pasta",
			IsAvailable = expectedAvailability
		};

		db.Dishes.Add(dish);
		await db.SaveChangesAsync();

		var service = new MenuReadService(db);

		var result =
			await service.GetDishAvailabilityAsync(dish.Id);

		Assert.Equal<bool?>(expectedAvailability, result);
	}

	[Fact]
	public async Task ReturnsNullWhenDishDoesNotExist()
	{
		await using var db = CreateContext();

		var service = new MenuReadService(db);

		var result =
			await service.GetDishAvailabilityAsync(999);

		Assert.Null(result);
	}

	[Fact]
	public async Task DishDisappearsWhenUnavailable()
	{
		await using var db = CreateContext();

		var dish = new Dish
		{
			Name = "Carbonara",
			Price = 14.50m,
			Category = "Pasta",
			IsAvailable = true
		};

		db.Dishes.Add(dish);
		await db.SaveChangesAsync();

		var service = new MenuReadService(db);

		// Initially visible
		var available =
			await service.GetAvailableDishesAsync();

		Assert.Single(available);

		// Change availability
		dish.IsAvailable = false;
		await db.SaveChangesAsync();

		// Should disappear from available menu
		var updated =
			await service.GetAvailableDishesAsync();

		Assert.Empty(updated);

		Assert.Equal<bool?>(
			false,
			await service.GetDishAvailabilityAsync(dish.Id));
	}

	[Fact]
	public async Task DishReappearsWhenAvailableAgain()
	{
		await using var db = CreateContext();

		var dish = new Dish
		{
			Name = "Fish Soup",
			Price = 7.25m,
			Category = "Soups",
			IsAvailable = false
		};

		db.Dishes.Add(dish);
		await db.SaveChangesAsync();

		var service = new MenuReadService(db);

		Assert.Empty(await service.GetAvailableDishesAsync());

		// Re-enable dish
		dish.IsAvailable = true;
		await db.SaveChangesAsync();

		var available =
			await service.GetAvailableDishesAsync();

		Assert.Single(available);
		Assert.Equal("Fish Soup", available[0].Name);
	}
}
