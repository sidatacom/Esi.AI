using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103095800_AddInferenceLogPayloads")]
public sealed class AddInferenceLogPayloads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestPayload",
            table: "InferenceLogs",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResponsePayload",
            table: "InferenceLogs",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RequestPayload",
            table: "InferenceLogs");

        migrationBuilder.DropColumn(
            name: "ResponsePayload",
            table: "InferenceLogs");
    }
}
