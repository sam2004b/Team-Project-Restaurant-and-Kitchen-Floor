using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Restaurant.Api.Data;

#nullable disable

namespace Restaurant.Api.Migrations;

/// <summary>
/// S3-US16-T01: immutable, tenant-scoped lifecycle event model.
/// Does not backfill invented historical events for existing orders.
/// </summary>
[DbContext(typeof(RestaurantDbContext))]
[Migration("20261123090000_S3US16OrderHistoryEvents")]
public partial class S3US16OrderHistoryEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "order_history_events",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RestaurantId = table.Column<int>(type: "integer", nullable: false),
                DiningTableId = table.Column<int>(type: "integer", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                OrderItemId = table.Column<int>(type: "integer", nullable: true),
                ActorStaffMembershipId = table.Column<int>(type: "integer", nullable: false),
                EventType = table.Column<string>(type: "character varying(64)",
                    maxLength: 64, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                BeforeValueJson = table.Column<string>(type: "jsonb", nullable: true),
                AfterValueJson = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_history_events", x => x.Id);
                table.CheckConstraint("CK_order_history_events_type_not_blank",
                    "length(btrim(\"EventType\")) > 0");
                table.CheckConstraint("CK_order_history_events_item_positive",
                    "\"OrderItemId\" IS NULL OR \"OrderItemId\" > 0");
                table.CheckConstraint("CK_order_history_events_values",
                    "(\"BeforeValueJson\" IS NOT NULL OR \"AfterValueJson\" IS NOT NULL) " +
                    "AND (\"BeforeValueJson\" IS NULL OR " +
                    "jsonb_typeof(\"BeforeValueJson\") = 'object') " +
                    "AND (\"AfterValueJson\" IS NULL OR " +
                    "jsonb_typeof(\"AfterValueJson\") = 'object')");
                table.ForeignKey(
                    name: "FK_order_history_events_orders_RestaurantId_DiningTableId_OrderId",
                    columns: x => new { x.RestaurantId, x.DiningTableId, x.OrderId },
                    principalTable: "orders",
                    principalColumns: new[] { "RestaurantId", "DiningTableId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_order_history_events_staff_memberships_RestaurantId_ActorStaffMembershipId",
                    columns: x => new { x.RestaurantId, x.ActorStaffMembershipId },
                    principalTable: "staff_memberships",
                    principalColumns: new[] { "RestaurantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_order_history_events_order_timeline",
            table: "order_history_events",
            columns: new[] { "RestaurantId", "OrderId", "OccurredAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_order_history_events_actor_timeline",
            table: "order_history_events",
            columns: new[] { "RestaurantId", "ActorStaffMembershipId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_order_history_events_RestaurantId_DiningTableId_OrderId",
            table: "order_history_events",
            columns: new[] { "RestaurantId", "DiningTableId", "OrderId" });

        // Database-level enforcement: history may be inserted but never
        // rewritten or erased. Application service must log in the same
        // transaction as the change, once those APIs are implemented.
        migrationBuilder.Sql(@"
CREATE FUNCTION prevent_order_history_event_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'order_history_events is append-only';
END;
$$;
CREATE TRIGGER TR_order_history_events_append_only
BEFORE UPDATE OR DELETE ON order_history_events
FOR EACH ROW EXECUTE FUNCTION prevent_order_history_event_mutation();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_history_events");
        migrationBuilder.Sql(
            "DROP FUNCTION IF EXISTS prevent_order_history_event_mutation();");
    }
}
