using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeModelPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ConfigurationProfileId",
                table: "Models",
                newName: "ConfigurationId");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModelConfigurationProfiles",
                table: "ModelConfigurationProfiles");

            migrationBuilder.RenameTable(
                name: "ModelConfigurationProfiles",
                newName: "ModelConfigurations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModelConfigurations",
                table: "ModelConfigurations",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ModelSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelPath = table.Column<string>(type: "TEXT", nullable: false),
                    Backend = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelSettings_Backend",
                table: "ModelSettings",
                column: "Backend",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO ModelSettings (ModelPath, Backend, ConfigurationJson, ConfigurationId, UpdatedAtUtc)
                SELECT ModelPath,
                       0,
                       json_object(
                           'ModelPath', ModelPath,
                           'Backend', Backend,
                           'GpuLayerCount', GpuLayerCount,
                           'ContextSize', ContextSize,
                           'VulkanDeviceWeights', json(VulkanDeviceWeightsJson),
                           'Advanced', json(AdvancedSettingsJson)),
                       ConfigurationProfileId,
                       datetime('now')
                FROM LlamaSettings;

                INSERT INTO ModelSettings (ModelPath, Backend, ConfigurationJson, ConfigurationId, UpdatedAtUtc)
                SELECT json_extract(SettingsJson, '$.ModelPath'),
                       1,
                       SettingsJson,
                       NULL,
                       datetime('now')
                FROM OpenVinoSettings;
                """);

            migrationBuilder.DropTable(
                name: "LlamaSettings");

            migrationBuilder.DropTable(
                name: "OpenVinoSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelConfigurations");

            migrationBuilder.DropTable(
                name: "ModelSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModelConfigurations",
                table: "ModelConfigurations");

            migrationBuilder.RenameTable(
                name: "ModelConfigurations",
                newName: "ModelConfigurationProfiles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModelConfigurationProfiles",
                table: "ModelConfigurationProfiles",
                column: "Id");

            migrationBuilder.RenameColumn(
                name: "ConfigurationId",
                table: "Models",
                newName: "ConfigurationProfileId");

            migrationBuilder.CreateTable(
                name: "LlamaSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdvancedSettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Backend = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigurationProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContextSize = table.Column<uint>(type: "INTEGER", nullable: false),
                    GpuLayerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelPath = table.Column<string>(type: "TEXT", nullable: false),
                    VulkanDeviceWeightsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlamaSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelConfigurationProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Backend = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModelPath = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelConfigurationProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenVinoSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenVinoSettings", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO LlamaSettings (ModelPath, Backend, GpuLayerCount, ContextSize, VulkanDeviceWeightsJson, AdvancedSettingsJson, ConfigurationProfileId)
                SELECT ModelPath,
                       json_extract(ConfigurationJson, '$.Backend'),
                       json_extract(ConfigurationJson, '$.GpuLayerCount'),
                       json_extract(ConfigurationJson, '$.ContextSize'),
                       json(ConfigurationJson -> '$.VulkanDeviceWeights'),
                       json(ConfigurationJson -> '$.Advanced'),
                       ConfigurationId
                FROM ModelSettings
                WHERE Backend = 0;

                INSERT INTO OpenVinoSettings (SettingsJson)
                SELECT ConfigurationJson
                FROM ModelSettings
                WHERE Backend = 1;
                """);
        }
    }
}
