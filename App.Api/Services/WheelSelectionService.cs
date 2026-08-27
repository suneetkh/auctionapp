using App.Api.Data;
using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Services;

public class WheelSelectionService
{
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;
    private static readonly Random _rng = new();

    public WheelSelectionService(AppDbContext db, AuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Player?> SpinAndSelectAsync(Auction auction, int performedByUserId)
    {
        var eligibleStatus = auction.Status == AuctionStatus.UnsoldPoolOpen
            ? PlayerStatus.ReauctionAvailable
            : PlayerStatus.Available;

        var candidates = await _db.Players
            .Where(p => p.AuctionId == auction.Id && p.Status == eligibleStatus)
            .ToListAsync();

        if (candidates.Count == 0) return null;

        var chosen = candidates[_rng.Next(candidates.Count)];
        chosen.Status = PlayerStatus.Selected;

        _audit.Write("Player", chosen.Id, "Selected", null, new { chosen.Status, round = auction.CurrentRound },
            $"Wheel selection, round {auction.CurrentRound}", performedByUserId);

        _db.AuctionEvents.Add(new AuctionEvent
        {
            AuctionId = auction.Id,
            EventType = "player_selected",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { playerId = chosen.Id, round = auction.CurrentRound })
        });

        await _db.SaveChangesAsync();
        return chosen;
    }
}
