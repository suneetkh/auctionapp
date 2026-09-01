using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class CorrectionService
{
    private readonly AppDbContext _db;
    private readonly TeamLedgerService _ledger;
    private readonly AuditLogService _audit;

    public CorrectionService(AppDbContext db, TeamLedgerService ledger, AuditLogService audit)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
    }

    public async Task<(bool Success, string? Error)> ReverseSaleAsync(int saleId, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");

        using var tx = await _db.Database.BeginTransactionAsync();

        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == saleId);
        if (sale == null) return (false, "Sale not found");
        if (sale.SaleStatus == SaleStatus.Reversed) return (false, "Sale already reversed");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == sale.PlayerId);
        if (player == null) return (false, "Player not found");

        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == sale.AuctionId);

        var beforePlayer = new { player.Status, player.TeamId, player.SalePrice };
        var beforeSale = new { sale.SaleStatus };

        sale.SaleStatus = SaleStatus.Reversed;
        sale.ReversedAt = DateTime.UtcNow;
        sale.ReversalReason = reason;

        // The wheel only ever draws from the Available pool while the auction is still
        // actually Live. Every other status the auction can be in by the time a reversal
        // happens - MainRoundComplete (even briefly, which happens automatically with no
        // audit trail the moment the last player is processed), UnsoldPoolOpen, Paused,
        // even Completed - has moved past the main round, so Available would strand the
        // player in a pool nothing is drawing from anymore. ReauctionAvailable is the only
        // status that's ever reachable again from any of those states (via spin/select once
        // the pool is open, or via the unsold-pool-open promotion otherwise).
        player.Status = auction?.Status == AuctionStatus.Live
            ? PlayerStatus.Available : PlayerStatus.ReauctionAvailable;
        player.TeamId = null;
        player.SalePrice = null;
        player.SoldRound = null;

        _ledger.AddEntry(sale.TeamId, LedgerTransactionType.Reversal, sale.FinalAmount, reason,
            relatedPlayerId: player.Id, relatedSaleId: sale.Id, createdByUserId: performedByUserId);

        _audit.Write("Sale", sale.Id, "Reversed", beforeSale, new { sale.SaleStatus }, reason, performedByUserId);
        _audit.Write("Player", player.Id, "Reversed", beforePlayer,
            new { player.Status, player.TeamId, player.SalePrice }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AdjustBalanceAsync(int teamId, decimal amount, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");
        using var tx = await _db.Database.BeginTransactionAsync();

        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return (false, "Team not found");

        _ledger.AddEntry(teamId, LedgerTransactionType.ManualAdjustment, amount, reason, createdByUserId: performedByUserId);
        _audit.Write("Team", teamId, "ManualBalanceAdjustment", null, new { amount }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }

    // Rules normally lock once an auction goes Live (see AuctionLifecycleService.CanEditRules).
    // This is the one sanctioned way to change the min-remaining-purse reserve after that point -
    // e.g. to correct a misconfigured value (such as a reserve far exceeding any team's budget,
    // which makes every sale fail) without unlocking the whole rules form. Always sets the field
    // to exactly the given value (including null, to disable the rule) - there is no "leave
    // unchanged" case here, unlike the general auction PATCH endpoint.
    public async Task<(bool Success, string? Error)> AdjustMinRemainingPurseRuleAsync(int auctionId, decimal? newValue, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");
        using var tx = await _db.Database.BeginTransactionAsync();

        var rules = await _db.AuctionRules.FirstOrDefaultAsync(r => r.AuctionId == auctionId);
        if (rules == null) return (false, "Auction rules not found");

        var before = new { rules.MinRemainingPurseRule };
        rules.MinRemainingPurseRule = newValue;
        _audit.Write("Auction", auctionId, "MinRemainingPurseRuleAdjusted", before, new { rules.MinRemainingPurseRule }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReassignPlayerAsync(int playerId, int newTeamId, decimal newAmount, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");
        if (newAmount <= 0) return (false, "Assignment price must be greater than zero");
        using var tx = await _db.Database.BeginTransactionAsync();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) return (false, "Player not found");
        if (player.IsCaptain) return (false, "Captain assignments must be managed through the captain controls");
        var auction = await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == player.AuctionId);
        if (auction == null) return (false, "Auction not found");
        var newTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == newTeamId && t.AuctionId == player.AuctionId);
        if (newTeam == null) return (false, "New team not found");
        if (!newTeam.IsActive) return (false, "The selected team is inactive");

        var floor = BidIncrementRule.AlignUp(
            Math.Max(player.MinimumBidOverride ?? player.BasePrice, auction.MinimumBidAmount),
            auction.BidIncrementAmount);
        var incrementError = BidIncrementRule.Validate(newAmount, floor, auction.BidIncrementAmount,
            requireIncrease: false, label: "Assignment price");
        if (incrementError != null) return (false, incrementError);

        var oldSale = await _db.Sales
            .Where(s => s.PlayerId == playerId && s.SaleStatus == SaleStatus.Confirmed)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        var oldTeamId = oldSale?.TeamId ?? player.TeamId;
        var oldAmount = oldSale?.FinalAmount ?? player.SalePrice ?? 0;
        var rosterExcludingPlayer = await _db.Players.CountAsync(p => p.TeamId == newTeamId && p.Id != playerId &&
            (p.Status == PlayerStatus.Sold || p.IsCaptain));
        if (auction.RosterMaxSize.HasValue && rosterExcludingPlayer >= auction.RosterMaxSize.Value)
            return (false, $"{newTeam.Name} has reached its maximum roster size ({auction.RosterMaxSize.Value})");

        var prospectiveBalance = _ledger.GetAvailableBalance(newTeamId) +
            (oldTeamId == newTeamId ? oldAmount : 0);
        var maximumBid = TeamBidCapacityRule.CalculateMaximumBid(auction, rosterExcludingPlayer, prospectiveBalance);
        if (newAmount > maximumBid)
            return (false, $"Maximum assignment price for {newTeam.Name} is {maximumBid:0.##}. Enough purse must remain to complete the minimum roster.");

        var before = new { player.Status, player.TeamId, player.SalePrice };

        // Refund any existing assignment before applying the corrected one. This also repairs
        // legacy/glitched player rows that have a team and price but no matching Sale record.
        if (oldTeamId.HasValue && oldAmount > 0)
        {
            if (oldSale != null)
            {
                oldSale.SaleStatus = SaleStatus.Reversed;
                oldSale.ReversedAt = DateTime.UtcNow;
                oldSale.ReversalReason = reason;
            }
            _ledger.AddEntry(oldTeamId.Value, LedgerTransactionType.Reversal, oldAmount, reason,
                relatedPlayerId: playerId, relatedSaleId: oldSale?.Id, createdByUserId: performedByUserId);
            await _db.SaveChangesAsync();
        }

        var newSale = new Sale
        {
            AuctionId = player.AuctionId,
            PlayerId = playerId,
            TeamId = newTeamId,
            FinalAmount = newAmount,
            RoundNumber = Math.Max(1, auction.CurrentRound),
            ConfirmedByUserId = performedByUserId,
            SaleStatus = SaleStatus.Confirmed,
        };
        _db.Sales.Add(newSale);
        await _db.SaveChangesAsync();

        _ledger.AddEntry(newTeamId, LedgerTransactionType.Purchase, -newAmount, reason,
            relatedPlayerId: playerId, relatedSaleId: newSale.Id, createdByUserId: performedByUserId);

        player.Status = PlayerStatus.Sold;
        player.TeamId = newTeamId;
        player.SalePrice = newAmount;
        player.SoldRound = Math.Max(1, auction.CurrentRound);

        _audit.Write("Player", playerId, "Reassigned", before,
            new { player.Status, player.TeamId, player.SalePrice }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UnassignPlayerAsync(int playerId, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");
        using var tx = await _db.Database.BeginTransactionAsync();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) return (false, "Player not found");
        if (player.IsCaptain) return (false, "Captain assignments must be managed through the captain controls");
        if (!player.TeamId.HasValue) return (false, "Player is not assigned to a team");
        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == player.AuctionId);
        if (auction == null) return (false, "Auction not found");

        var sale = await _db.Sales
            .Where(s => s.PlayerId == playerId && s.SaleStatus == SaleStatus.Confirmed)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        var teamId = sale?.TeamId ?? player.TeamId.Value;
        var refund = sale?.FinalAmount ?? player.SalePrice ?? 0;
        var before = new { player.Status, player.TeamId, player.SalePrice, player.SoldRound };

        if (sale != null)
        {
            sale.SaleStatus = SaleStatus.Reversed;
            sale.ReversedAt = DateTime.UtcNow;
            sale.ReversalReason = reason;
        }
        if (refund > 0)
            _ledger.AddEntry(teamId, LedgerTransactionType.Reversal, refund, reason,
                relatedPlayerId: playerId, relatedSaleId: sale?.Id, createdByUserId: performedByUserId);

        player.Status = auction.Status is AuctionStatus.Draft or AuctionStatus.Ready or AuctionStatus.Live or AuctionStatus.Paused
            ? PlayerStatus.Available
            : PlayerStatus.ReauctionAvailable;
        player.TeamId = null;
        player.SalePrice = null;
        player.SoldRound = null;

        _audit.Write("Player", playerId, "Unassigned", before,
            new { player.Status, player.TeamId, player.SalePrice, player.SoldRound }, reason, performedByUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }
}
