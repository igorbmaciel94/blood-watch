using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BloodWatchDbContext))]
[Migration("20260219233000_AddSubscriptionNotificationStates")]
public partial class AddSubscriptionNotificationStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscription_notification_states",
            columns: table => new
            {
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_low_open = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                last_low_notified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_low_notified_bucket = table.Column<int>(type: "integer", nullable: true),
                last_low_notified_units = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                last_recovery_notified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscription_notification_states", x => x.subscription_id);
                table.ForeignKey(
                    name: "FK_subscription_notification_states_subscriptions_subscription_id",
                    column: x => x.subscription_id,
                    principalTable: "subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_subscription_notification_states_updated_at_utc",
            table: "subscription_notification_states",
            column: "updated_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "subscription_notification_states");
    }
}
