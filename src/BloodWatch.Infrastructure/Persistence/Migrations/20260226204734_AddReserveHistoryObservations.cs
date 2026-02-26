using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReserveHistoryObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reserve_history_observations",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_key = table.Column<string>(type: "text", nullable: false),
                    status_key = table.Column<string>(type: "text", nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status_rank = table.Column<short>(type: "smallint", nullable: false),
                    captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reserve_history_observations", x => new { x.source_id, x.region_id, x.metric_key, x.reference_date, x.status_key });
                    table.ForeignKey(
                        name: "FK_reserve_history_observations_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reserve_history_observations_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reserve_history_observations_region_id",
                table: "reserve_history_observations",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_reserve_history_observations_source_id_metric_key_reference~",
                table: "reserve_history_observations",
                columns: new[] { "source_id", "metric_key", "reference_date" });

            migrationBuilder.CreateIndex(
                name: "IX_reserve_history_observations_source_id_region_id_metric_key~",
                table: "reserve_history_observations",
                columns: new[] { "source_id", "region_id", "metric_key", "captured_at_utc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_reserve_history_observations_source_id_status_rank_referenc~",
                table: "reserve_history_observations",
                columns: new[] { "source_id", "status_rank", "reference_date" });

            migrationBuilder.Sql(
                """
                INSERT INTO reserve_history_observations (
                    source_id,
                    region_id,
                    metric_key,
                    status_key,
                    reference_date,
                    status_rank,
                    captured_at_utc)
                SELECT
                    cr.source_id,
                    cr.region_id,
                    cr.metric_key,
                    LOWER(BTRIM(cr.status_key)) AS status_key,
                    COALESCE(cr.reference_date, (cr.captured_at_utc AT TIME ZONE 'UTC')::date) AS reference_date,
                    CASE LOWER(BTRIM(cr.status_key))
                        WHEN 'normal' THEN 0
                        WHEN 'watch' THEN 1
                        WHEN 'warning' THEN 2
                        WHEN 'critical' THEN 3
                        ELSE -1
                    END::smallint AS status_rank,
                    NOW() AS captured_at_utc
                FROM current_reserves cr
                ON CONFLICT (source_id, region_id, metric_key, reference_date, status_key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reserve_history_observations");
        }
    }
}
