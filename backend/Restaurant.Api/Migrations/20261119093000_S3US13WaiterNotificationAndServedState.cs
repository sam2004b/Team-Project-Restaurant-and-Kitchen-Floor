using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US13-T01: per-line service state plus a tenant-safe waiter inbox.
/// Existing order lines become Unserved with unknown (null) serve time.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261119093000_S3US13WaiterNotificationAndServedState")]
public partial class S3US13WaiterNotificationAndServedState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ServiceStatus", table: "order_items",
            type: "character varying(16)", maxLength: 16,
            nullable: false, defaultValue: "Unserved");

        migrationBuilder.AddColumn<DateTime>(
            name: "ServedAtUtc", table: "order_items",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_service_status", table: "order_items",
            sql: "\"ServiceStatus\" IN ('Unserved', 'Served')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_service_consistency", table: "order_items",
            sql: "(\"ServiceStatus\" = 'Unserved' AND \"ServedAtUtc\" IS NULL) " +
                 "OR (\"ServiceStatus\" = 'Served' AND \"ServedAtUtc\" IS NOT NULL " +
                 "AND \"PreparationStatus\" = 'Ready' " +
                 "AND \"KitchenSubmissionId\" IS NOT NULL " +
                 "AND \"ReadyAtUtc\" IS NOT NULL " +
                 "AND \"ServedAtUtc\" >= \"ReadyAtUtc\")");

        // Needed for tenant + table + order FK on notifications.
        migrationBuilder.AddUniqueConstraint(
            name: "AK_orders_RestaurantId_DiningTableId_Id", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId", "Id" });

        // Needed for order + dish FK on notifications.
        migrationBuilder.AddUniqueConstraint(
            name: "AK_order_items_OrderId_Id", table: "order_items",
            columns: new[] { "OrderId", "Id" });

        migrationBuilder.CreateTable(
            name: "waiter_notifications",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RestaurantId = table.Column<int>(type: "integer", nullable: false),
                DiningTableId = table.Column<int>(type: "integer", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                OrderItemId = table.Column<int>(type: "integer", nullable: false),
                WaiterStaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_waiter_notifications", x => x.Id);
                table.CheckConstraint("CK_waiter_notifications_read_after_created",
                    "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"CreatedAtUtc\"");
                table.ForeignKey(
                    name: "FK_waiter_notifications_orders_RestaurantId_DiningTableId_OrderId",
                    columns: x => new { x.RestaurantId, x.DiningTableId, x.OrderId },
                    principalTable: "orders",
                    principalColumns: new[] { "RestaurantId", "DiningTableId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_waiter_notifications_order_items_OrderId_OrderItemId",
                    columns: x => new { x.OrderId, x.OrderItemId },
                    principalTable: "order_items",
                    principalColumns: new[] { "OrderId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_waiter_notifications_staff_memberships_RestaurantId_WaiterStaffMembershipId",
                    columns: x => new { x.RestaurantId, x.WaiterStaffMembershipId },
                    principalTable: "staff_memberships",
                    principalColumns: new[] { "RestaurantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_waiter_notifications_OrderId_OrderItemId", table: "waiter_notifications",
            columns: new[] { "OrderId", "OrderItemId" });

        migrationBuilder.CreateIndex(
            name: "IX_waiter_notifications_RestaurantId_DiningTableId_OrderId", table: "waiter_notifications",
            columns: new[] { "RestaurantId", "DiningTableId", "OrderId" });

        migrationBuilder.CreateIndex(
            name: "IX_waiter_notifications_RestaurantId_WaiterStaffMembershipId", table: "waiter_notifications",
            columns: new[] { "RestaurantId", "WaiterStaffMembershipId" });

        migrationBuilder.CreateIndex(
            name: "UX_waiter_notifications_item_waiter", table: "waiter_notifications",
            columns: new[] { "OrderItemId", "WaiterStaffMembershipId" }, unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_waiter_notifications_unread_inbox", table: "waiter_notifications",
            columns: new[] { "RestaurantId", "WaiterStaffMembershipId", "CreatedAtUtc", "Id" },
            filter: "\"ReadAtUtc\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "waiter_notifications");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_order_items_OrderId_Id", table: "order_items");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_orders_RestaurantId_DiningTableId_Id", table: "orders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_service_consistency", table: "order_items");
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_service_status", table: "order_items");
        migrationBuilder.DropColumn(name: "ServedAtUtc", table: "order_items");
        migrationBuilder.DropColumn(name: "ServiceStatus", table: "order_items");
    }
}
