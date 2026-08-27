using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class AuctionLifecycleService
{
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;
    private readonly TeamLedgerService _ledger;

    public AuctionLifecycleService(AppDbContext db, AuditLogService audit, TeamLedgerService ledger)
    {
        _db = db;
        _audit = audit;
        _ledger = ledger;
    }

    public static bool CanTransition(AuctionStatus from, AuctionStatus to)
    {
        return (from, to) switch
        {
            (AuctionStatus.Draft, AuctionStatus.Ready) => true,
            (AuctionStatus.Draft, AuctionStatus.Live) => true,
            (AuctionStatus.Ready, AuctionStatus.Live) => true,
            (AuctionStatus.Live, AuctionStatus.Paused) => true,
            (AuctionStatus.Paused, AuctionStatus.Live) => true,
            (AuctionStatus.Live, AuctionStatus.MainRoundComplete) => true,
            (AuctionStatus.MainRoundComplete, AuctionStatus.UnsoldPoolOpen) => true,
            (AuctionStatus.UnsoldPoolOpen, AuctionStatus.MainRoundComplete) => true,
            (AuctionStatus.MainRoundComplete, AuctionStatus.Completed) => true,
            (AuctionStatus.UnsoldPoolOpen, AuctionStatus.Completed) => true,
            (AuctionStatus.Completed, AuctionStatus.Archived) => true,
            _ => false
        };
    }

    public async Task<(bool Success, string? Error)> ValidateStartAsync(int auctionId)
    {
        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return (false, "Auction not found");
        if (auction.Status != AuctionStatus.Ready && auction.Status != AuctionStatus.Draft)
            return (false, "Auction must be Draft or Ready to start");

        var teamCount = await _db.Teams.CountAsync(t => t.AuctionId == auctionId && t.IsActive);
        if (teamCount < 2) return (false, "At least 2 active teams are required to start the auction");

        var playerCount = await _db.Players.CountAsync(p => p.AuctionId == auctionId);
        if (playerCount < 1) return (false, "At least 1 player is required to start the auction");

        return (true, null);
    }

    public bool CanEditRules(AuctionStatus status)
    {
        return status == AuctionStatus.Draft || status == AuctionStatus.Ready;
    }

    public async Task<(bool Success, string? Error)> ValidateCompleteAsync(int auctionId, bool forceConfirm)
    {
        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return (false, "Auction not found");

        if (auction.Status != AuctionStatus.MainRoundComplete && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return (false, "Auction must have completed its main round before it can be completed");

        var unresolved = await _db.Players.CountAsync(p => p.AuctionId == auctionId &&
            (p.Status == PlayerStatus.Unsold || p.Status == PlayerStatus.ReauctionAvailable
             || p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));

        if (unresolved > 0 && !forceConfirm)
            return (false, $"There are {unresolved} unresolved players in the unsold pool. Pass forceConfirm=true to complete anyway.");

        return (true, null);
    }

    /// <summary>
    /// Wipes the auction back to a clean Draft state: every player back to Available
    /// (Withdrawn players stay Withdrawn), every team back to its opening balance, and
    /// all bids/sales/ledger entries deleted. Used to restart a test/demo auction from
    /// scratch rather than reversing sales one at a time.
    /// </summary>
    public async Task<(bool Success, string? Error)> ResetAuctionAsync(int auctionId, int performedByUserId)
    {
        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return (false, "Auction not found");

        using var tx = await _db.Database.BeginTransactionAsync();

        var teams = await _db.Teams.Where(t => t.AuctionId == auctionId).ToListAsync();
        var teamIds = teams.Select(t => t.Id).ToList();

        var bids = _db.Bids.Where(b => b.AuctionId == auctionId);
        var sales = _db.Sales.Where(s => s.AuctionId == auctionId);
        var ledgerEntries = _db.TeamLedgerEntries.Where(l => teamIds.Contains(l.TeamId));

        _db.Bids.RemoveRange(bids);
        _db.Sales.RemoveRange(sales);
        _db.TeamLedgerEntries.RemoveRange(ledgerEntries);

        // Must actually persist the ledger wipe before re-seeding: EnsureOpeningBalance checks
        // "does this team already have any ledger entries?" via a fresh DB query, which would
        // still see the old (not-yet-saved) entries and skip re-seeding otherwise - leaving each
        // team's ledger empty until its first post-reset sale, which is exactly what caused the
        // "first sale after reset goes negative" bug (that sale's Purchase entry got mistaken for
        // the opening-balance seed, since AddEntry treats "no prior entries" as the seed case).
        await _db.SaveChangesAsync();
        foreach (var team in teams)
        {
            _ledger.EnsureOpeningBalance(team);
        }
        await _db.SaveChangesAsync();

        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        foreach (var player in players)
        {
            if (player.IsCaptain)
            {
                player.Status = PlayerStatus.Withdrawn;
                player.SalePrice = null;
                player.SoldRound = null;
                if (player.TeamId.HasValue && (player.CaptainCost ?? 0) > 0)
                    _ledger.AddEntry(player.TeamId.Value, LedgerTransactionType.CaptainAssignment,
                        -player.CaptainCost!.Value, $"Captain assignment: {player.Name}", player.Id,
                        createdByUserId: performedByUserId);
                continue;
            }
            if (player.Status == PlayerStatus.Withdrawn) continue;
            player.Status = PlayerStatus.Available;
            player.TeamId = null;
            player.SalePrice = null;
            player.SoldRound = null;
        }

        var beforeAuction = new { auction.Status, auction.CurrentRound };
        auction.Status = AuctionStatus.Draft;
        auction.CurrentRound = 1;

        _audit.Write("Auction", auctionId, "Reset", beforeAuction,
            new { auction.Status, auction.CurrentRound }, "Full reset to Draft", performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }

    /// <summary>
    /// Reopens a Completed auction back into UnsoldPoolOpen - for when it was completed too
    /// early (e.g. force-completed with unresolved players still pending). Any stray players
    /// still sitting in Available (left over from the main round, which the wheel no longer
    /// draws from once the auction has moved past it) are promoted to ReauctionAvailable so
    /// they're actually reachable again, rather than reopening into a state that still misses them.
    /// </summary>
    public async Task<(bool Success, string? Error)> ReopenAuctionAsync(int auctionId, string reason, int performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required");

        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return (false, "Auction not found");
        if (auction.Status != AuctionStatus.Completed)
            return (false, "Only a Completed auction can be reopened");

        using var tx = await _db.Database.BeginTransactionAsync();

        var strayAvailable = await _db.Players
            .Where(p => p.AuctionId == auctionId && p.Status == PlayerStatus.Available)
            .ToListAsync();
        foreach (var player in strayAvailable)
            player.Status = PlayerStatus.ReauctionAvailable;

        var before = new { auction.Status };
        auction.Status = AuctionStatus.UnsoldPoolOpen;

        _audit.Write("Auction", auctionId, "Reopened", before, new { auction.Status }, reason, performedByUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, null);
    }
}
