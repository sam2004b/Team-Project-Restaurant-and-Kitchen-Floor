using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantSettingsTablesAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_staff_memberships_RestaurantId_Id",
                table: "staff_memberships",
                columns: new[] { "RestaurantId", "Id" });

            migrationBuilder.CreateTable(
                name: "dining_tables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    TableNumber = table.Column<int>(type: "integer", nullable: false),
                    SeatCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tables", x => x.Id);
                    table.UniqueConstraint("AK_dining_tables_RestaurantId_Id", x => new { x.RestaurantId, x.Id });
                    table.CheckConstraint("CK_dining_tables_number_positive", "\"TableNumber\" > 0");
                    table.CheckConstraint("CK_dining_tables_seats_positive", "\"SeatCount\" > 0");
                    table.ForeignKey(
                        name: "FK_dining_tables_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_settings",
                columns: table => new
                {
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    DefaultSeatCapacity = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_settings", x => x.RestaurantId);
                    table.CheckConstraint("CK_restaurant_settings_default_seats_positive", "\"DefaultSeatCapacity\" > 0");
                    table.ForeignKey(
                        name: "FK_restaurant_settings_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "table_staff_assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    DiningTableId = table.Column<int>(type: "integer", nullable: false),
                    StaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_staff_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_table_staff_assignments_dining_tables_RestaurantId_DiningTa~",
                        columns: x => new { x.RestaurantId, x.DiningTableId },
                        principalTable: "dining_tables",
                        principalColumns: new[] { "RestaurantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_table_staff_assignments_staff_memberships_RestaurantId_Staf~",
                        columns: x => new { x.RestaurantId, x.StaffMembershipId },
                        principalTable: "staff_memberships",
                        principalColumns: new[] { "RestaurantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dining_tables_RestaurantId_TableNumber",
                table: "dining_tables",
                columns: new[] { "RestaurantId", "TableNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_staff_assignments_DiningTableId",
                table: "table_staff_assignments",
                column: "DiningTableId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_staff_assignments_RestaurantId_DiningTableId",
                table: "table_staff_assignments",
                columns: new[] { "RestaurantId", "DiningTableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_staff_assignments_RestaurantId_StaffMembershipId",
                table: "table_staff_assignments",
                columns: new[] { "RestaurantId", "StaffMembershipId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "restaurant_settings");

            migrationBuilder.DropTable(
                name: "table_staff_assignments");

            migrationBuilder.DropTable(
                name: "dining_tables");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_staff_memberships_RestaurantId_Id",
                table: "staff_memberships");
        }
    }
}
