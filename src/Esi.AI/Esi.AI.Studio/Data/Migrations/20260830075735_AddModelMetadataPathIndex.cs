using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelMetadataPathIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ModelMetadata_ModelPath",
                table: "ModelMetadata",
                column: "ModelPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelMetadata_ModelPath",
                table: "ModelMetadata");
        }
    }
}
