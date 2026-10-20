
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Menu;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Restaurant.Api.Tests;

public class MenuReadServiceTests
{
	private static RestaurantDbContext CreateContext()
	{
		var options =
			new DbContextOptionsBuilder<RestaurantDbContext>()
				.UseInMemoryDatabase(Guid.NewGuid().ToString())
				.Options;

		return new RestaurantDbContext(options);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(12)]
	[InlineData(50)]
	public async Task ReturnsAllAvailableDishesWithoutDuplicates(
		int count)
	{
		await using var db = CreateContext();

		var dishes = Enumerable.Range(1, count)
			.Select(i => new Dish
			{
				Name = $"Test Dish {i}",
				Price = 10.00m,
				Category = "Test",
				IsAvailable = true
			});

		db.Dishes.AddRange(dishes);
		await db.SaveChangesAsync();

		var service = new MenuReadService(db);

		var result = await service.GetAvailableDishesAsync();

		Assert.Equal(count, result.Count);
		Assert.Equal(count, result.Select(d => d.Id).Distinct().Count());
	}

	[Fact]
	public async Task ExcludesUnavailableDishes()
	{
		await using var db = CreateContext();

		db.Dishes.AddRange(
			new Dish
			{
				Name = "Carbonara",
				Price = 14.50m,
				Category = "Pasta",
				IsAvailable = true
			},
			new Dish
			{
				Name = "Fish Soup",
				Price = 7.25m,
				Category = "Soup",
				IsAvailable = false
			});

		await db.SaveChangesAsync();

		var service = new MenuReadService(db);

		var result = await service.GetAvailableDishesAsync();

		Assert.Single(result);
		Assert.Equal("Carbonara", result[0].Name);
	}

	[Fact]
	public async Task ReflectsAvailabilityChanges()
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

		Assert.Single(await service.GetAvailableDishesAsync());

		dish.IsAvailable = false;
		await db.SaveChangesAsync();

		Assert.Empty(await service.GetAvailableDishesAsync());
	}
}
