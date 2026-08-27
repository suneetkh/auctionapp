using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260824190000_AddAuctionPlanningState")]
public class AddAuctionPlanningState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuctionPlanningStates",
            columns: table => new
            {
                AuctionId = table.Column<int>(type: "INTEGER", nullable: false),
                DrawStateJson = table.Column<string>(type: "TEXT", nullable: true),
                FixtureStateJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuctionPlanningStates", x => x.AuctionId);
                table.ForeignKey("FK_AuctionPlanningStates_Auctions_AuctionId", x => x.AuctionId, "Auctions", "Id", onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("AuctionPlanningStates");
}
