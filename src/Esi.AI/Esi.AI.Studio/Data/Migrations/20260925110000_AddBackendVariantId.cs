using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925110000_AddBackendVariantId")]
public sealed class AddBackendVariantId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BackendVariantId",
            table: "ModelConfigurations",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BackendVariantId",
            table: "ModelConfigurations");
    }
}