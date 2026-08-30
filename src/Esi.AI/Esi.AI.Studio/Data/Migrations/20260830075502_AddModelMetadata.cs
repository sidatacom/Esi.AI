using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelPath = table.Column<string>(type: "TEXT", nullable: false),
                    CompatibleBackendsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HuggingFaceModelId = table.Column<string>(type: "TEXT", nullable: true),
                    HuggingFaceRevision = table.Column<string>(type: "TEXT", nullable: true),
                    HuggingFaceSynchronizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsManuallyConfigured = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelMetadata", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelMetadata");
        }
    }
}
