using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260823120000_AddPlayerCaptains")]
public class AddPlayerCaptains : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "CaptainCost", table: "Players", type: "decimal(18,2)", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsCaptain", table: "Players", type: "INTEGER", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CaptainCost", table: "Players");
        migrationBuilder.DropColumn(name: "IsCaptain", table: "Players");
    }
}
