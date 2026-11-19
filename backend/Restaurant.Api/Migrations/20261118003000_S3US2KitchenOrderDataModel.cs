using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US2-T01: tenant-safe kitchen batches and stable preparation ordering.
/// Existing rows are backfilled before constraints become NOT NULL.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261118003000_S3US2KitchenOrderDataModel")]
public partial class S3US2KitchenOrderDataModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_order_items_kitchen_submissions_KitchenSubmissionId",
            table: "order_items");
        migrationBuilder.DropForeignKey(
            name: "FK_kitchen_submissions_orders_OrderId",
            table: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "IX_order_items_KitchenSubmissionId", table: "order_items");
        migrationBuilder.DropIndex(
            name: "IX_kitchen_submissions_OrderId_SentAtUtc",
            table: "kitchen_submissions");

        // Nullable first so existing Sprint 2 data can be populated safely.
        migrationBuilder.AddColumn<int>(
            name: "RestaurantId", table: "kitchen_submissions",
            type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "SequenceNumber", table: "kitchen_submissions",
            type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "SequenceNumber", table: "order_items",
            type: "integer", nullable: true);

        migrationBuilder.Sql("""
            UPDATE kitchen_submissions AS ks
            SET "RestaurantId" = o."RestaurantId"
            FROM orders AS o
            WHERE o."Id" = ks."OrderId";

            WITH numbered AS (
                SELECT "Id", ROW_NUMBER() OVER (
                    PARTITION BY "OrderId" ORDER BY "SentAtUtc", "Id"
                )::integer AS pos
                FROM kitchen_submissions
            )
            UPDATE kitchen_submissions AS ks
            SET "SequenceNumber" = n.pos
            FROM numbered AS n WHERE n."Id" = ks."Id";

            WITH numbered AS (
                SELECT "Id", ROW_NUMBER() OVER (
                    PARTITION BY "OrderId" ORDER BY "Id"
                )::integer AS pos
                FROM order_items
            )
            UPDATE order_items AS oi
            SET "SequenceNumber" = n.pos
            FROM numbered AS n WHERE n."Id" = oi."Id";
            """);

        migrationBuilder.AlterColumn<int>(
            name: "RestaurantId", table: "kitchen_submissions",
            type: "integer", nullable: false,
            oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "SequenceNumber", table: "kitchen_submissions",
            type: "integer", nullable: false,
            oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "SequenceNumber", table: "order_items",
            type: "integer", nullable: false,
            oldClrType: typeof(int), oldType: "integer", oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_kitchen_submissions_sequence_positive",
            table: "kitchen_submissions", sql: "\"SequenceNumber\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_sequence_positive",
            table: "order_items", sql: "\"SequenceNumber\" > 0");

        // PostgreSQL must have unique targets for both composite FKs.
        migrationBuilder.AddUniqueConstraint(
            name: "AK_orders_RestaurantId_Id", table: "orders",
            columns: new[] { "RestaurantId", "Id" });
        migrationBuilder.AddUniqueConstraint(
            name: "AK_kitchen_submissions_OrderId_Id",
            table: "kitchen_submissions",
            columns: new[] { "OrderId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_orders_restaurant_sent_id", table: "orders",
            columns: new[] { "RestaurantId", "SentAtUtc", "Id" },
            filter: "\"SentAtUtc\" IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_kitchen_submissions_restaurant_sent_id",
            table: "kitchen_submissions",
            columns: new[] { "RestaurantId", "SentAtUtc", "Id" });
        migrationBuilder.CreateIndex(
            name: "UX_kitchen_submissions_order_sequence",
            table: "kitchen_submissions",
            columns: new[] { "OrderId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_order_items_order_sequence", table: "order_items",
            columns: new[] { "OrderId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_order_items_OrderId_KitchenSubmissionId", table: "order_items",
            columns: new[] { "OrderId", "KitchenSubmissionId" });
        migrationBuilder.CreateIndex(
            name: "IX_kitchen_submissions_RestaurantId_OrderId",
            table: "kitchen_submissions",
            columns: new[] { "RestaurantId", "OrderId" });

        // A kitchen submission's restaurant must match its parent's restaurant.
        migrationBuilder.AddForeignKey(
            name: "FK_kitchen_submissions_orders_RestaurantId_OrderId",
            table: "kitchen_submissions",
            columns: new[] { "RestaurantId", "OrderId" },
            principalTable: "orders",
            principalColumns: new[] { "RestaurantId", "Id" },
            onDelete: ReferentialAction.Cascade);

        // An order item must belong to the same order as its kitchen batch.
        migrationBuilder.AddForeignKey(
            name: "FK_order_items_kitchen_submissions_OrderId_KitchenSubmissionId",
            table: "order_items",
            columns: new[] { "OrderId", "KitchenSubmissionId" },
            principalTable: "kitchen_submissions",
            principalColumns: new[] { "OrderId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_order_items_kitchen_submissions_OrderId_KitchenSubmissionId",
            table: "order_items");
        migrationBuilder.DropForeignKey(
            name: "FK_kitchen_submissions_orders_RestaurantId_OrderId",
            table: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "IX_order_items_OrderId_KitchenSubmissionId", table: "order_items");
        migrationBuilder.DropIndex(
            name: "UX_order_items_order_sequence", table: "order_items");
        migrationBuilder.DropIndex(
            name: "IX_kitchen_submissions_RestaurantId_OrderId",
            table: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "UX_kitchen_submissions_order_sequence", table: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "IX_kitchen_submissions_restaurant_sent_id",
            table: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "IX_orders_restaurant_sent_id", table: "orders");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_kitchen_submissions_OrderId_Id", table: "kitchen_submissions");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_orders_RestaurantId_Id", table: "orders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_kitchen_submissions_sequence_positive", table: "kitchen_submissions");
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_sequence_positive", table: "order_items");
        migrationBuilder.DropColumn(name: "RestaurantId", table: "kitchen_submissions");
        migrationBuilder.DropColumn(name: "SequenceNumber", table: "kitchen_submissions");
        migrationBuilder.DropColumn(name: "SequenceNumber", table: "order_items");

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_submissions_OrderId_SentAtUtc",
            table: "kitchen_submissions", columns: new[] { "OrderId", "SentAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_order_items_KitchenSubmissionId",
            table: "order_items", column: "KitchenSubmissionId");
        migrationBuilder.AddForeignKey(
            name: "FK_kitchen_submissions_orders_OrderId",
            table: "kitchen_submissions", column: "OrderId",
            principalTable: "orders", principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey(
            name: "FK_order_items_kitchen_submissions_KitchenSubmissionId",
            table: "order_items", column: "KitchenSubmissionId",
            principalTable: "kitchen_submissions", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
