using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260823001500_AddAuctionUserAccess")]
public class AddAuctionUserAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuctionUserAccess",
            columns: table => new
            {
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                AuctionId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuctionUserAccess", x => new { x.UserId, x.AuctionId });
                table.ForeignKey(
                    name: "FK_AuctionUserAccess_Auctions_AuctionId",
                    column: x => x.AuctionId,
                    principalTable: "Auctions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AuctionUserAccess_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuctionUserAccess_AuctionId",
            table: "AuctionUserAccess",
            column: "AuctionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AuctionUserAccess");
}
