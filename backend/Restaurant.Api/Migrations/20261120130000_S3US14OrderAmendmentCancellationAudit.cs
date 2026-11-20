using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US14-T01: append-only order change history with immutable snapshots.
/// Existing orders are unchanged; no historical actions are invented.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261120130000_S3US14OrderAmendmentCancellationAudit")]
public partial class S3US14OrderAmendmentCancellationAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "order_change_audits",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RestaurantId = table.Column<int>(type: "integer", nullable: false),
                DiningTableId = table.Column<int>(type: "integer", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                OrderItemId = table.Column<int>(type: "integer", nullable: true),
                ChangedByStaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                ChangeType = table.Column<string>(type: "character varying(16)",
                    maxLength: 16, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)",
                    maxLength: 500, nullable: false),
                ChangedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                BeforeSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                AfterSnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_change_audits", x => x.Id);
                table.CheckConstraint("CK_order_change_audits_type",
                    "\"ChangeType\" IN ('Amended', 'Cancelled')");
                table.CheckConstraint("CK_order_change_audits_reason",
                    "length(btrim(\"Reason\")) > 0");
                table.CheckConstraint("CK_order_change_audits_item",
                    "\"OrderItemId\" IS NULL OR \"OrderItemId\" > 0");
                table.CheckConstraint("CK_order_change_audits_snapshots",
                    "jsonb_typeof(\"BeforeSnapshotJson\") = 'object' " +
                    "AND ((\"ChangeType\" = 'Amended' AND " +
                    "\"AfterSnapshotJson\" IS NOT NULL AND " +
                    "jsonb_typeof(\"AfterSnapshotJson\") = 'object') " +
                    "OR (\"ChangeType\" = 'Cancelled' AND " +
                    "\"AfterSnapshotJson\" IS NULL))");
                table.ForeignKey(
                    name: "FK_order_change_audits_orders_RestaurantId_DiningTableId_OrderId",
                    columns: x => new { x.RestaurantId, x.DiningTableId, x.OrderId },
                    principalTable: "orders",
                    principalColumns: new[] { "RestaurantId", "DiningTableId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_order_change_audits_staff_memberships_RestaurantId_ChangedByStaffMembershipId",
                    columns: x => new { x.RestaurantId, x.ChangedByStaffMembershipId },
                    principalTable: "staff_memberships",
                    principalColumns: new[] { "RestaurantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_order_change_audits_order_history",
            table: "order_change_audits",
            columns: new[] { "RestaurantId", "OrderId", "ChangedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_order_change_audits_actor_history",
            table: "order_change_audits",
            columns: new[] { "RestaurantId", "ChangedByStaffMembershipId", "ChangedAtUtc" });
        // Preserve evidence even when an API accidentally tries to edit or
        // delete history. Corrections require a new audit entry.
        migrationBuilder.Sql(@"
CREATE FUNCTION prevent_order_change_audit_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'order_change_audits is append-only';
END;
$$;
CREATE TRIGGER TR_order_change_audits_append_only
BEFORE UPDATE OR DELETE ON order_change_audits
FOR EACH ROW EXECUTE FUNCTION prevent_order_change_audit_mutation();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_change_audits");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_order_change_audit_mutation();");
    }
}
