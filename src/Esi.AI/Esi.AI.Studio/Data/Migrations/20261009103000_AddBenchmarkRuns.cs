using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009103000_AddBenchmarkRuns")]
public sealed class AddBenchmarkRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BenchmarkRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                Backend = table.Column<int>(type: "INTEGER", nullable: false),
                BackendVariantId = table.Column<string>(type: "TEXT", nullable: false),
                ModelPath = table.Column<string>(type: "TEXT", nullable: false),
                ProfileSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                MeasurementsJson = table.Column<string>(type: "TEXT", nullable: false),
                State = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_BenchmarkRuns", run => run.Id));

        migrationBuilder.CreateIndex(
            name: "IX_BenchmarkRuns_ModelPath_CreatedAtUtc",
            table: "BenchmarkRuns",
            columns: new[] { "ModelPath", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_BenchmarkRuns_ProfileId_CreatedAtUtc",
            table: "BenchmarkRuns",
            columns: new[] { "ProfileId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "BenchmarkRuns");
}
