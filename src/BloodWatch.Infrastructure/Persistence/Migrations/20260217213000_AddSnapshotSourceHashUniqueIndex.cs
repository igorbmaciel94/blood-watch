using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

public partial class AddSnapshotSourceHashUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_snapshots_source_id_hash",
            table: "snapshots",
            columns: new[] { "source_id", "hash" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_snapshots_source_id_hash",
            table: "snapshots");
    }
}
