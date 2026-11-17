using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US4-T01: per-line dish preparation state and nullable milestone times.
/// Existing and future items start Pending; historical timestamps are not invented.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261118013000_S3US4DishPreparationStatus")]
public partial class S3US4DishPreparationStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL fills existing Sprint 2/3 lines with Pending. Future lines
        // also default to Pending when inserted without a status.
        migrationBuilder.AddColumn<string>(
            name: "PreparationStatus", table: "order_items",
            type: "character varying(16)", maxLength: 16,
            nullable: false, defaultValue: "Pending");

        migrationBuilder.AddColumn<DateTime>(
            name: "PreparationStartedAtUtc", table: "order_items",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ReadyAtUtc", table: "order_items",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_preparation_status",
            table: "order_items",
            sql: "\"PreparationStatus\" IN ('Pending', 'Preparing', 'Ready')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_preparation_requires_submission",
            table: "order_items",
            sql: "\"KitchenSubmissionId\" IS NOT NULL OR \"PreparationStatus\" = 'Pending'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_order_items_preparation_timestamps",
            table: "order_items",
            sql: "(\"PreparationStatus\" = 'Pending' AND " +
                 "\"PreparationStartedAtUtc\" IS NULL AND \"ReadyAtUtc\" IS NULL) " +
                 "OR (\"PreparationStatus\" = 'Preparing' AND " +
                 "\"PreparationStartedAtUtc\" IS NOT NULL AND \"ReadyAtUtc\" IS NULL) " +
                 "OR (\"PreparationStatus\" = 'Ready' AND " +
                 "\"PreparationStartedAtUtc\" IS NOT NULL AND \"ReadyAtUtc\" IS NOT NULL " +
                 "AND \"ReadyAtUtc\" >= \"PreparationStartedAtUtc\")");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_preparation_timestamps", table: "order_items");
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_preparation_requires_submission", table: "order_items");
        migrationBuilder.DropCheckConstraint(
            name: "CK_order_items_preparation_status", table: "order_items");
        migrationBuilder.DropColumn(name: "ReadyAtUtc", table: "order_items");
        migrationBuilder.DropColumn(name: "PreparationStartedAtUtc", table: "order_items");
        migrationBuilder.DropColumn(name: "PreparationStatus", table: "order_items");
    }
}
