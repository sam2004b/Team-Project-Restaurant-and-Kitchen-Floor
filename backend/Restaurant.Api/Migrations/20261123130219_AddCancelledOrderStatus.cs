using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelledOrderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_waiter_notifications_RestaurantId_WaiterStaffMembershipId",
                table: "waiter_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_state",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders",
                sql: "(\"Status\" IN ('Sent', 'Closed', 'Cancelled') AND \"SentAtUtc\" IS NOT NULL) OR (\"Status\" IN ('Legacy', 'Open') AND \"SentAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_state",
                table: "orders",
                sql: "\"Status\" IN ('Legacy', 'Open', 'Sent', 'Closed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_sent_consistency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_state",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_waiter_notifications_RestaurantId_WaiterStaffMembershipId",
                table: "waiter_notifications",
                columns: new[] { "RestaurantId", "WaiterStaffMembershipId" });

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
