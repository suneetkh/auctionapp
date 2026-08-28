using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260827230000_AddSelectionDisplayMode")]
public class AddSelectionDisplayMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "SelectionDisplayMode",
        table: "AuctionRules",
        type: "TEXT",
        nullable: false,
        defaultValue: "Meter");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "SelectionDisplayMode",
        table: "AuctionRules");
}
