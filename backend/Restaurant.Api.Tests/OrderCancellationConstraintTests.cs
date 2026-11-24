using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Restaurant.Api.Data;
using Restaurant.Api.Models;
using Xunit;

namespace Restaurant.Api.Tests;

public sealed class OrderCancellationConstraintTests
{
    [Fact]
    public void SchemaAllowsCancelledOrdersWithoutInventingSentAtTimestamp()
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseInMemoryDatabase("cancel-constraint-" + Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new RestaurantDbContext(options);
        // Check database schema metadata, not EF's read-optimized runtime model.
        var order = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RestaurantOrder))!;
        var sql = order.GetCheckConstraints()
            .Single(c => c.Name == "CK_orders_sent_consistency").Sql;

        Assert.Contains("\"Status\" = 'Cancelled'", sql);
        Assert.Contains("'Sent', 'Closed'", sql);
        Assert.Contains("'Legacy', 'Open'", sql);
    }
}
