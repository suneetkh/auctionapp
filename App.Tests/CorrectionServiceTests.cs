using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class CorrectionServiceTests
{
    [Fact]
    public async Task AssignmentCorrection_AssignsReassignsAndUnassignsWithPurseUpdates_InAnyState()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction
        {
            Name = "A", Status = AuctionStatus.Draft, CurrentRound = 1,
            MinimumBidAmount = 100, BidIncrementAmount = 100,
            RosterMinSize = 1, RosterMaxSize = 5,
            Rules = new AuctionRules()
        };
        db.Auctions.Add(auction);
        db.SaveChanges();

        var teamOne = new Team { AuctionId = auction.Id, Name = "One", OpeningBalance = 10000, IsActive = true };
        var teamTwo = new Team { AuctionId = auction.Id, Name = "Two", OpeningBalance = 10000, IsActive = true };
        db.Teams.AddRange(teamOne, teamTwo);
        db.SaveChanges();
        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(teamOne);
        ledger.EnsureOpeningBalance(teamTwo);
        db.SaveChanges();

        var player = new Player { AuctionId = auction.Id, Name = "Player", BasePrice = 100, Status = PlayerStatus.Available };
        db.Players.Add(player);
        db.SaveChanges();
        var service = new CorrectionService(db, ledger, new AuditLogService(db));

        Assert.True((await service.ReassignPlayerAsync(player.Id, teamOne.Id, 500, "Recovery", 1)).Success);
        Assert.Equal(PlayerStatus.Sold, player.Status);
        Assert.Equal(teamOne.Id, player.TeamId);
        Assert.Equal(9500, ledger.GetAvailableBalance(teamOne.Id));

        auction.Status = AuctionStatus.Completed;
        db.SaveChanges();
        Assert.True((await service.ReassignPlayerAsync(player.Id, teamTwo.Id, 700, "Correct team", 1)).Success);
        Assert.Equal(10000, ledger.GetAvailableBalance(teamOne.Id));
        Assert.Equal(9300, ledger.GetAvailableBalance(teamTwo.Id));
        Assert.Equal(teamTwo.Id, player.TeamId);

        Assert.True((await service.UnassignPlayerAsync(player.Id, "Remove incorrect assignment", 1)).Success);
        Assert.Equal(10000, ledger.GetAvailableBalance(teamTwo.Id));
        Assert.Null(player.TeamId);
        Assert.Null(player.SalePrice);
        Assert.Equal(PlayerStatus.ReauctionAvailable, player.Status);
    }

    [Fact]
    public async Task AssignmentCorrection_RejectsTeamFromAnotherAuction()
    {
        var db = TestDbFactory.Create();
        var first = new Auction { Name = "First", MinimumBidAmount = 100, BidIncrementAmount = 100, Rules = new AuctionRules() };
        var second = new Auction { Name = "Second", MinimumBidAmount = 100, BidIncrementAmount = 100, Rules = new AuctionRules() };
        db.Auctions.AddRange(first, second);
        db.SaveChanges();
        var player = new Player { AuctionId = first.Id, Name = "Player", BasePrice = 100 };
        var wrongTeam = new Team { AuctionId = second.Id, Name = "Wrong", OpeningBalance = 1000, IsActive = true };
        db.Players.Add(player);
        db.Teams.Add(wrongTeam);
        db.SaveChanges();

        var result = await new CorrectionService(db, new TeamLedgerService(db), new AuditLogService(db))
            .ReassignPlayerAsync(player.Id, wrongTeam.Id, 100, "Test", 1);

        Assert.False(result.Success);
        Assert.Equal("New team not found", result.Error);
    }
}
