using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828100000_AddChatMessageBackend")]
public partial class AddChatMessageBackend : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Backend",
            table: "ChatMessages",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Backend",
            table: "ChatMessages");
    }
}
