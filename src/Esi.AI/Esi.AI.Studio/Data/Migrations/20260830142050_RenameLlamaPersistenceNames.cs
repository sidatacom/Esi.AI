using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

public partial class RenameLlamaPersistenceNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "LlamaModels",
            newName: "Models");

        migrationBuilder.DropPrimaryKey(
            name: "PK_LlamaConfigurationProfiles",
            table: "LlamaConfigurationProfiles");

        migrationBuilder.RenameTable(
            name: "LlamaConfigurationProfiles",
            newName: "ModelConfigurationProfiles");

        migrationBuilder.AddPrimaryKey(
            name: "PK_ModelConfigurationProfiles",
            table: "ModelConfigurationProfiles",
            column: "Id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "Models",
            newName: "LlamaModels");

        migrationBuilder.DropPrimaryKey(
            name: "PK_ModelConfigurationProfiles",
            table: "ModelConfigurationProfiles");

        migrationBuilder.RenameTable(
            name: "ModelConfigurationProfiles",
            newName: "LlamaConfigurationProfiles");

        migrationBuilder.AddPrimaryKey(
            name: "PK_LlamaConfigurationProfiles",
            table: "LlamaConfigurationProfiles",
            column: "Id");
    }
}
