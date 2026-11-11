using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("20261111220000_AllowActiveOrdersPerSeat")]
public partial class AllowActiveOrdersPerSeat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Older orders have no recorded seat. Keep them intact and accessible by ID.
        migrationBuilder.AddColumn<int>(
            name: "SeatNumber", table: "orders", type: "integer", nullable: true);

        // Works whether the previous migration created a unique or nonunique index.
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_orders_one_open_per_table\";");

        migrationBuilder.CreateIndex(
            name: "IX_orders_active_by_table", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId" },
            filter: "\"Status\" IN ('Open', 'Sent')");

        // One active order per *seat*; several seats can have active orders.
        // Legacy orders without a seat are intentionally excluded.
        migrationBuilder.CreateIndex(
            name: "UX_orders_one_active_per_seat", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId", "SeatNumber" },
            unique: true,
            filter: "\"SeatNumber\" IS NOT NULL AND \"Status\" IN ('Open', 'Sent')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_seat_number", table: "orders",
            sql: "\"SeatNumber\" IS NULL OR \"SeatNumber\" > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_orders_seat_number", table: "orders");
        migrationBuilder.DropIndex(name: "UX_orders_one_active_per_seat", table: "orders");
        migrationBuilder.DropIndex(name: "IX_orders_active_by_table", table: "orders");
        migrationBuilder.DropColumn(name: "SeatNumber", table: "orders");
        migrationBuilder.CreateIndex(
            name: "UX_orders_one_open_per_table", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId" },
            unique: false,
            filter: "\"Status\" IN ('Open', 'Sent')");
    }
}
