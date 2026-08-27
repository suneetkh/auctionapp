using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260826210000_AddTournamentPlanningLocks")]
public class AddTournamentPlanningLocks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DrawLocked",
            table: "AuctionPlanningStates",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "FixturesLocked",
            table: "AuctionPlanningStates",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DrawLocked", table: "AuctionPlanningStates");
        migrationBuilder.DropColumn(name: "FixturesLocked", table: "AuctionPlanningStates");
    }
}
