using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

 [DbContext(typeof(ApplicationDbContext))]
 [Migration("20260827133000_AddModelConfigurationProfileId")]
public partial class AddModelConfigurationProfileId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigurationProfileId",
            table: "LlamaModels",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ConfigurationProfileId",
            table: "LlamaModels");
    }
}