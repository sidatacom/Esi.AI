using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104111800_AddInferenceLogPerformanceMetrics")]
public sealed class AddInferenceLogPerformanceMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PromptTokenCount",
            table: "InferenceLogs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "TimeToFirstTokenMs",
            table: "InferenceLogs",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PrefillDurationMs",
            table: "InferenceLogs",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "DecodeDurationMs",
            table: "InferenceLogs",
            type: "REAL",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PromptTokenCount", table: "InferenceLogs");
        migrationBuilder.DropColumn(name: "TimeToFirstTokenMs", table: "InferenceLogs");
        migrationBuilder.DropColumn(name: "PrefillDurationMs", table: "InferenceLogs");
        migrationBuilder.DropColumn(name: "DecodeDurationMs", table: "InferenceLogs");
    }
}
