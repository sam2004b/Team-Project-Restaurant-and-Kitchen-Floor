
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using System;
using System.Threading.Tasks;

namespace Restaurant.Api.Seeding;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration)
    {
        var db = services.GetRequiredService<RestaurantDbContext>();

        var userManager =
            services.GetRequiredService<UserManager<IdentityUser>>();

        var roleManager =
            services.GetRequiredService<RoleManager<IdentityRole>>();

        // Password supplied through User Secrets
        // or a development environment variable.
        var password = configuration["Seed:DefaultPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Seed:DefaultPassword is not configured."
            );
        }

        // 1. Create roles
        string[] roles =
        {
            "Administrator",
            "Waiter",
            "Chef",
            "Reception Manager"
        };

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(
                    new IdentityRole(roleName));

                EnsureSuccess(result);
            }
        }

        // 2. Create test accounts
        var accounts = new[]
        {
            (Username: "admin",
             Email: "admin@restaurant.test",
             Role: "Administrator"),

            (Username: "waiter",
             Email: "waiter@restaurant.test",
             Role: "Waiter"),

            (Username: "chef",
             Email: "chef@restaurant.test",
             Role: "Chef"),

            (Username: "reception",
             Email: "reception@restaurant.test",
             Role: "Reception Manager")
        };

        foreach (var account in accounts)
        {
            var user = await userManager.FindByNameAsync(
                account.Username);

            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = account.Username,
                    Email = account.Email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(
                    user, password);

                EnsureSuccess(result);
            }

            if (!await userManager.IsInRoleAsync(
                user, account.Role))
            {
                var result = await userManager.AddToRoleAsync(
                    user, account.Role);

                EnsureSuccess(result);
            }
        }

        // 3. Create example menu dishes
        var dishes = new[]
        {
            new Dish
            {
                Name = "Margherita Pizza",
                Description = "Pizza with tomato and mozzarella",
                Category = "Pizza",
                Price = 9.90m,
                IsAvailable = true
            },

            new Dish
            {
                Name = "Chicken Burger",
                Description = "Grilled chicken burger",
                Category = "Burgers",
                Price = 12.50m,
                IsAvailable = true
            },

            new Dish
            {
                Name = "Caesar Salad",
                Description = "Salad with Caesar dressing",
                Category = "Salads",
                Price = 8.00m,
                IsAvailable = true
            },

            new Dish
            {
                Name = "Fish Soup",
                Description = "Traditional fish soup",
                Category = "Soups",
                Price = 7.25m,
                IsAvailable = false
            }
        };

        foreach (var dish in dishes)
        {
            bool exists = await db.Dishes.AnyAsync(
                d => d.Name == dish.Name);

            if (!exists)
            {
                db.Dishes.Add(dish);
            }
        }

        await db.SaveChangesAsync();

        Console.WriteLine("Database seeding completed.");
    }

    private static void EnsureSuccess(
        IdentityResult result)
    {
        if (result.Succeeded)
            return;

        var errors = string.Join(
            "; ",
            result.Errors.Select(e => e.Description));

        throw new InvalidOperationException(
            $"Identity seeding failed: {errors}");
    }
}
