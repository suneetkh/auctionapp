using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260825150000_AddSoldAnimationStyle")]
public class AddSoldAnimationStyle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "SoldAnimationStyle", table: "AuctionRules", type: "TEXT", nullable: false, defaultValue: "Stamp");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "SoldAnimationStyle", table: "AuctionRules");
}
