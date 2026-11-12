using System.Security.Claims;
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


        await SeedRestaurantsAndStaffAsync(db, userManager);

        // S2-US7-T01: Ivan and Anna share one restaurant, with different roles.
        await SeedRoleSpecificStaffAsync(db, userManager, password);


        await SeedRestaurantSettingsAndTablesAsync(db);


        var bootstrapAdmin =
    await userManager.FindByNameAsync("admin");

if (bootstrapAdmin is null)
{
    throw new InvalidOperationException(
        "Seed administrator was not found.");
}

var claims =
    await userManager.GetClaimsAsync(bootstrapAdmin);

if (!claims.Any(c =>
    c.Type == "permission" &&
    c.Value == "restaurants:create"))
{
    var result = await userManager.AddClaimAsync(
        bootstrapAdmin,
        new Claim(
            "permission",
            "restaurants:create"));

    EnsureSuccess(result);
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


        var additionalMenuDishes = new[]
        {
            (Name: "Carbonara", Category: "Pasta", Price: 14.50m),
            (Name: "Grilled Salmon", Category: "Seafood", Price: 19.90m),
            (Name: "Beef Steak", Category: "Main Course", Price: 24.00m),
            (Name: "Pasta Alfredo", Category: "Pasta", Price: 13.50m),
            (Name: "Vegetable Soup", Category: "Soups", Price: 6.50m),
            (Name: "Veggie Pizza", Category: "Pizza", Price: 11.00m),
            (Name: "Cheesecake", Category: "Desserts", Price: 6.90m),
            (Name: "Chocolate Brownie", Category: "Desserts", Price: 5.50m),
            (Name: "French Fries", Category: "Sides", Price: 4.50m)
        };

        foreach (var sample in additionalMenuDishes)
        {
            bool exists = await db.Dishes.AnyAsync(
                d => d.Name == sample.Name);

            if (!exists)
            {
                db.Dishes.Add(new Dish
                {
                    Name = sample.Name,
                    Category = sample.Category,
                    Price = sample.Price,
                    Description = $"Sample {sample.Name} dish",
                    IsAvailable = true
                });
            }
        }

        await db.SaveChangesAsync();


        // Seed Carbonara ingredients for US-11
        var carbonara = await db.Dishes
            .Include(d => d.Ingredients)
            .FirstOrDefaultAsync(d => d.Name == "Carbonara");

        if (carbonara != null && !carbonara.Ingredients.Any())
        {
            var ingredientNames = new[]
            {
                "Spaghetti",
                "Eggs",
                "Pecorino Romano",
                "Guanciale"
            };

            foreach (var name in ingredientNames)
            {
                carbonara.Ingredients.Add(new DishIngredient
                {
                    Name = name
                });
            }

            await db.SaveChangesAsync();
        }




        // Assign images to seeded dishes (including existing rows).
        var dishImages = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Margherita Pizza"] = "/images/dishes/margherita-pizza.jpg",
            ["Chicken Burger"] = "/images/dishes/chicken-burger.jpg",
            ["Caesar Salad"] = "/images/dishes/caesar-salad.jpg",
            ["Fish Soup"] = "/images/dishes/fish-soup.jpg",
            ["Carbonara"] = "/images/dishes/carbonara.jpg",
            ["Grilled Salmon"] = "/images/dishes/grilled-salmon.jpg",
            ["Beef Steak"] = "/images/dishes/beef-steak.jpg",
            ["Pasta Alfredo"] = "/images/dishes/pasta-alfredo.jpg",
            ["Vegetable Soup"] = "/images/dishes/vegetable-soup.jpg",
            ["Veggie Pizza"] = "/images/dishes/veggie-pizza.jpg",
            ["Cheesecake"] = "/images/dishes/cheesecake.jpg",
            ["Chocolate Brownie"] = "/images/dishes/chocolate-brownie.jpg",
            ["French Fries"] = "/images/dishes/french-fries.jpg"
        };

        // Only update dishes that have no image assigned yet.
        var dishesWithoutImages = await db.Dishes
            .Where(d => d.ImageUrl == null || d.ImageUrl == "")
            .ToListAsync();

        foreach (var dish in dishesWithoutImages)
        {
            if (dishImages.TryGetValue(dish.Name, out var imagePath))
            {
                dish.ImageUrl = imagePath;
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


    private static async Task SeedRestaurantsAndStaffAsync(
        RestaurantDbContext db,
        UserManager<IdentityUser> userManager)
    {
        // 1. Create two development restaurants.
        var bistro = await db.Restaurants
            .SingleOrDefaultAsync(
                r => r.NormalizedName == "BISTRO 21");

        if (bistro == null)
        {
            bistro = new RestaurantAccount
            {
                Name = "Bistro 21",
                Address = "21 Test Street"
            };

            db.Restaurants.Add(bistro);
            await db.SaveChangesAsync();
        }

        var cafe = await db.Restaurants
            .SingleOrDefaultAsync(
                r => r.NormalizedName == "CAFE 8");

        if (cafe == null)
        {
            cafe = new RestaurantAccount
            {
                Name = "Cafe 8",
                Address = "8 Test Street"
            };

            db.Restaurants.Add(cafe);
            await db.SaveChangesAsync();
        }

        // 2. Assign existing Identity users to restaurants.
        var memberships = new[]
        {
        (
            RestaurantId: bistro.Id,
            Username: "admin",
            Role: StaffRoleNames.Administrator,
            Status: MembershipStatus.Active
        ),
        (
            RestaurantId: bistro.Id,
            Username: "waiter",
            Role: StaffRoleNames.Waiter,
            Status: MembershipStatus.Active
        ),
        (
            RestaurantId: cafe.Id,
            Username: "chef",
            Role: StaffRoleNames.Chef,
            Status: MembershipStatus.Active
        ),
        (
            RestaurantId: cafe.Id,
            Username: "reception",
            Role: StaffRoleNames.ReceptionManager,
            Status: MembershipStatus.Active
        ),
        (
            RestaurantId: bistro.Id,
            Username: "reception",
            Role: StaffRoleNames.ReceptionManager,
            Status: MembershipStatus.Revoked
        )
    };

        foreach (var membership in memberships)
        {
            var user = await userManager.FindByNameAsync(
                membership.Username);

            if (user == null)
            {
                throw new InvalidOperationException(
                    $"Missing seed user: {membership.Username}");
            }

            bool exists = await db.StaffMemberships.AnyAsync(
                m => m.RestaurantId == membership.RestaurantId
                  && m.UserId == user.Id);

            if (!exists)
            {
                db.StaffMemberships.Add(new StaffMembership
                {
                    RestaurantId = membership.RestaurantId,
                    UserId = user.Id,
                    Role = membership.Role,
                    Status = membership.Status
                });
            }
        }

        // 3. Create a pending invitation for Bistro 21.
        bool invitationExists = await db.StaffInvitations
            .AnyAsync(i =>
                i.RestaurantId == bistro.Id &&
                i.NormalizedEmail == "NEWSTAFF@RESTAURANT.TEST");

        if (!invitationExists)
        {
            db.StaffInvitations.Add(new StaffInvitation
            {
                RestaurantId = bistro.Id,
                Email = "newstaff@restaurant.test",
                Role = StaffRoleNames.Waiter,
                Status = InvitationStatus.Pending
            });
        }

        await db.SaveChangesAsync();
    }


    private static async Task SeedRoleSpecificStaffAsync(
        RestaurantDbContext db,
        UserManager<IdentityUser> userManager,
        string seedPassword)
    {
        var restaurant = await db.Restaurants.SingleAsync(
            r => r.NormalizedName == "BISTRO 21");

        var staff = new[]
        {
            (Username: "ivan", Email: "ivan@restaurant.test",
             Role: StaffRoleNames.Waiter),
            (Username: "maria", Email: "maria@restaurant.test",
             Role: StaffRoleNames.Waiter),
            (Username: "anna", Email: "anna@restaurant.test",
             Role: StaffRoleNames.Chef)
        };

        foreach (var entry in staff)
        {
            var user = await userManager.FindByNameAsync(entry.Username);
            if (user is null)
            {
                user = new IdentityUser
                {
                    UserName = entry.Username,
                    Email = entry.Email,
                    EmailConfirmed = true,
                    LockoutEnabled = true
                };
                EnsureSuccess(await userManager.CreateAsync(user, seedPassword));
            }

            if (!await userManager.IsInRoleAsync(user, entry.Role))
                EnsureSuccess(await userManager.AddToRoleAsync(user, entry.Role));

            var existing = await db.StaffMemberships.SingleOrDefaultAsync(
                m => m.RestaurantId == restaurant.Id && m.UserId == user.Id);

            // Never automatically reactivate a revoked employee on reseed.
            if (existing is null)
            {
                db.StaffMemberships.Add(new StaffMembership
                {
                    RestaurantId = restaurant.Id,
                    UserId = user.Id,
                    Role = entry.Role,
                    Status = MembershipStatus.Active
                });
            }
        }

        await db.SaveChangesAsync();
    }


    private static async Task SeedRestaurantSettingsAndTablesAsync(
        RestaurantDbContext db)
    {
        // ---------------------------------------
        // 1. Find the existing restaurants.
        // ---------------------------------------

        var bistro = await db.Restaurants.SingleAsync(
            r => r.NormalizedName == "BISTRO 21");

        var cafe = await db.Restaurants.SingleAsync(
            r => r.NormalizedName == "CAFE 8");

        // ---------------------------------------
        // 2. Create settings for each restaurant.
        // ---------------------------------------

        var restaurantIds = new[]
        {
        bistro.Id,
        cafe.Id
    };

        foreach (var restaurantId in restaurantIds)
        {
            bool settingsExist =
                await db.RestaurantSettings.AnyAsync(
                    s => s.RestaurantId == restaurantId);

            if (!settingsExist)
            {
                db.RestaurantSettings.Add(
                    new RestaurantSettings
                    {
                        RestaurantId = restaurantId,
                        DefaultSeatCapacity = 4,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
            }
        }

        await db.SaveChangesAsync();

        // ---------------------------------------
        // 3. Create example dining tables.
        // ---------------------------------------

        var tableDefinitions = new[]
        {
        (
            RestaurantId: bistro.Id,
            TableNumber: 1,
            SeatCount: 4
        ),
        (
            RestaurantId: bistro.Id,
            TableNumber: 2,
            SeatCount: 2
        ),
        (
            RestaurantId: cafe.Id,
            TableNumber: 1,
            SeatCount: 2
        )
    };

        foreach (var definition in tableDefinitions)
        {
            bool tableExists = await db.DiningTables.AnyAsync(
                t =>
                    t.RestaurantId == definition.RestaurantId &&
                    t.TableNumber == definition.TableNumber);

            if (!tableExists)
            {
                db.DiningTables.Add(new DiningTable
                {
                    RestaurantId = definition.RestaurantId,
                    TableNumber = definition.TableNumber,
                    SeatCount = definition.SeatCount,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();

        // ---------------------------------------
        // 4. Find Bistro 21 table 1.
        // ---------------------------------------

        var table1 = await db.DiningTables.SingleAsync(
            t =>
                t.RestaurantId == bistro.Id &&
                t.TableNumber == 1);

        // ---------------------------------------
        // 5. Find Bistro 21's active waiter.
        // ---------------------------------------

        var waiterMembership =
            await db.StaffMemberships
                .Include(m => m.User)
                .SingleOrDefaultAsync(
                    m =>
                        m.RestaurantId == bistro.Id &&
                        m.User.UserName == "waiter" &&
                        m.Role == StaffRoleNames.Waiter &&
                        m.Status == MembershipStatus.Active);

        // ---------------------------------------
        // 6. Assign the waiter to table 1.
        // ---------------------------------------

        bool assignmentExists =
            await db.TableStaffAssignments.AnyAsync(
                a => a.DiningTableId == table1.Id);

        if (waiterMembership != null && !assignmentExists)
        {
            db.TableStaffAssignments.Add(
                new TableStaffAssignment
                {
                    RestaurantId = bistro.Id,
                    DiningTableId = table1.Id,
                    StaffMembershipId = waiterMembership.Id,
                    AssignedAtUtc = DateTime.UtcNow
                });

            await db.SaveChangesAsync();
        }
    }



}
