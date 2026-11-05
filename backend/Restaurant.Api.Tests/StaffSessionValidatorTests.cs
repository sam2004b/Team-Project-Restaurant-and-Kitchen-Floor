using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Restaurant.Api.Security;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class StaffSessionValidatorTests
{
    private static async Task<(RestaurantDbContext Db, StaffSession Session,
        StaffMembership Membership, IdentityUser User)> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new RestaurantDbContext(options);

        var user = new IdentityUser
        {
            UserName = "ivan",
            Email = "ivan@restaurant.test",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
        var restaurant = new RestaurantAccount
        {
            Name = "Bistro 21",
            Address = "21 Test Street"
        };
        db.Users.Add(user);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();

        var membership = new StaffMembership
        {
            UserId = user.Id,
            RestaurantId = restaurant.Id,
            Role = StaffRoleNames.Waiter,
            Status = MembershipStatus.Active
        };
        db.StaffMemberships.Add(membership);
        await db.SaveChangesAsync();

        var session = new StaffSession
        {
            UserId = user.Id,
            RestaurantId = restaurant.Id,
            StaffMembershipId = membership.Id,
            SecurityStamp = user.SecurityStamp,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };
        db.StaffSessions.Add(session);
        await db.SaveChangesAsync();

        return (db, session, membership, user);
    }

    [Fact]
    public async Task ActiveMembership_ProvidesCurrentRoleAndRestaurant()
    {
        var (db, session, membership, user) = await CreateAsync();
        await using (db)
        {
            var validator = new StaffSessionValidator(db);
            var context = await validator.GetCurrentAsync(session.Id, user.Id);
            Assert.NotNull(context);
            Assert.Equal(membership.RestaurantId, context.RestaurantId);
            Assert.Equal(membership.Id, context.StaffMembershipId);
            Assert.Equal(StaffRoleNames.Waiter, context.Role);

            membership.Role = StaffRoleNames.Chef;
            await db.SaveChangesAsync();
            context = await validator.GetCurrentAsync(session.Id, user.Id);
            Assert.NotNull(context);
            Assert.Equal(StaffRoleNames.Chef, context.Role);
        }
    }

    [Fact]
    public async Task RevokedMembership_DeniesExistingSession()
    {
        var (db, session, membership, user) = await CreateAsync();
        await using (db)
        {
            membership.Status = MembershipStatus.Revoked;
            await db.SaveChangesAsync();
            Assert.Null(await new StaffSessionValidator(db)
                .GetCurrentAsync(session.Id, user.Id));
        }
    }

    [Fact]
    public async Task RevokedSession_DeniesAccessEvenWhenMembershipActive()
    {
        var (db, session, _, user) = await CreateAsync();
        await using (db)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Assert.Null(await new StaffSessionValidator(db)
                .GetCurrentAsync(session.Id, user.Id));
        }
    }

    [Fact]
    public async Task LockedAccount_DeniesExistingSession()
    {
        var (db, session, _, user) = await CreateAsync();
        await using (db)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(2);
            await db.SaveChangesAsync();
            Assert.Null(await new StaffSessionValidator(db)
                .GetCurrentAsync(session.Id, user.Id));
        }
    }

    [Fact]
    public async Task ExpiredSession_DeniesAccess()
    {
        var (db, session, _, user) = await CreateAsync();
        await using (db)
        {
            session.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
            Assert.Null(await new StaffSessionValidator(db)
                .GetCurrentAsync(session.Id, user.Id));
        }
    }

    [Fact]
    public async Task SecurityStampChange_DeniesExistingSession()
    {
        var (db, session, _, user) = await CreateAsync();
        await using (db)
        {
            user.SecurityStamp = Guid.NewGuid().ToString();
            await db.SaveChangesAsync();
            Assert.Null(await new StaffSessionValidator(db)
                .GetCurrentAsync(session.Id, user.Id));
        }
    }
}
