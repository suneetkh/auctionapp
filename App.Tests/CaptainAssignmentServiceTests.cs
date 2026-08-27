using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class CaptainAssignmentServiceTests
{
    private static (App.Api.Data.AppDbContext Db, CaptainAssignmentService Service, Auction Auction, Team Team, Player P1, Player P2) Setup()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "A", Status = AuctionStatus.Draft, RosterMaxSize = 10 };
        db.Auctions.Add(auction);
        db.SaveChanges();
        var team = new Team { AuctionId = auction.Id, Name = "Team A", OpeningBalance = 1000 };
        db.Teams.Add(team);
        db.SaveChanges();
        var ledger = new TeamLedgerService(db);
        ledger.EnsureOpeningBalance(team);
        var p1 = new Player { AuctionId = auction.Id, Name = "P1", Status = PlayerStatus.Available };
        var p2 = new Player { AuctionId = auction.Id, Name = "P2", Status = PlayerStatus.Available };
        db.Players.AddRange(p1, p2);
        db.SaveChanges();
        return (db, new CaptainAssignmentService(db, ledger, new AuditLogService(db)), auction, team, p1, p2);
    }

    [Fact]
    public async Task FreeCaptain_JoinsRosterWithoutReducingPurse()
    {
        var (db, service, _, team, p1, _) = Setup();
        var before = new TeamLedgerService(db).GetAvailableBalance(team.Id);
        var result = await service.SetAsync(p1.Id, true, team.Id, 0, 1);
        Assert.True(result.Success);
        Assert.True(p1.IsCaptain);
        Assert.Equal(team.Id, p1.TeamId);
        Assert.Equal(PlayerStatus.Withdrawn, p1.Status);
        Assert.Equal(before, new TeamLedgerService(db).GetAvailableBalance(team.Id));
    }

    [Fact]
    public async Task RejectsSecondCaptainForSameTeam()
    {
        var (_, service, _, team, p1, p2) = Setup();
        Assert.True((await service.SetAsync(p1.Id, true, team.Id, 0, 1)).Success);
        var second = await service.SetAsync(p2.Id, true, team.Id, 0, 1);
        Assert.False(second.Success);
        Assert.Contains("already has a captain", second.Error);
    }

    [Fact]
    public async Task PaidCaptain_DebitsAndRemovalRefundsPurse()
    {
        var (db, service, _, team, p1, _) = Setup();
        Assert.True((await service.SetAsync(p1.Id, true, team.Id, 250, 1)).Success);
        Assert.Equal(750, new TeamLedgerService(db).GetAvailableBalance(team.Id));
        Assert.True((await service.SetAsync(p1.Id, false, null, null, 1)).Success);
        Assert.Equal(1000, new TeamLedgerService(db).GetAvailableBalance(team.Id));
        Assert.Equal(PlayerStatus.Available, p1.Status);
    }
}
