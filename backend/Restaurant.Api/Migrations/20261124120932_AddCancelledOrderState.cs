using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelledOrderState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_state",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders",
                sql: "(\"Status\" IN ('Sent', 'Closed') AND \"SentAtUtc\" IS NOT NULL) OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL) OR \"Status\" = 'Cancelled'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_state",
                table: "orders",
                sql: "\"Status\" IN ('Legacy', 'Open', 'Sent', 'Closed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM orders WHERE ""Status"" = 'Cancelled') THEN
        RAISE EXCEPTION 'Cannot remove cancelled-order status while cancelled orders exist.';
    END IF;
END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_state",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders",
                sql: "(\"Status\" IN ('Sent', 'Closed') AND \"SentAtUtc\" IS NOT NULL) OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_state",
                table: "orders",
                sql: "\"Status\" IN ('Legacy', 'Open', 'Sent', 'Closed')");
        }
    }
}
