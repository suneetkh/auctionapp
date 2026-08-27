using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260811030000_AddSoldPresentationRules")]
public partial class AddSoldPresentationRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "SoldAnimationEnabled",
            table: "AuctionRules",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "SoldSoundEnabled",
            table: "AuctionRules",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SoldAnimationEnabled", table: "AuctionRules");
        migrationBuilder.DropColumn(name: "SoldSoundEnabled", table: "AuctionRules");
    }
}
