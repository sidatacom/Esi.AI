using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackendDeviceRoutingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelSettings_Backend",
                table: "ModelSettings");

            migrationBuilder.AddColumn<string>(
                name: "BackendVariantId",
                table: "ModelSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Devices",
                table: "ModelSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_ModelSettings_Backend_BackendVariantId",
                table: "ModelSettings",
                columns: new[] { "Backend", "BackendVariantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelSettings_Backend_BackendVariantId",
                table: "ModelSettings");

            migrationBuilder.DropColumn(
                name: "BackendVariantId",
                table: "ModelSettings");

            migrationBuilder.DropColumn(
                name: "Devices",
                table: "ModelSettings");

            migrationBuilder.CreateIndex(
                name: "IX_ModelSettings_Backend",
                table: "ModelSettings",
                column: "Backend",
                unique: true);
        }
    }
}
