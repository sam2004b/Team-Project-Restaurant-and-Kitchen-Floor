using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantAccountsAndStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "restaurants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, computedColumnSql: "upper(btrim(\"Name\"))", stored: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurants", x => x.Id);
                    table.CheckConstraint("CK_restaurants_address_not_blank", "length(btrim(\"Address\")) > 0");
                    table.CheckConstraint("CK_restaurants_name_not_blank", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "staff_invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, computedColumnSql: "upper(btrim(\"Email\"))", stored: true),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_invitations", x => x.Id);
                    table.CheckConstraint("CK_staff_invitations_email_not_blank", "length(btrim(\"Email\")) > 0");
                    table.CheckConstraint("CK_staff_invitations_role", "\"Role\" IN ('Administrator', 'Waiter', 'Chef', 'Reception Manager')");
                    table.CheckConstraint("CK_staff_invitations_status", "\"Status\" IN ('Pending', 'Accepted', 'Revoked')");
                    table.ForeignKey(
                        name: "FK_staff_invitations_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_memberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_memberships", x => x.Id);
                    table.CheckConstraint("CK_staff_memberships_role", "\"Role\" IN ('Administrator', 'Waiter', 'Chef', 'Reception Manager')");
                    table.CheckConstraint("CK_staff_memberships_status", "\"Status\" IN ('Active', 'Revoked')");
                    table.ForeignKey(
                        name: "FK_staff_memberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_memberships_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_restaurants_NormalizedName",
                table: "restaurants",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_invitations_RestaurantId_NormalizedEmail",
                table: "staff_invitations",
                columns: new[] { "RestaurantId", "NormalizedEmail" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_staff_memberships_RestaurantId_UserId",
                table: "staff_memberships",
                columns: new[] { "RestaurantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_memberships_UserId",
                table: "staff_memberships",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_invitations");

            migrationBuilder.DropTable(
                name: "staff_memberships");

            migrationBuilder.DropTable(
                name: "restaurants");
        }
    }
}
