using App.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260827233000_AddSelectionRevealPending")]
public class AddSelectionRevealPending : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "SelectionRevealPending",
        table: "Auctions",
        type: "INTEGER",
        nullable: false,
        defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "SelectionRevealPending",
        table: "Auctions");
}
