using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineItemsAndSendState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    DishId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    SpecialRequest = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DishNameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.Id);
                    table.CheckConstraint("CK_order_items_price_positive", "\"UnitPriceSnapshot\" > 0");
                    table.CheckConstraint("CK_order_items_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_order_items_dishes_DishId",
                        column: x => x.DishId,
                        principalTable: "dishes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_items_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_orders_one_open_per_table",
                table: "orders",
                columns: new[] { "RestaurantId", "DiningTableId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_sent_after_created",
                table: "orders",
                sql: "\"SentAtUtc\" IS NULL OR \"SentAtUtc\" >= \"CreatedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders",
                sql: "(\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL) OR (\"Status\" <> 'Sent' AND \"SentAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_state",
                table: "orders",
                sql: "\"Status\" IN ('Legacy', 'Open', 'Sent')");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_DishId",
                table: "order_items",
                column: "DishId");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_OrderId",
                table: "order_items",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropIndex(
                name: "UX_orders_one_open_per_table",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_after_created",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_state",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "orders");
        }
    }
}
