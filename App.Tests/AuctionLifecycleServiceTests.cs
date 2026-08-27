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
}
