using System.Security.Claims;
using App.Api.Data;
using App.Api.DTOs;
using App.Api.Models;
using App.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

[ApiController]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TeamLedgerService _ledger;
    private readonly AuditLogService _audit;

    public TeamsController(AppDbContext db, TeamLedgerService ledger, AuditLogService audit)
    {
        _db = db;
        _ledger = ledger;
        _audit = audit;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    private async Task<bool> CanManageAuction(int auctionId)
    {
        if (CurrentRole == "SuperAdmin") return true;
        if (CurrentRole != "AuctionAdmin") return false;
        var auction = await _db.Auctions.FindAsync(auctionId);
        return auction != null && (auction.OwnerUserId == CurrentUserId ||
            await _db.AuctionUserAccess.AnyAsync(x => x.AuctionId == auctionId && x.UserId == CurrentUserId));
    }

    [HttpGet("api/auctions/{auctionId}/teams")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTeams(int auctionId)
    {
        var auction = await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction == null) return NotFound();
        var teams = await _db.Teams.Where(t => t.AuctionId == auctionId).ToListAsync();
        var result = teams.Select(t => new
        {
            t.Id, t.AuctionId, t.Name, t.LogoUrl, t.TeamColor, t.OwnerUserId, t.ContactInfo, t.Notes, t.OpeningBalance, t.IsActive,
            AvailableBalance = _ledger.GetAvailableBalance(t.Id),
            RosterCount = _db.Players.Count(p => p.TeamId == t.Id && (p.Status == PlayerStatus.Sold || p.IsCaptain)),
            MaximumBid = TeamBidCapacityRule.CalculateMaximumBid(
                auction,
                _db.Players.Count(p => p.TeamId == t.Id && (p.Status == PlayerStatus.Sold || p.IsCaptain)),
                _ledger.GetAvailableBalance(t.Id)),
            ReservePerRequiredSlot = TeamBidCapacityRule.ReservePerRequiredSlot(auction)
        });
        return Ok(result);
    }

    [HttpPost("api/auctions/{auctionId}/teams")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> CreateTeam(int auctionId, CreateTeamRequest req)
    {
        if (!await CanManageAuction(auctionId)) return Forbid();
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return NotFound();
        if (!TryNormalizeColor(req.TeamColor, out var teamColor))
            return BadRequest(new { error = "Team color must be a color name (for example, blue) or a hex code (for example, #0ea5e9)." });

        var team = new Team
        {
            AuctionId = auctionId,
            Name = req.Name,
            LogoUrl = req.LogoUrl,
            TeamColor = teamColor,
            OwnerUserId = req.OwnerUserId,
            ContactInfo = req.ContactInfo,
            Notes = req.Notes,
            OpeningBalance = req.OpeningBalance ?? auction.DefaultTeamBalance,
            IsActive = true
        };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        _ledger.EnsureOpeningBalance(team);
        await _db.SaveChangesAsync();
        return Ok(team);
    }

    [HttpPatch("api/teams/{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> UpdateTeam(int id, UpdateTeamRequest req)
    {
        var team = await _db.Teams.FindAsync(id);
        if (team == null) return NotFound();
        if (!await CanManageAuction(team.AuctionId)) return Forbid();
        if (req.TeamColor != null && !TryNormalizeColor(req.TeamColor, out _))
            return BadRequest(new { error = "Team color must be a color name (for example, blue) or a hex code (for example, #0ea5e9)." });

        if (req.Name != null) team.Name = req.Name;
        if (req.LogoUrl != null) team.LogoUrl = req.LogoUrl;
        if (req.TeamColor != null) { TryNormalizeColor(req.TeamColor, out var teamColor); team.TeamColor = teamColor; }
        if (req.OwnerUserId.HasValue) team.OwnerUserId = req.OwnerUserId;
        if (req.ContactInfo != null) team.ContactInfo = req.ContactInfo;
        if (req.Notes != null) team.Notes = req.Notes;
        if (req.IsActive.HasValue) team.IsActive = req.IsActive.Value;

        if (req.OpeningBalance.HasValue && req.OpeningBalance.Value != team.OpeningBalance)
        {
            var auction = await _db.Auctions.FindAsync(team.AuctionId);
            var editableStatuses = new[] { AuctionStatus.Draft, AuctionStatus.Ready };
            if (auction == null || !editableStatuses.Contains(auction.Status))
            {
                return Conflict(new { error = "Opening balance can only be changed before the auction starts. Use the correction workflow (adjust_balance) instead.", code = "BALANCE_LOCKED" });
            }
            var before = new { team.OpeningBalance };
            team.OpeningBalance = req.OpeningBalance.Value;
            _audit.Write("Team", team.Id, "OpeningBalanceChanged", before, new { team.OpeningBalance }, null, CurrentUserId);

            // Available balance is read from the ledger's latest entry, not the OpeningBalance
            // column directly. Since this path is only reachable pre-Live (no sales possible yet),
            // there is at most one prior ledger entry (the initial OpeningBalance snapshot) -
            // replace it so the new balance is reflected immediately instead of only after a full reset.
            var oldEntries = _db.TeamLedgerEntries.Where(l => l.TeamId == team.Id);
            _db.TeamLedgerEntries.RemoveRange(oldEntries);
            await _db.SaveChangesAsync();
            _ledger.EnsureOpeningBalance(team);
        }

        await _db.SaveChangesAsync();
        return Ok(team);
    }

    [HttpDelete("api/teams/{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> DeleteTeam(int id)
    {
        var team = await _db.Teams.FindAsync(id);
        if (team == null) return NotFound();
        if (!await CanManageAuction(team.AuctionId)) return Forbid();

        var hasPurchases = await _db.Players.AnyAsync(p => p.TeamId == id && (p.Status == PlayerStatus.Sold || p.IsCaptain));
        if (hasPurchases)
            return Conflict(new { error = "Cannot delete a team with completed purchases. Use the correction workflow to reassign players first." });

        _db.Teams.Remove(team);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("api/teams/{id}/ledger")]
    public async Task<IActionResult> GetLedger(int id)
    {
        var team = await _db.Teams.FindAsync(id);
        if (team == null) return NotFound();
        var entries = await _db.TeamLedgerEntries.Where(l => l.TeamId == id).OrderBy(l => l.Id).ToListAsync();
        return Ok(entries);
    }

    private static bool TryNormalizeColor(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var color = value.Trim();
        var validHex = System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{3,4}([0-9a-fA-F]{3,4})?$");
        var validName = System.Text.RegularExpressions.Regex.IsMatch(color, "^[a-zA-Z]{3,30}$");
        if (!validHex && !validName) return false;
        normalized = color;
        return true;
    }
}
