using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BloodWatchDbContext))]
[Migration("20260218210000_AddCurrentReservesAndSourceLastPolledAt")]
public partial class AddCurrentReservesAndSourceLastPolledAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "last_polled_at_utc",
            table: "sources",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "current_reserves",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                region_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "text", nullable: false),
                value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                severity = table.Column<string>(type: "text", nullable: true),
                reference_date = table.Column<DateOnly>(type: "date", nullable: true),
                captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_current_reserves", x => x.Id);
                table.ForeignKey(
                    name: "FK_current_reserves_regions_region_id",
                    column: x => x.region_id,
                    principalTable: "regions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_current_reserves_sources_source_id",
                    column: x => x.source_id,
                    principalTable: "sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_current_reserves_region_id",
            table: "current_reserves",
            column: "region_id");

        migrationBuilder.CreateIndex(
            name: "IX_current_reserves_source_id_captured_at_utc",
            table: "current_reserves",
            columns: new[] { "source_id", "captured_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_current_reserves_source_id_region_id_metric_key",
            table: "current_reserves",
            columns: new[] { "source_id", "region_id", "metric_key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "current_reserves");

        migrationBuilder.DropColumn(
            name: "last_polled_at_utc",
            table: "sources");
    }
}
