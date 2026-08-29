using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    Library = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationPath = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", nullable: false),
                    FileNamesJson = table.Column<string>(type: "TEXT", nullable: false),
                    FileStatusesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelDownloads", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelDownloads");
        }
    }
}
