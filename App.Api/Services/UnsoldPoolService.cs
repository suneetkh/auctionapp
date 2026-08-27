using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class UnsoldPoolService
{
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;

    public UnsoldPoolService(AppDbContext db, AuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> IsMainRoundCompleteAsync(int auctionId)
    {
        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        if (players.Count == 0) return false;
        return players.All(p => p.Status == PlayerStatus.Sold || p.Status == PlayerStatus.Unsold
            || p.Status == PlayerStatus.Withdrawn || p.Status == PlayerStatus.FinalUnsold);
    }

    public async Task<(bool Success, string? Error)> OpenUnsoldPoolAsync(Auction auction, int performedByUserId)
    {
        if (auction.Status != AuctionStatus.MainRoundComplete && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return (false, "Auction must be in MainRoundComplete state to open the unsold pool");

        var rules = auction.Rules;
        var maxRounds = rules?.MaxUnsoldRounds ?? 1;
        if (auction.CurrentRound >= maxRounds + 1)
            return (false, "Maximum unsold rounds already reached");

        var unsoldPlayers = await _db.Players
            .Where(p => p.AuctionId == auction.Id && p.Status == PlayerStatus.Unsold)
            .ToListAsync();

        // A reversed sale can already leave a player as ReauctionAvailable before the pool is
        // formally (re)opened (e.g. its round auto-completed the moment the sale happened, then
        // got undone) - that player still needs to count as "something to reopen for", not just
        // players literally sitting in Unsold right now.
        var alreadyReauctionAvailable = await _db.Players
            .CountAsync(p => p.AuctionId == auction.Id && p.Status == PlayerStatus.ReauctionAvailable);

        if (unsoldPlayers.Count == 0 && alreadyReauctionAvailable == 0)
            return (false, "Unsold pool is empty, nothing to reopen");

        foreach (var p in unsoldPlayers)
        {
            var before = new { p.Status };
            p.Status = PlayerStatus.ReauctionAvailable;
            _audit.Write("Player", p.Id, "ReauctionAvailable", before, new { p.Status }, "Unsold pool opened", performedByUserId);
        }

        auction.CurrentRound += 1;
        auction.Status = AuctionStatus.UnsoldPoolOpen;

        _db.AuctionEvents.Add(new AuctionEvent
        {
            AuctionId = auction.Id,
            EventType = "unsold_pool_opened",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { round = auction.CurrentRound, count = unsoldPlayers.Count })
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task FinalizeRemainingUnsoldAsync(int auctionId, int performedByUserId)
    {
        var remaining = await _db.Players
            .Where(p => p.AuctionId == auctionId && p.Status == PlayerStatus.Unsold)
            .ToListAsync();
        foreach (var p in remaining)
        {
            var before = new { p.Status };
            p.Status = PlayerStatus.FinalUnsold;
            _audit.Write("Player", p.Id, "FinalUnsold", before, new { p.Status }, "Final unsold round reached", performedByUserId);
        }
        await _db.SaveChangesAsync();
    }
}
