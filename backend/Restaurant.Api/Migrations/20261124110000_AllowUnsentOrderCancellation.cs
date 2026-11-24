using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// Allow draft orders to be cancelled without inventing a kitchen-send timestamp.
/// A sent order that is later cancelled keeps its original SentAtUtc value.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261124110000_AllowUnsentOrderCancellation")]
public partial class AllowUnsentOrderCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_sent_consistency",
            table: "orders");

        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_sent_consistency",
            table: "orders",
            sql: "(\"Status\" IN ('Sent', 'Closed') AND \"SentAtUtc\" IS NOT NULL) " +
                 "OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL) " +
                 "OR (\"Status\" = 'Cancelled')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_sent_consistency",
            table: "orders");

        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_sent_consistency",
            table: "orders",
            sql: "(\"Status\" IN ('Sent', 'Closed', 'Cancelled') AND \"SentAtUtc\" IS NOT NULL) " +
                 "OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL)");
    }
}
