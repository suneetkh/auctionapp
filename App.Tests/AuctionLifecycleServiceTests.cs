using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class AuctionLifecycleServiceTests
{
    [Theory]
    [InlineData(AuctionStatus.Draft, AuctionStatus.Ready, true)]
    [InlineData(AuctionStatus.Draft, AuctionStatus.Live, true)]
    [InlineData(AuctionStatus.Ready, AuctionStatus.Live, true)]
    [InlineData(AuctionStatus.Live, AuctionStatus.Paused, true)]
    [InlineData(AuctionStatus.Paused, AuctionStatus.Live, true)]
    [InlineData(AuctionStatus.Completed, AuctionStatus.Live, false)]
    [InlineData(AuctionStatus.MainRoundComplete, AuctionStatus.Completed, true)]
    public void CanTransition_MatchesExpected(AuctionStatus from, AuctionStatus to, bool expected)
    {
        Assert.Equal(expected, AuctionLifecycleService.CanTransition(from, to));
    }

    [Fact]
    public async Task ValidateStartAsync_FailsWithFewerThanTwoTeams()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.Draft };
        db.Auctions.Add(auction);
        db.SaveChanges();
        db.Teams.Add(new Team { AuctionId = auction.Id, Name = "T1", IsActive = true });
        db.Players.Add(new Player { AuctionId = auction.Id, Name = "P1", BasePrice = 100 });
        db.SaveChanges();

        var svc = new AuctionLifecycleService(db, new AuditLogService(db), new TeamLedgerService(db));
        var (ok, error) = await svc.ValidateStartAsync(auction.Id);
        Assert.False(ok);
        Assert.Contains("2 active teams", error);
    }

    [Fact]
    public async Task ValidateStartAsync_FailsWithNoPlayers()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.Draft };
        db.Auctions.Add(auction);
        db.SaveChanges();
        db.Teams.Add(new Team { AuctionId = auction.Id, Name = "T1", IsActive = true });
        db.Teams.Add(new Team { AuctionId = auction.Id, Name = "T2", IsActive = true });
        db.SaveChanges();

        var svc = new AuctionLifecycleService(db, new AuditLogService(db), new TeamLedgerService(db));
        var (ok, error) = await svc.ValidateStartAsync(auction.Id);
        Assert.False(ok);
        Assert.Contains("1 player", error);
    }

    [Fact]
    public async Task ValidateStartAsync_SucceedsWithEnoughTeamsAndPlayers()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.Draft };
        db.Auctions.Add(auction);
        db.SaveChanges();
        db.Teams.Add(new Team { AuctionId = auction.Id, Name = "T1", IsActive = true });
        db.Teams.Add(new Team { AuctionId = auction.Id, Name = "T2", IsActive = true });
        db.Players.Add(new Player { AuctionId = auction.Id, Name = "P1", BasePrice = 100 });
        db.SaveChanges();

        var svc = new AuctionLifecycleService(db, new AuditLogService(db), new TeamLedgerService(db));
        var (ok, error) = await svc.ValidateStartAsync(auction.Id);
        Assert.True(ok);
    }

    [Fact]
    public async Task ValidateCompleteAsync_RequiresForceConfirm_WhenUnresolvedPlayersExist()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.MainRoundComplete };
        db.Auctions.Add(auction);
        db.SaveChanges();
        db.Players.Add(new Player { AuctionId = auction.Id, Name = "P1", BasePrice = 100, Status = PlayerStatus.Unsold });
        db.SaveChanges();

        var svc = new AuctionLifecycleService(db, new AuditLogService(db), new TeamLedgerService(db));
        var (ok, error) = await svc.ValidateCompleteAsync(auction.Id, forceConfirm: false);
        Assert.False(ok);

        var (ok2, _) = await svc.ValidateCompleteAsync(auction.Id, forceConfirm: true);
        Assert.True(ok2);
    }

    [Fact]
    public async Task Reset_PreservesCaptainAndRecalculatesMaximumBidFromRestoredPurse()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction
        {
            Name = "A",
            Status = AuctionStatus.Live,
            CurrentRound = 2,
            MinimumBidAmount = 100,
            RosterMinSize = 3,
            SelectionRevealPending = true,
            Rules = new AuctionRules()
        };
        db.Auctions.Add(auction);
        db.SaveChanges();

        var team = new Team { AuctionId = auction.Id, Name = "T1", OpeningBalance = 1000 };
        db.Teams.Add(team);
        db.SaveChanges();
        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        db.SaveChanges();

        var captain = new Player
        {
            AuctionId = auction.Id, Name = "Captain", IsCaptain = true, TeamId = team.Id,
            CaptainCost = 200, Status = PlayerStatus.Withdrawn
        };
        var soldPlayer = new Player
        {
            AuctionId = auction.Id, Name = "Sold player", TeamId = team.Id,
            SalePrice = 300, SoldRound = 1, Status = PlayerStatus.Sold
        };
        db.Players.AddRange(captain, soldPlayer);
        db.SaveChanges();
        ledger.AddEntry(team.Id, LedgerTransactionType.CaptainAssignment, -200, "Captain", captain.Id);
        ledger.AddEntry(team.Id, LedgerTransactionType.Purchase, -300, "Purchase", soldPlayer.Id);
        db.SaveChanges();

        var service = new AuctionLifecycleService(db, new AuditLogService(db), ledger);
        var result = await service.ResetAuctionAsync(auction.Id, 1);

        Assert.True(result.Success);
        Assert.True(captain.IsCaptain);
        Assert.Equal(team.Id, captain.TeamId);
        Assert.Equal(PlayerStatus.Withdrawn, captain.Status);
        Assert.Equal(200, captain.CaptainCost);
        Assert.Equal(PlayerStatus.Available, soldPlayer.Status);
        Assert.Null(soldPlayer.TeamId);
        Assert.Equal(800, ledger.GetAvailableBalance(team.Id));
        Assert.Equal(700, TeamBidCapacityRule.CalculateMaximumBid(auction, 1, ledger.GetAvailableBalance(team.Id)));
        Assert.False(auction.SelectionRevealPending);
    }
}
