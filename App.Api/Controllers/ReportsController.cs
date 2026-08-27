using System.Text;
using App.Api.Data;
using App.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

[ApiController]
[Route("api/auctions/{auctionId}")]
[AllowAnonymous]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReportsController(AppDbContext db) { _db = db; }

    [HttpGet("reports/summary")]
    public async Task<IActionResult> Summary(int auctionId)
    {
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return NotFound();

        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        var sold = players.Count(p => p.Status == PlayerStatus.Sold);
        var captains = players.Count(p => p.IsCaptain);
        var unsold = players.Count(p => p.Status == PlayerStatus.Unsold || p.Status == PlayerStatus.FinalUnsold);
        var totalSpent = await _db.Sales.Where(s => s.AuctionId == auctionId && s.SaleStatus == SaleStatus.Confirmed).SumAsync(s => s.FinalAmount);

        return Ok(new
        {
            auction.Name,
            Status = auction.Status.ToString(),
            TotalPlayers = players.Count,
            Sold = sold,
            Captains = captains,
            Unsold = unsold,
            Remaining = players.Count - sold - unsold - captains,
            TotalSpent = totalSpent,
            TeamCount = await _db.Teams.CountAsync(t => t.AuctionId == auctionId)
        });
    }

    [HttpGet("reports/teams")]
    public async Task<IActionResult> Teams(int auctionId, [FromServices] Services.TeamLedgerService ledger)
    {
        var teams = await _db.Teams.Where(t => t.AuctionId == auctionId).ToListAsync();
        var result = teams.Select(t => new
        {
            t.Id,
            t.Name,
            t.OpeningBalance,
            AvailableBalance = ledger.GetAvailableBalance(t.Id),
            Roster = _db.Players.Where(p => p.TeamId == t.Id && (p.Status == PlayerStatus.Sold || p.IsCaptain))
                .Select(p => new { p.Id, p.Name, p.Role, p.SalePrice, p.IsCaptain, p.CaptainCost }).ToList()
        });
        return Ok(result);
    }

    [HttpGet("reports/players")]
    public async Task<IActionResult> Players(int auctionId)
    {
        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        return Ok(players);
    }

    [HttpGet("reports/bids")]
    public async Task<IActionResult> Bids(int auctionId)
    {
        var bids = await _db.Bids.Where(b => b.AuctionId == auctionId).OrderByDescending(b => b.Id).ToListAsync();
        return Ok(bids);
    }

    // Exposes Sale.Id so the correction workflow can target a specific sale (including
    // ones from a prior session, not just "the last thing I just sold").
    [HttpGet("reports/sales")]
    public async Task<IActionResult> Sales(int auctionId)
    {
        var sales = await _db.Sales.Where(s => s.AuctionId == auctionId).OrderByDescending(s => s.Id).ToListAsync();
        return Ok(sales);
    }

    [HttpGet("reports/audit")]
    public async Task<IActionResult> Audit(int auctionId)
    {
        // audit logs aren't auction-scoped directly; filter by related player/team/auction entity ids for this auction
        var playerIds = await _db.Players.Where(p => p.AuctionId == auctionId).Select(p => p.Id).ToListAsync();
        var teamIds = await _db.Teams.Where(t => t.AuctionId == auctionId).Select(t => t.Id).ToListAsync();

        var logs = await _db.AuditLogs.Where(l =>
            (l.EntityType == "Auction" && l.EntityId == auctionId) ||
            (l.EntityType == "Player" && playerIds.Contains(l.EntityId)) ||
            (l.EntityType == "Team" && teamIds.Contains(l.EntityId)) ||
            (l.EntityType == "Sale"))
            .OrderByDescending(l => l.Id).ToListAsync();
        return Ok(logs);
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(int auctionId)
    {
        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        var teams = await _db.Teams.Where(t => t.AuctionId == auctionId).ToDictionaryAsync(t => t.Id, t => t.Name);

        var sb = new StringBuilder();
        sb.AppendLine("Name,Role,BasePrice,Status,Team,SalePrice,SoldRound,IsCaptain,CaptainCost");
        foreach (var p in players)
        {
            var teamName = p.TeamId.HasValue && teams.ContainsKey(p.TeamId.Value) ? teams[p.TeamId.Value] : "";
            sb.AppendLine($"{p.Name},{p.Role},{p.BasePrice},{p.Status},{teamName},{p.SalePrice},{p.SoldRound},{p.IsCaptain},{p.CaptainCost}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"auction_{auctionId}_report.csv");
    }
}
