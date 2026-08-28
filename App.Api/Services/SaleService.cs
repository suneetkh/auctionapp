using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class SaleResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Sale? Sale { get; set; }
    public Player? Player { get; set; }
    public Team? Team { get; set; }
}

public class SaleService
{
    private readonly AppDbContext _db;
    private readonly TeamLedgerService _ledger;
    private readonly AuditLogService _audit;

    public SaleService(AppDbContext db, TeamLedgerService ledger, AuditLogService audit)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
    }

    public async Task<SaleResult> SellPlayerAsync(int auctionId, int playerId, int teamId, decimal amount, int confirmedByUserId)
    {
        using var tx = await _db.Database.BeginTransactionAsync();

        var auction = await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return new SaleResult { Success = false, Error = "Auction not found" };
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return new SaleResult { Success = false, Error = "Auction is not in a live/biddable state" };

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.AuctionId == auctionId);
        if (player == null) return new SaleResult { Success = false, Error = "Player not found" };
        if (player.IsCaptain) return new SaleResult { Success = false, Error = "Captains are not part of the auction pool" };

        // idempotency: reject if player already processed
        if (player.Status == PlayerStatus.Sold || player.Status == PlayerStatus.Unsold || player.Status == PlayerStatus.FinalUnsold)
            return new SaleResult { Success = false, Error = "Player has already been processed (double-sell prevented)" };

        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.AuctionId == auctionId);
        if (team == null) return new SaleResult { Success = false, Error = "Team not found" };

        var availableBalance = _ledger.GetAvailableBalance(teamId);
        if (amount > availableBalance)
            return new SaleResult { Success = false, Error = "Team has insufficient balance for this sale amount" };

        // Selling at the leader's exact bid is valid. A direct/no-bid sale starts at the
        // player's floor. Any amount above either baseline must use whole bid increments.
        var floor = Math.Max(player.MinimumBidOverride ?? player.BasePrice, auction.MinimumBidAmount);
        var highestBid = _db.Bids.Where(b => b.PlayerId == playerId && b.IsValid)
            .ToList().OrderByDescending(b => b.Amount).FirstOrDefault();
        var baseline = highestBid?.Amount ?? BidIncrementRule.AlignUp(floor, auction.BidIncrementAmount);
        var incrementError = BidIncrementRule.Validate(amount, baseline, auction.BidIncrementAmount,
            requireIncrease: false, label: "Sale amount");
        if (incrementError != null)
            return new SaleResult { Success = false, Error = incrementError };

        // Roster size / min-remaining-purse must be enforced here too, not just on /bids -
        // a sale can be confirmed directly (drag-and-drop, "sell to leader", manual override)
        // without ever going through bid validation.
        var currentRosterSize = await _db.Players.CountAsync(p => p.TeamId == teamId && (p.Status == PlayerStatus.Sold || p.IsCaptain));
        if (auction.RosterMaxSize.HasValue && currentRosterSize >= auction.RosterMaxSize.Value)
            return new SaleResult { Success = false, Error = $"Team has reached its maximum roster size ({auction.RosterMaxSize.Value})" };

        var maximumBid = TeamBidCapacityRule.CalculateMaximumBid(auction, currentRosterSize, availableBalance);
        if (amount > maximumBid)
            return new SaleResult { Success = false, Error = $"Maximum sale amount for {team.Name} is {maximumBid:0.##}. Enough purse must remain to complete the minimum roster." };

        var beforePlayer = new { player.Status, player.TeamId, player.SalePrice };

        player.Status = PlayerStatus.Sold;
        player.TeamId = teamId;
        player.SalePrice = amount;
        player.SoldRound = auction.CurrentRound;

        var sale = new Sale
        {
            AuctionId = auctionId,
            PlayerId = playerId,
            TeamId = teamId,
            FinalAmount = amount,
            RoundNumber = auction.CurrentRound,
            ConfirmedByUserId = confirmedByUserId,
            SaleStatus = SaleStatus.Confirmed,
            CreatedAt = DateTime.UtcNow
        };
        _db.Sales.Add(sale);
        await _db.SaveChangesAsync(); // to get sale.Id

        _ledger.AddEntry(teamId, LedgerTransactionType.Purchase, -amount, $"Purchase of player {player.Name}",
            relatedPlayerId: playerId, relatedSaleId: sale.Id, createdByUserId: confirmedByUserId);

        _audit.Write("Player", playerId, "Sold", beforePlayer,
            new { player.Status, player.TeamId, player.SalePrice }, null, confirmedByUserId);

        _db.AuctionEvents.Add(new AuctionEvent
        {
            AuctionId = auctionId,
            EventType = "player_sold",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { playerId, teamId, amount })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new SaleResult { Success = true, Sale = sale, Player = player, Team = team };
    }

    public async Task<SaleResult> MarkUnsoldAsync(int auctionId, int playerId, int performedByUserId)
    {
        using var tx = await _db.Database.BeginTransactionAsync();

        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return new SaleResult { Success = false, Error = "Auction not found" };
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return new SaleResult { Success = false, Error = "Auction is not in a live/biddable state" };

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.AuctionId == auctionId);
        if (player == null) return new SaleResult { Success = false, Error = "Player not found" };
        if (player.IsCaptain) return new SaleResult { Success = false, Error = "Captains are not part of the auction pool" };

        if (player.Status == PlayerStatus.Sold || player.Status == PlayerStatus.Unsold || player.Status == PlayerStatus.FinalUnsold)
            return new SaleResult { Success = false, Error = "Player has already been processed" };

        var before = new { player.Status };
        player.Status = PlayerStatus.Unsold;

        _audit.Write("Player", playerId, "Unsold", before, new { player.Status }, null, performedByUserId);

        _db.AuctionEvents.Add(new AuctionEvent
        {
            AuctionId = auctionId,
            EventType = "player_unsold",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { playerId })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new SaleResult { Success = true, Player = player };
    }
}
