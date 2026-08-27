using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class AuctionDeletionService
{
    private readonly AppDbContext _db;

    public AuctionDeletionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int auctionId)
    {
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return (false, "Auction not found");

        if (auction.Status is AuctionStatus.Live or AuctionStatus.Paused or AuctionStatus.UnsoldPoolOpen)
            return (false, "A live auction cannot be deleted. Complete or archive it first.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var teamIds = await _db.Teams.Where(t => t.AuctionId == auctionId).Select(t => t.Id).ToListAsync();
        var playerIds = await _db.Players.Where(p => p.AuctionId == auctionId).Select(p => p.Id).ToListAsync();
        var bidIds = await _db.Bids.Where(b => b.AuctionId == auctionId).Select(b => b.Id).ToListAsync();
        var saleIds = await _db.Sales.Where(s => s.AuctionId == auctionId).Select(s => s.Id).ToListAsync();

        // Several history tables intentionally have no EF navigation/FK. Remove them explicitly,
        // along with audit entries whose entity belongs to this auction, before deleting the root.
        // Use ordered database-side deletes. Mixing tracked RemoveRange calls with database
        // cascades can make EF try to delete an already-cascaded row and raise a false
        // concurrency error on auctions with real ledger history.
        await _db.TeamLedgerEntries.Where(l => teamIds.Contains(l.TeamId)).ExecuteDeleteAsync();
        await _db.Bids.Where(b => b.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.Sales.Where(s => s.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.AuctionEvents.Where(e => e.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.Sponsors.Where(s => s.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.AuditLogs.Where(log =>
            (log.EntityType == "Auction" && log.EntityId == auctionId) ||
            (log.EntityType == "Team" && teamIds.Contains(log.EntityId)) ||
            (log.EntityType == "Player" && playerIds.Contains(log.EntityId)) ||
            (log.EntityType == "Bid" && bidIds.Contains(log.EntityId)) ||
            (log.EntityType == "Sale" && saleIds.Contains(log.EntityId))).ExecuteDeleteAsync();

        await _db.AuctionUserAccess.Where(x => x.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.AuctionPlanningStates.Where(x => x.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.AuctionRules.Where(r => r.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.Players.Where(p => p.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.Teams.Where(t => t.AuctionId == auctionId).ExecuteDeleteAsync();
        await _db.Auctions.Where(a => a.Id == auctionId).ExecuteDeleteAsync();
        await tx.CommitAsync();
        return (true, null);
    }
}
