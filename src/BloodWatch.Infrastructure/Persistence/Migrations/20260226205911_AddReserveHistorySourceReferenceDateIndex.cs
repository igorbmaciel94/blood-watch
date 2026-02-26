using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReserveHistorySourceReferenceDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_reserve_history_observations_source_id_reference_date",
                table: "reserve_history_observations",
                columns: new[] { "source_id", "reference_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reserve_history_observations_source_id_reference_date",
                table: "reserve_history_observations");
        }
    }
}
