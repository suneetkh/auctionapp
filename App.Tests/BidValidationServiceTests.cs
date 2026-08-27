using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class BidValidationServiceTests
{
    private static (App.Api.Data.AppDbContext db, BidValidationService svc, Auction auction, Player player, Team team) Setup(
        decimal openingBalance = 10000, decimal basePrice = 500, decimal minBid = 100, decimal increment = 50,
        AuctionStatus status = AuctionStatus.Live, PlayerStatus playerStatus = PlayerStatus.Selected)
    {
        var db = TestDbFactory.Create();
        var auction = new Auction
        {
            Name = "A", MinimumBidAmount = minBid, BidIncrementAmount = increment,
            Status = status, RosterMinSize = 1, RosterMaxSize = 5
        };
        db.Auctions.Add(auction);
        db.SaveChanges();

        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = openingBalance, IsActive = true };
        db.Teams.Add(team);
        db.SaveChanges();

        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();

        var player = new Player { AuctionId = auction.Id, Name = "P1", BasePrice = basePrice, Status = playerStatus };
        db.Players.Add(player);
        db.SaveChanges();

        var svc = new BidValidationService(db, ledger);
        return (db, svc, auction, player, team);
    }

    [Fact]
    public void RejectsBid_WhenAuctionNotLive()
    {
        var (db, svc, auction, player, team) = Setup(status: AuctionStatus.Paused);
        var result = svc.Validate(auction, player, team, 500);
        Assert.False(result.IsValid);
        Assert.Contains("not in a biddable state", result.Reason);
    }

    [Fact]
    public void RejectsFirstBid_BelowMinimum()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 1000);
        var result = svc.Validate(auction, player, team, 500);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AcceptsFirstBid_AtOrAboveMax_Of_BaseAndMinimum()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 100);
        var result = svc.Validate(auction, player, team, 500);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsFirstBid_ThatIsNotAWholeIncrementAboveFloor()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 100, increment: 50);
        var result = svc.Validate(auction, player, team, 525);
        Assert.False(result.IsValid);
        Assert.Contains("whole multiple", result.Reason);
    }

    [Fact]
    public void RejectsSubsequentBid_BelowIncrement()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 100, increment: 50);
        db.Bids.Add(new Bid { AuctionId = auction.Id, PlayerId = player.Id, TeamId = team.Id, Amount = 500, IsValid = true });
        db.SaveChanges();

        var result = svc.Validate(auction, player, team, 520);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AcceptsSubsequentBid_MeetingIncrement_FromDifferentTeam()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 100, increment: 50);
        var otherTeam = new Team { AuctionId = auction.Id, Name = "T2", OpeningBalance = 10000, IsActive = true };
        db.Teams.Add(otherTeam);
        db.SaveChanges();
        new TeamLedgerService(db).EnsureOpeningBalance(otherTeam);
        db.SaveChanges();

        db.Bids.Add(new Bid { AuctionId = auction.Id, PlayerId = player.Id, TeamId = otherTeam.Id, Amount = 500, IsValid = true });
        db.SaveChanges();

        var result = svc.Validate(auction, player, team, 550);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsSubsequentBid_ThatSkipsToAnOffStepAmount()
    {
        var (db, svc, auction, player, team) = Setup(basePrice: 500, minBid: 100, increment: 50);
        var otherTeam = new Team { AuctionId = auction.Id, Name = "T2", OpeningBalance = 10000, IsActive = true };
        db.Teams.Add(otherTeam);
        db.SaveChanges();

        db.Bids.Add(new Bid { AuctionId = auction.Id, PlayerId = player.Id, TeamId = otherTeam.Id, Amount = 500, IsValid = true });
        db.SaveChanges();

        var result = svc.Validate(auction, player, team, 575);
        Assert.False(result.IsValid);
        Assert.Contains("whole multiple", result.Reason);
    }

    [Fact]
    public void RejectsBid_WhenExceedsAvailableBalance()
    {
        var (db, svc, auction, player, team) = Setup(openingBalance: 400, basePrice: 500, minBid: 100);
        var result = svc.Validate(auction, player, team, 500);
        Assert.False(result.IsValid);
        Assert.Contains("exceeds", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsBid_WhenPlayerNotSelectedOrBidding()
    {
        var (db, svc, auction, player, team) = Setup(playerStatus: PlayerStatus.Available);
        var result = svc.Validate(auction, player, team, 500);
        Assert.False(result.IsValid);
    }
}
