using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BloodWatchDbContext))]
[Migration("20260219210000_AddAlertingSubscriptionScopesAndIdempotency")]
public partial class AddAlertingSubscriptionScopesAndIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "attempt_count",
            table: "deliveries",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "idempotency_key",
            table: "events",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "metric_filter",
            table: "subscriptions",
            type: "text",
            nullable: true);

        migrationBuilder.Sql("""
            update subscriptions
            set is_enabled = false,
                disabled_at_utc = now(),
                region_filter = 'legacy-unset'
            where region_filter is null;
            """);

        migrationBuilder.Sql("""
            update subscriptions
            set metric_filter = 'overall'
            where metric_filter is null;
            """);

        migrationBuilder.Sql("""
            update events
            set idempotency_key = "Id"::text
            where idempotency_key is null;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "region_filter",
            table: "subscriptions",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "metric_filter",
            table: "subscriptions",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "idempotency_key",
            table: "events",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_event_id_subscription_id",
            table: "deliveries",
            columns: new[] { "event_id", "subscription_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_events_idempotency_key",
            table: "events",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_subscriptions_source_id_region_filter_metric_filter_is_enabled",
            table: "subscriptions",
            columns: new[] { "source_id", "region_filter", "metric_filter", "is_enabled" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_deliveries_event_id_subscription_id",
            table: "deliveries");

        migrationBuilder.DropIndex(
            name: "IX_events_idempotency_key",
            table: "events");

        migrationBuilder.DropIndex(
            name: "IX_subscriptions_source_id_region_filter_metric_filter_is_enabled",
            table: "subscriptions");

        migrationBuilder.DropColumn(
            name: "attempt_count",
            table: "deliveries");

        migrationBuilder.DropColumn(
            name: "idempotency_key",
            table: "events");

        migrationBuilder.DropColumn(
            name: "metric_filter",
            table: "subscriptions");

        migrationBuilder.AlterColumn<string>(
            name: "region_filter",
            table: "subscriptions",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text");
    }
}
