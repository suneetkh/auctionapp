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
        using var tx = await _db.Database.BeginTransactionAsync();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) return (false, "Player not found");
        var newTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == newTeamId);
        if (newTeam == null) return (false, "New team not found");

        var before = new { player.Status, player.TeamId, player.SalePrice };

        // reverse old sale ledger entry if previously sold
        if (player.TeamId.HasValue && player.SalePrice.HasValue)
        {
            var oldSale = await _db.Sales.FirstOrDefaultAsync(s => s.PlayerId == playerId && s.SaleStatus == SaleStatus.Confirmed);
            if (oldSale != null)
            {
                oldSale.SaleStatus = SaleStatus.Reversed;
                oldSale.ReversedAt = DateTime.UtcNow;
                oldSale.ReversalReason = reason;
                _ledger.AddEntry(oldSale.TeamId, LedgerTransactionType.Reversal, oldSale.FinalAmount, reason,
                    relatedPlayerId: playerId, relatedSaleId: oldSale.Id, createdByUserId: performedByUserId);
            }
        }

        var newSale = new Sale
        {
            AuctionId = player.AuctionId,
            PlayerId = playerId,
            TeamId = newTeamId,
            FinalAmount = newAmount,
            RoundNumber = player.SoldRound ?? 0,
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

        _audit.Write("Player", playerId, "Reassigned", before,
            new { player.Status, player.TeamId, player.SalePrice }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }
}
