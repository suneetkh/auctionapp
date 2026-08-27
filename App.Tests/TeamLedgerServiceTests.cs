using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class TeamLedgerServiceTests
{
    private static Auction SeedAuction(App.Api.Data.AppDbContext db)
    {
        var auction = new Auction { Name = "A", SportType = "Cricket", TournamentName = "T", OwnerUserId = 1 };
        db.Auctions.Add(auction);
        db.SaveChanges();
        return auction;
    }

    [Fact]
    public void GetAvailableBalance_NoEntries_ReturnsOpeningBalance()
    {
        using var db = TestDbFactory.Create();
        var auction = SeedAuction(db);
        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = 1000 };
        db.Teams.Add(team);
        db.SaveChanges();

        var ledger = new TeamLedgerService(db);
        Assert.Equal(1000, ledger.GetAvailableBalance(team.Id));
    }

    [Fact]
    public void EnsureOpeningBalance_CreatesEntryOnlyOnce()
    {
        using var db = TestDbFactory.Create();
        var auction = SeedAuction(db);
        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = 5000 };
        db.Teams.Add(team);
        db.SaveChanges();

        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();

        Assert.Single(db.TeamLedgerEntries);
        Assert.Equal(5000, ledger.GetAvailableBalance(team.Id));
    }

    [Fact]
    public void AddEntry_ComputesBalanceBeforeAndAfterFromLedger()
    {
        using var db = TestDbFactory.Create();
        var auction = SeedAuction(db);
        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = 1000 };
        db.Teams.Add(team);
        db.SaveChanges();

        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();

        var entry = ledger.AddEntry(team.Id, LedgerTransactionType.Purchase, -300, "bought player");
        db.SaveChanges();

        Assert.Equal(1000, entry.BalanceBefore);
        Assert.Equal(700, entry.BalanceAfter);
        Assert.Equal(700, ledger.GetAvailableBalance(team.Id));
    }
}
