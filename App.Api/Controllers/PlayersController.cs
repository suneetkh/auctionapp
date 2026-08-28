using System.Globalization;
using System.Security.Claims;
using System.Text;
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
public class PlayersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CaptainAssignmentService _captains;
    public PlayersController(AppDbContext db, CaptainAssignmentService captains) { _db = db; _captains = captains; }

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

    private static string? ValidateIncrementAlignedPrice(Auction auction, decimal value, string label)
    {
        if (value <= 0) return $"{label} must be greater than zero.";
        if (auction.BidIncrementAmount <= 0) return "Auction bid increment must be greater than zero.";
        return value % auction.BidIncrementAmount == 0
            ? null
            : $"{label} must be a whole multiple of the auction bid increment ({auction.BidIncrementAmount:0.##}).";
    }

    [HttpGet("api/auctions/{auctionId}/players")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlayers(int auctionId, [FromQuery] bool includePhotos = true)
    {
        var players = await _db.Players.AsNoTracking().Where(p => p.AuctionId == auctionId).ToListAsync();
        if (!includePhotos)
        {
            // Live roster/pool views do not render player photos. Avoid repeatedly sending every
            // base64 photo across the host's internet connection on each real-time auction update.
            foreach (var player in players) player.PhotoUrl = null;
        }
        return Ok(players);
    }

    [HttpPost("api/auctions/{auctionId}/players")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> CreatePlayer(int auctionId, CreatePlayerRequest req)
    {
        if (!await CanManageAuction(auctionId)) return Forbid();
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return NotFound();
        var basePriceError = ValidateIncrementAlignedPrice(auction, req.BasePrice, "Base price");
        if (basePriceError != null) return BadRequest(new { error = basePriceError });
        if (req.MinimumBidOverride.HasValue)
        {
            var overrideError = ValidateIncrementAlignedPrice(auction, req.MinimumBidOverride.Value, "Minimum bid override");
            if (overrideError != null) return BadRequest(new { error = overrideError });
        }

        var player = new Player
        {
            AuctionId = auctionId,
            Name = req.Name,
            PhotoUrl = req.PhotoUrl,
            AgeOrDob = req.AgeOrDob,
            Role = req.Role,
            SkillTags = req.SkillTags,
            BasePrice = req.BasePrice,
            MinimumBidOverride = req.MinimumBidOverride,
            Notes = req.Notes,
            ContactInfo = req.ContactInfo,
            Status = PlayerStatus.Available
        };
        _db.Players.Add(player);
        await _db.SaveChangesAsync();
        if (req.IsCaptain)
        {
            var captainResult = await _captains.SetAsync(player.Id, true, req.CaptainTeamId, req.CaptainCost ?? 0, CurrentUserId);
            if (!captainResult.Success)
            {
                _db.Players.Remove(player);
                await _db.SaveChangesAsync();
                return Conflict(new { error = captainResult.Error });
            }
        }
        return Ok(player);
    }

    [HttpPatch("api/players/{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> UpdatePlayer(int id, UpdatePlayerRequest req)
    {
        var player = await _db.Players.FindAsync(id);
        if (player == null) return NotFound();
        if (!await CanManageAuction(player.AuctionId)) return Forbid();
        var auction = await _db.Auctions.FindAsync(player.AuctionId);
        if (auction == null) return NotFound();
        if (req.BasePrice.HasValue)
        {
            var basePriceError = ValidateIncrementAlignedPrice(auction, req.BasePrice.Value, "Base price");
            if (basePriceError != null) return BadRequest(new { error = basePriceError });
        }
        if (req.MinimumBidOverride.HasValue)
        {
            var overrideError = ValidateIncrementAlignedPrice(auction, req.MinimumBidOverride.Value, "Minimum bid override");
            if (overrideError != null) return BadRequest(new { error = overrideError });
        }

        var identityFieldsChanged = req.Name != null || req.Role != null || req.BasePrice.HasValue;
        if (identityFieldsChanged && player.Status == PlayerStatus.Sold)
        {
            return Conflict(new { error = "Name, role, and base price are locked once a player is sold. Use the correction workflow to reassign/reverse instead.", code = "PLAYER_IDENTITY_LOCKED" });
        }

        if (req.Name != null) player.Name = req.Name;
        if (req.PhotoUrl != null) player.PhotoUrl = req.PhotoUrl;
        if (req.AgeOrDob != null) player.AgeOrDob = req.AgeOrDob;
        if (req.Role != null) player.Role = req.Role;
        if (req.SkillTags != null) player.SkillTags = req.SkillTags;
        if (req.BasePrice.HasValue) player.BasePrice = req.BasePrice.Value;
        if (req.MinimumBidOverride.HasValue) player.MinimumBidOverride = req.MinimumBidOverride;
        if (req.Notes != null) player.Notes = req.Notes;
        if (req.ContactInfo != null) player.ContactInfo = req.ContactInfo;
        if (req.Status.HasValue) player.Status = req.Status.Value;
        await _db.SaveChangesAsync();
        return Ok(player);
    }

    [HttpDelete("api/players/{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> DeletePlayer(int id)
    {
        var player = await _db.Players.FindAsync(id);
        if (player == null) return NotFound();
        if (!await CanManageAuction(player.AuctionId)) return Forbid();
        if (player.Status == PlayerStatus.Sold)
            return Conflict(new { error = "Cannot delete a sold player. Reverse the sale first via correction." });
        if (player.IsCaptain)
            return Conflict(new { error = "Remove the captain assignment before deleting this player." });

        _db.Players.Remove(player);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("api/players/{id}/captain")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> SetCaptain(int id, SetCaptainRequest req)
    {
        var player = await _db.Players.FindAsync(id);
        if (player == null) return NotFound();
        if (!await CanManageAuction(player.AuctionId)) return Forbid();
        var result = await _captains.SetAsync(id, req.IsCaptain, req.TeamId, req.Cost, CurrentUserId);
        return result.Success ? Ok(result.Player) : Conflict(new { error = result.Error });
    }

    [HttpPost("api/auctions/{auctionId}/players/import")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> ImportCsv(int auctionId, IFormFile file)
    {
        if (!await CanManageAuction(auctionId)) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file uploaded" });
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return NotFound();

        var added = 0;
        var captainRows = new List<(Player Player, string Team, decimal Cost)>();
        using var reader = new StreamReader(file.OpenReadStream());
        string? line;
        var isHeader = true;
        var rowNumber = 1;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            rowNumber++;
            if (isHeader) { isHeader = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            // name,role,basePrice,skillTags,photoUrl,ageOrDob,notes,captainTeam,captainCost
            if (parts.Length < 3) continue;
            var name = parts[0].Trim();
            var role = parts[1].Trim();
            if (!decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var basePrice)) continue;
            var basePriceError = ValidateIncrementAlignedPrice(auction, basePrice, $"Base price on CSV row {rowNumber}");
            if (basePriceError != null) return BadRequest(new { error = basePriceError });

            var player = new Player
            {
                AuctionId = auctionId,
                Name = name,
                Role = role,
                BasePrice = basePrice,
                SkillTags = parts.Length > 3 ? parts[3].Trim() : null,
                PhotoUrl = parts.Length > 4 ? parts[4].Trim() : null,
                AgeOrDob = parts.Length > 5 ? parts[5].Trim() : null,
                Notes = parts.Length > 6 ? parts[6].Trim() : null,
                Status = PlayerStatus.Available
            };
            _db.Players.Add(player);
            if (parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7]))
            {
                var cost = 0m;
                if (parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8]) &&
                    !decimal.TryParse(parts[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out cost))
                    return BadRequest(new { error = $"Invalid captain cost for {name}." });
                captainRows.Add((player, parts[7].Trim(), cost));
            }
            added++;
        }
        await _db.SaveChangesAsync();
        var teams = await _db.Teams.Where(t => t.AuctionId == auctionId).ToListAsync();
        var warnings = new List<string>();
        var captainsAssigned = 0;
        foreach (var row in captainRows)
        {
            var team = teams.FirstOrDefault(t => string.Equals(t.Name, row.Team, StringComparison.OrdinalIgnoreCase))
                ?? (int.TryParse(row.Team, out var teamId) ? teams.FirstOrDefault(t => t.Id == teamId) : null);
            if (team == null) { warnings.Add($"{row.Player.Name}: team '{row.Team}' was not found."); continue; }
            var result = await _captains.SetAsync(row.Player.Id, true, team.Id, row.Cost, CurrentUserId);
            if (result.Success) captainsAssigned++;
            else warnings.Add($"{row.Player.Name}: {result.Error}");
        }
        return Ok(new { imported = added, captainsAssigned, warnings });
    }

    [HttpGet("api/auctions/{auctionId}/players/export")]
    public async Task<IActionResult> ExportCsv(int auctionId)
    {
        var players = await _db.Players.Where(p => p.AuctionId == auctionId).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Name,Role,BasePrice,SkillTags,Status,TeamId,SalePrice,IsCaptain,CaptainCost");
        foreach (var p in players)
        {
            sb.AppendLine($"{p.Name},{p.Role},{p.BasePrice},{p.SkillTags},{p.Status},{p.TeamId},{p.SalePrice},{p.IsCaptain},{p.CaptainCost}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"players_auction_{auctionId}.csv");
    }
}
