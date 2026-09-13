using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

/// <inheritdoc />
public partial class AddChatPerformanceMetrics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "TimeToFirstTokenMs",
            table: "ChatMessages",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PrefillDurationMs",
            table: "ChatMessages",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "DecodeDurationMs",
            table: "ChatMessages",
            type: "REAL",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TimeToFirstTokenMs",
            table: "ChatMessages");

        migrationBuilder.DropColumn(
            name: "PrefillDurationMs",
            table: "ChatMessages");

        migrationBuilder.DropColumn(
            name: "DecodeDurationMs",
            table: "ChatMessages");
    }
}
