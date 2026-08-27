using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class SaleServiceTests
{
    private static (App.Api.Data.AppDbContext db, SaleService svc, Auction auction, Player player, Team team) Setup()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.Live, CurrentRound = 1 };
        db.Auctions.Add(auction);
        db.SaveChanges();

        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = 10000, IsActive = true };
        db.Teams.Add(team);
        db.SaveChanges();

        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();

        var player = new Player { AuctionId = auction.Id, Name = "P1", BasePrice = 500, Status = PlayerStatus.Bidding };
        db.Players.Add(player);
        db.SaveChanges();

        var audit = new AuditLogService(db);
        var svc = new SaleService(db, ledger, audit);
        return (db, svc, auction, player, team);
    }

    [Fact]
    public async Task SellPlayer_MarksSold_WritesSaleAndLedgerEntry()
    {
        var (db, svc, auction, player, team) = Setup();

        var result = await svc.SellPlayerAsync(auction.Id, player.Id, team.Id, 700, confirmedByUserId: 1);

        Assert.True(result.Success);
        Assert.Equal(PlayerStatus.Sold, result.Player!.Status);
        Assert.Equal(700, result.Player.SalePrice);

        var ledger = new TeamLedgerService(db);
        Assert.Equal(9300, ledger.GetAvailableBalance(team.Id));
        Assert.Single(db.Sales);
    }

    [Fact]
    public async Task SellPlayer_DoubleSell_IsRejected()
    {
        var (db, svc, auction, player, team) = Setup();
        var first = await svc.SellPlayerAsync(auction.Id, player.Id, team.Id, 700, 1);
        Assert.True(first.Success);

        var second = await svc.SellPlayerAsync(auction.Id, player.Id, team.Id, 700, 1);
        Assert.False(second.Success);
        Assert.Contains("already been processed", second.Error);

        // ledger should only have opening balance + one purchase entry
        Assert.Equal(2, db.TeamLedgerEntries.Count());
    }

    [Fact]
    public async Task SellPlayer_ExceedingBalance_IsRejected()
    {
        var (db, svc, auction, player, team) = Setup();
        var result = await svc.SellPlayerAsync(auction.Id, player.Id, team.Id, 999999, 1);
        Assert.False(result.Success);
        Assert.Equal(PlayerStatus.Bidding, (await db.Players.FindAsync(player.Id))!.Status);
    }

    [Fact]
    public async Task SellPlayer_OffIncrementAmount_IsRejected()
    {
        var (db, svc, auction, player, team) = Setup();
        var result = await svc.SellPlayerAsync(auction.Id, player.Id, team.Id, 725, 1);

        Assert.False(result.Success);
        Assert.Contains("whole multiple", result.Error);
        Assert.Empty(db.Sales);
        Assert.Equal(PlayerStatus.Bidding, (await db.Players.FindAsync(player.Id))!.Status);
    }

    [Fact]
    public async Task MarkUnsold_TransitionsPlayer_AndRejectsReprocessing()
    {
        var (db, svc, auction, player, team) = Setup();

        var result = await svc.MarkUnsoldAsync(auction.Id, player.Id, 1);
        Assert.True(result.Success);
        Assert.Equal(PlayerStatus.Unsold, result.Player!.Status);

        var again = await svc.MarkUnsoldAsync(auction.Id, player.Id, 1);
        Assert.False(again.Success);
    }
}
