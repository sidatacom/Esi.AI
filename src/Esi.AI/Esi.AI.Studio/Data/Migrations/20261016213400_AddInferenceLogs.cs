using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261016213400_AddInferenceLogs")]
public sealed class AddInferenceLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InferenceLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InferenceId = table.Column<string>(type: "TEXT", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Path = table.Column<string>(type: "TEXT", nullable: false),
                Backend = table.Column<string>(type: "TEXT", nullable: false),
                Model = table.Column<string>(type: "TEXT", nullable: false),
                BackendVariantId = table.Column<string>(type: "TEXT", nullable: true),
                MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                ImageCount = table.Column<int>(type: "INTEGER", nullable: false),
                ToolCount = table.Column<int>(type: "INTEGER", nullable: false),
                Streaming = table.Column<bool>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                EstimatedPrefillTokens = table.Column<int>(type: "INTEGER", nullable: true),
                OutputTokenCount = table.Column<int>(type: "INTEGER", nullable: true),
                FinishReason = table.Column<string>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                ErrorType = table.Column<string>(type: "TEXT", nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_InferenceLogs", log => log.Id));

        migrationBuilder.CreateIndex(
            name: "IX_InferenceLogs_InferenceId",
            table: "InferenceLogs",
            column: "InferenceId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_InferenceLogs_StartedAtUtc",
            table: "InferenceLogs",
            column: "StartedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "InferenceLogs");
}
