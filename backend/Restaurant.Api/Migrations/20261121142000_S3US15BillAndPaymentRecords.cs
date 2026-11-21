using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US15-T01: new bill and append-only payment tables. Existing orders
/// are left intact; no unpaid orders are falsely marked as paid.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261121142000_S3US15BillAndPaymentRecords")]
public partial class S3US15BillAndPaymentRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bills",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RestaurantId = table.Column<int>(type: "integer", nullable: false),
                DiningTableId = table.Column<int>(type: "integer", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                CurrencyCode = table.Column<string>(type: "character varying(3)",
                    maxLength: 3, nullable: false),
                SubtotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                TaxAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false,
                    defaultValue: 0m),
                DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false,
                    defaultValue: 0m),
                TotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bills", x => x.Id);
                table.UniqueConstraint("AK_bills_RestaurantId_Id", x => new { x.RestaurantId, x.Id });
                table.CheckConstraint("CK_bills_amounts_nonnegative",
                    "\"SubtotalAmount\" >= 0 AND \"TaxAmount\" >= 0 AND " +
                    "\"DiscountAmount\" >= 0 AND \"TotalAmount\" >= 0");
                table.CheckConstraint("CK_bills_total_consistency",
                    "\"TotalAmount\" = \"SubtotalAmount\" + \"TaxAmount\" - \"DiscountAmount\" " +
                    "AND \"DiscountAmount\" <= \"SubtotalAmount\" + \"TaxAmount\"");
                table.CheckConstraint("CK_bills_currency_code",
                    "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                table.ForeignKey(
                    name: "FK_bills_orders_RestaurantId_DiningTableId_OrderId",
                    columns: x => new { x.RestaurantId, x.DiningTableId, x.OrderId },
                    principalTable: "orders",
                    principalColumns: new[] { "RestaurantId", "DiningTableId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_bills_restaurant_order",
            table: "bills",
            columns: new[] { "RestaurantId", "OrderId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_bills_restaurant_table_created",
            table: "bills",
            columns: new[] { "RestaurantId", "DiningTableId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_bills_RestaurantId_DiningTableId_OrderId",
            table: "bills",
            columns: new[] { "RestaurantId", "DiningTableId", "OrderId" });

        migrationBuilder.CreateTable(
            name: "payment_records",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RestaurantId = table.Column<int>(type: "integer", nullable: false),
                BillId = table.Column<int>(type: "integer", nullable: false),
                RecordedByStaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                Kind = table.Column<string>(type: "character varying(16)",
                    maxLength: 16, nullable: false),
                Method = table.Column<string>(type: "character varying(16)",
                    maxLength: 16, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                ExternalReference = table.Column<string>(type: "character varying(120)",
                    maxLength: 120, nullable: true),
                RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_records", x => x.Id);
                table.CheckConstraint("CK_payment_records_amount_positive",
                    "\"Amount\" > 0");
                table.CheckConstraint("CK_payment_records_kind",
                    "\"Kind\" IN ('Payment', 'Refund')");
                table.CheckConstraint("CK_payment_records_method",
                    "\"Method\" IN ('Cash', 'Card', 'Other')");
                table.CheckConstraint("CK_payment_records_reference",
                    "\"ExternalReference\" IS NULL OR length(btrim(\"ExternalReference\")) > 0");
                table.ForeignKey(
                    name: "FK_payment_records_bills_RestaurantId_BillId",
                    columns: x => new { x.RestaurantId, x.BillId },
                    principalTable: "bills",
                    principalColumns: new[] { "RestaurantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_payment_records_staff_memberships_RestaurantId_RecordedByStaffMembershipId",
                    columns: x => new { x.RestaurantId, x.RecordedByStaffMembershipId },
                    principalTable: "staff_memberships",
                    principalColumns: new[] { "RestaurantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payment_records_bill_history", table: "payment_records",
            columns: new[] { "RestaurantId", "BillId", "RecordedAtUtc", "Id" });
        migrationBuilder.CreateIndex(
            name: "IX_payment_records_RestaurantId_RecordedByStaffMembershipId",
            table: "payment_records",
            columns: new[] { "RestaurantId", "RecordedByStaffMembershipId" });
        migrationBuilder.CreateIndex(
            name: "UX_payment_records_external_reference", table: "payment_records",
            columns: new[] { "RestaurantId", "ExternalReference" },
            unique: true,
            filter: "\"ExternalReference\" IS NOT NULL");

        // Payment/refund history must never be silently overwritten or deleted.
        // Reversals must be NEW rows and are handled by a later authorized API.
        migrationBuilder.Sql(@"
CREATE FUNCTION prevent_payment_record_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'payment_records is append-only';
END;
$$;
CREATE TRIGGER TR_payment_records_append_only
BEFORE UPDATE OR DELETE ON payment_records
FOR EACH ROW EXECUTE FUNCTION prevent_payment_record_mutation();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payment_records");
        migrationBuilder.DropTable(name: "bills");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_payment_record_mutation();");
    }
}
