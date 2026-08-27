using App.Api.Models;
using App.Api.Services;
using Xunit;

namespace App.Tests;

public class AuctionDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_RemovesAuctionAndAllRelatedData()
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "Delete me", Status = AuctionStatus.Archived };
        var user = new User { Email = "delete-test@example.com", PasswordHash = "x", DisplayName = "Test" };
        db.AddRange(auction, user);
        db.SaveChanges();
        db.AuctionUserAccess.Add(new AuctionUserAccess { AuctionId = auction.Id, UserId = user.Id });
        db.AuctionPlanningStates.Add(new AuctionPlanningState { AuctionId = auction.Id, DrawStateJson = "{}", FixtureStateJson = "{}" });

        var rules = new AuctionRules { AuctionId = auction.Id };
        var team = new Team { AuctionId = auction.Id, Name = "T", OpeningBalance = 1000 };
        var player = new Player { AuctionId = auction.Id, Name = "P", BasePrice = 100 };
        db.AddRange(rules, team, player);
        db.SaveChanges();

        var bid = new Bid { AuctionId = auction.Id, PlayerId = player.Id, TeamId = team.Id, Amount = 100 };
        var sale = new Sale { AuctionId = auction.Id, PlayerId = player.Id, TeamId = team.Id, FinalAmount = 100 };
        db.AddRange(bid, sale);
        db.SaveChanges();

        db.TeamLedgerEntries.Add(new TeamLedgerEntry { TeamId = team.Id, Amount = 1000, BalanceAfter = 1000 });
        db.AuctionEvents.Add(new AuctionEvent { AuctionId = auction.Id, EventType = "test" });
        db.Sponsors.Add(new Sponsor { AuctionId = auction.Id, LogoDataUri = "data:image/png;base64,AA==" });
        db.AuditLogs.AddRange(
            new AuditLog { EntityType = "Auction", EntityId = auction.Id, Action = "Archived" },
            new AuditLog { EntityType = "Team", EntityId = team.Id, Action = "Updated" },
            new AuditLog { EntityType = "Player", EntityId = player.Id, Action = "Sold" },
            new AuditLog { EntityType = "Bid", EntityId = bid.Id, Action = "Undone" },
            new AuditLog { EntityType = "Sale", EntityId = sale.Id, Action = "Reversed" });
        db.SaveChanges();

        var result = await new AuctionDeletionService(db).DeleteAsync(auction.Id);

        Assert.True(result.Success);
        Assert.Empty(db.Auctions);
        Assert.Empty(db.AuctionUserAccess);
        Assert.Empty(db.AuctionPlanningStates);
        Assert.Empty(db.AuctionRules);
        Assert.Empty(db.Teams);
        Assert.Empty(db.Players);
        Assert.Empty(db.Bids);
        Assert.Empty(db.Sales);
        Assert.Empty(db.TeamLedgerEntries);
        Assert.Empty(db.AuctionEvents);
        Assert.Empty(db.Sponsors);
        Assert.Empty(db.AuditLogs);
    }

    [Theory]
    [InlineData(AuctionStatus.Live)]
    [InlineData(AuctionStatus.Paused)]
    [InlineData(AuctionStatus.UnsoldPoolOpen)]
    public async Task DeleteAsync_RejectsActiveAuction(AuctionStatus status)
    {
        var db = TestDbFactory.Create();
        var auction = new Auction { Name = "Active", Status = status };
        db.Auctions.Add(auction);
        db.SaveChanges();

        var result = await new AuctionDeletionService(db).DeleteAsync(auction.Id);

        Assert.False(result.Success);
        Assert.Contains("live auction", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.Auctions);
    }
}
