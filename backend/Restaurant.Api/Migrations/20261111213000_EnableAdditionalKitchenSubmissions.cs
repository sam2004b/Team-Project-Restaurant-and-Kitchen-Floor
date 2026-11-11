using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("20261111213000_EnableAdditionalKitchenSubmissions")]
public partial class EnableAdditionalKitchenSubmissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "PaidAtUtc", table: "orders",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.CreateTable(
            name: "kitchen_submissions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kitchen_submissions", x => x.Id);
                table.ForeignKey(
                    name: "FK_kitchen_submissions_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_submissions_OrderId_SentAtUtc",
            table: "kitchen_submissions",
            columns: new[] { "OrderId", "SentAtUtc" });

        migrationBuilder.AddColumn<int>(
            name: "KitchenSubmissionId", table: "order_items",
            type: "integer", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_order_items_KitchenSubmissionId",
            table: "order_items", column: "KitchenSubmissionId");

        migrationBuilder.AddForeignKey(
            name: "FK_order_items_kitchen_submissions_KitchenSubmissionId",
            table: "order_items",
            column: "KitchenSubmissionId",
            principalTable: "kitchen_submissions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // Backfill all already-sent orders so existing kitchen tickets survive.
        migrationBuilder.Sql(@"
            INSERT INTO kitchen_submissions (""OrderId"", ""SentAtUtc"")
            SELECT ""Id"", ""SentAtUtc""
            FROM orders WHERE ""Status"" = 'Sent';

            UPDATE order_items AS oi
            SET ""KitchenSubmissionId"" = ks.""Id""
            FROM kitchen_submissions AS ks
            WHERE oi.""OrderId"" = ks.""OrderId"";
        ");

        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_state", table: "orders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_sent_consistency", table: "orders");
        migrationBuilder.DropIndex(
            name: "UX_orders_one_open_per_table", table: "orders");

        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_state", table: "orders",
            sql: "\"Status\" IN ('Legacy', 'Open', 'Sent', 'Closed')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_sent_consistency", table: "orders",
            sql: "(\"Status\" IN ('Sent', 'Closed') AND \"SentAtUtc\" IS NOT NULL) " +
                 "OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL)");
        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_paid_consistency", table: "orders",
            sql: "(\"Status\" = 'Closed' AND \"PaidAtUtc\" IS NOT NULL) " +
                 "OR (\"Status\" <> 'Closed' AND \"PaidAtUtc\" IS NULL)");

        // A restaurant table can have separate active orders for separate seats.
        // Do not create a per-table UNIQUE index: existing production-like data
        // may already contain several valid sent orders for the same table.
        // The follow-up migration adds a seat-scoped unique index instead.
        migrationBuilder.CreateIndex(
            name: "UX_orders_one_open_per_table", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId" },
            unique: false,
            filter: "\"Status\" IN ('Open', 'Sent')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM orders WHERE ""Status"" = 'Closed') THEN
                    RAISE EXCEPTION 'Cannot roll back additional kitchen submissions while paid orders exist';
                END IF;
            END $$;
        ");

        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_paid_consistency", table: "orders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_sent_consistency", table: "orders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_orders_state", table: "orders");
        migrationBuilder.DropIndex(
            name: "UX_orders_one_open_per_table", table: "orders");

        migrationBuilder.DropForeignKey(
            name: "FK_order_items_kitchen_submissions_KitchenSubmissionId",
            table: "order_items");
        migrationBuilder.DropTable(name: "kitchen_submissions");
        migrationBuilder.DropIndex(
            name: "IX_order_items_KitchenSubmissionId", table: "order_items");
        migrationBuilder.DropColumn(
            name: "KitchenSubmissionId", table: "order_items");
        migrationBuilder.DropColumn(
            name: "PaidAtUtc", table: "orders");

        migrationBuilder.CreateIndex(
            name: "UX_orders_one_open_per_table", table: "orders",
            columns: new[] { "RestaurantId", "DiningTableId" },
            unique: true, filter: "\"Status\" = 'Open'");
        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_state", table: "orders",
            sql: "\"Status\" IN ('Legacy', 'Open', 'Sent')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_orders_sent_consistency", table: "orders",
            sql: "(\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL) " +
                 "OR (\"Status\" <> 'Sent' AND \"SentAtUtc\" IS NULL)");
    }
}
