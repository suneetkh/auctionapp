using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class CaptainAssignmentService
{
    private readonly AppDbContext _db;
    private readonly TeamLedgerService _ledger;
    private readonly AuditLogService _audit;

    public CaptainAssignmentService(AppDbContext db, TeamLedgerService ledger, AuditLogService audit)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
    }

    public async Task<(bool Success, string? Error, Player? Player)> SetAsync(
        int playerId, bool isCaptain, int? teamId, decimal? requestedCost, int performedByUserId)
    {
        var player = await _db.Players.FindAsync(playerId);
        if (player == null) return (false, "Player not found", null);
        var auction = await _db.Auctions.FindAsync(player.AuctionId);
        if (auction == null) return (false, "Auction not found", null);
        if (auction.Status != AuctionStatus.Draft && auction.Status != AuctionStatus.Ready)
            return (false, "Captain assignments can only be changed before the auction goes live", null);
        if (player.Status == PlayerStatus.Sold)
            return (false, "A sold player cannot be made a captain", null);

        var cost = requestedCost ?? 0;
        if (cost < 0) return (false, "Captain cost cannot be negative", null);

        Team? newTeam = null;
        if (isCaptain)
        {
            if (!teamId.HasValue) return (false, "Select the captain's team", null);
            newTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.AuctionId == player.AuctionId);
            if (newTeam == null) return (false, "Selected team does not belong to this auction", null);
            if (await _db.Players.AnyAsync(p => p.AuctionId == player.AuctionId && p.IsCaptain &&
                p.TeamId == teamId && p.Id != playerId))
                return (false, $"{newTeam.Name} already has a captain", null);

            var rosterWithoutPlayer = await _db.Players.CountAsync(p => p.TeamId == teamId &&
                (p.Status == PlayerStatus.Sold || p.IsCaptain) && p.Id != playerId);
            if (auction.RosterMaxSize.HasValue && rosterWithoutPlayer >= auction.RosterMaxSize.Value)
                return (false, $"{newTeam.Name} has reached its maximum roster size", null);

            var refundedOldCost = player.IsCaptain && player.TeamId == teamId ? player.CaptainCost ?? 0 : 0;
            if (cost > _ledger.GetAvailableBalance(teamId.Value) + refundedOldCost)
                return (false, $"{newTeam.Name} has insufficient available purse for this captain cost", null);
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        var before = new { player.IsCaptain, player.TeamId, player.CaptainCost, player.Status };

        if (player.IsCaptain && player.TeamId.HasValue && (player.CaptainCost ?? 0) > 0)
        {
            _ledger.AddEntry(player.TeamId.Value, LedgerTransactionType.CaptainReversal, player.CaptainCost!.Value,
                $"Captain assignment reversed for {player.Name}", player.Id, createdByUserId: performedByUserId);
            await _db.SaveChangesAsync();
        }

        if (!isCaptain)
        {
            player.IsCaptain = false;
            player.CaptainCost = null;
            player.TeamId = null;
            player.Status = PlayerStatus.Available;
        }
        else
        {
            player.IsCaptain = true;
            player.CaptainCost = cost;
            player.TeamId = teamId;
            player.Status = PlayerStatus.Withdrawn;
            player.SalePrice = null;
            player.SoldRound = null;
            if (cost > 0)
                _ledger.AddEntry(teamId!.Value, LedgerTransactionType.CaptainAssignment, -cost,
                    $"Captain assignment: {player.Name}", player.Id, createdByUserId: performedByUserId);
        }

        _audit.Write("Player", player.Id, isCaptain ? "CaptainAssigned" : "CaptainRemoved", before,
            new { player.IsCaptain, player.TeamId, player.CaptainCost, player.Status }, null, performedByUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null, player);
    }
}
