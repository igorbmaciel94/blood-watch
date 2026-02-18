using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BloodWatchDbContext))]
[Migration("20260218233000_RemoveLegacySnapshotsAndRetargetEvents")]
public partial class RemoveLegacySnapshotsAndRetargetEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deliveries");

        migrationBuilder.DropTable(
            name: "events");

        migrationBuilder.DropTable(
            name: "snapshot_items");

        migrationBuilder.DropTable(
            name: "snapshots");

        migrationBuilder.CreateTable(
            name: "events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                current_reserve_id = table.Column<Guid>(type: "uuid", nullable: false),
                region_id = table.Column<Guid>(type: "uuid", nullable: true),
                rule_key = table.Column<string>(type: "text", nullable: false),
                metric_key = table.Column<string>(type: "text", nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_events_current_reserves_current_reserve_id",
                    column: x => x.current_reserve_id,
                    principalTable: "current_reserves",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_events_regions_region_id",
                    column: x => x.region_id,
                    principalTable: "regions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_events_sources_source_id",
                    column: x => x.source_id,
                    principalTable: "sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "deliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                last_error = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_deliveries_events_event_id",
                    column: x => x.event_id,
                    principalTable: "events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_deliveries_subscriptions_subscription_id",
                    column: x => x.subscription_id,
                    principalTable: "subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_event_id_status",
            table: "deliveries",
            columns: new[] { "event_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_subscription_id",
            table: "deliveries",
            column: "subscription_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_current_reserve_id",
            table: "events",
            column: "current_reserve_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_region_id",
            table: "events",
            column: "region_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_source_id_created_at_utc",
            table: "events",
            columns: new[] { "source_id", "created_at_utc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deliveries");

        migrationBuilder.DropTable(
            name: "events");

        migrationBuilder.CreateTable(
            name: "snapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                reference_date = table.Column<DateOnly>(type: "date", nullable: true),
                hash = table.Column<string>(type: "text", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_snapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_snapshots_sources_source_id",
                    column: x => x.source_id,
                    principalTable: "sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                region_id = table.Column<Guid>(type: "uuid", nullable: true),
                rule_key = table.Column<string>(type: "text", nullable: false),
                metric_key = table.Column<string>(type: "text", nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_events_regions_region_id",
                    column: x => x.region_id,
                    principalTable: "regions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_events_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalTable: "snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_events_sources_source_id",
                    column: x => x.source_id,
                    principalTable: "sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "snapshot_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                region_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "text", nullable: false),
                value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                severity = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_snapshot_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_snapshot_items_regions_region_id",
                    column: x => x.region_id,
                    principalTable: "regions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_snapshot_items_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalTable: "snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "deliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                last_error = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_deliveries_events_event_id",
                    column: x => x.event_id,
                    principalTable: "events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_deliveries_subscriptions_subscription_id",
                    column: x => x.subscription_id,
                    principalTable: "subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_event_id_status",
            table: "deliveries",
            columns: new[] { "event_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_subscription_id",
            table: "deliveries",
            column: "subscription_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_region_id",
            table: "events",
            column: "region_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_snapshot_id",
            table: "events",
            column: "snapshot_id");

        migrationBuilder.CreateIndex(
            name: "IX_events_source_id_created_at_utc",
            table: "events",
            columns: new[] { "source_id", "created_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_snapshot_items_region_id",
            table: "snapshot_items",
            column: "region_id");

        migrationBuilder.CreateIndex(
            name: "IX_snapshot_items_snapshot_id_region_id_metric_key",
            table: "snapshot_items",
            columns: new[] { "snapshot_id", "region_id", "metric_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_snapshots_source_id_captured_at_utc",
            table: "snapshots",
            columns: new[] { "source_id", "captured_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_snapshots_source_id_hash",
            table: "snapshots",
            columns: new[] { "source_id", "hash" },
            unique: true);
    }
}
