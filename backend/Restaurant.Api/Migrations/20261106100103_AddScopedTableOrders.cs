using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedTableOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    DiningTableId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByStaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_orders_dining_tables_RestaurantId_DiningTableId",
                        columns: x => new { x.RestaurantId, x.DiningTableId },
                        principalTable: "dining_tables",
                        principalColumns: new[] { "RestaurantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_staff_memberships_RestaurantId_CreatedByStaffMembers~",
                        columns: x => new { x.RestaurantId, x.CreatedByStaffMembershipId },
                        principalTable: "staff_memberships",
                        principalColumns: new[] { "RestaurantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_RestaurantId_CreatedByStaffMembershipId",
                table: "orders",
                columns: new[] { "RestaurantId", "CreatedByStaffMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_RestaurantId_DiningTableId_CreatedAtUtc",
                table: "orders",
                columns: new[] { "RestaurantId", "DiningTableId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
