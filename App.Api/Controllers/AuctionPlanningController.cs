using System.Security.Claims;
using System.Text.Json;
using App.Api.Data;
using App.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

public record SavePlanningStateRequest(JsonElement State);

[ApiController]
[Authorize(Roles = "SuperAdmin,AuctionAdmin")]
public class AuctionPlanningController : ControllerBase
{
    private const int MaxStateCharacters = 2_000_000;
    private readonly AppDbContext _db;
    public AuctionPlanningController(AppDbContext db) { _db = db; }
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    private async Task<bool> CanManage(int auctionId)
    {
        if (CurrentRole == "SuperAdmin") return true;
        var auction = await _db.Auctions.FindAsync(auctionId);
        return auction != null && (auction.OwnerUserId == CurrentUserId ||
            await _db.AuctionUserAccess.AnyAsync(x => x.AuctionId == auctionId && x.UserId == CurrentUserId));
    }

    [HttpGet("api/auctions/{auctionId}/planning/{kind}")]
    public async Task<IActionResult> Get(int auctionId, string kind)
    {
        if (!await CanManage(auctionId)) return Forbid();
        if (kind is not ("draw" or "fixtures")) return NotFound();
        var row = await _db.AuctionPlanningStates.FindAsync(auctionId);
        var json = kind == "draw" ? row?.DrawStateJson : row?.FixtureStateJson;
        return Content(json ?? "null", "application/json");
    }

    [HttpGet("api/auctions/{auctionId}/planning/locks")]
    public async Task<IActionResult> GetLocks(int auctionId)
    {
        if (!await CanManage(auctionId)) return Forbid();
        var row = await _db.AuctionPlanningStates.FindAsync(auctionId);
        return Ok(new { drawLocked = row?.DrawLocked ?? false, fixturesLocked = row?.FixturesLocked ?? false });
    }

    [HttpPut("api/auctions/{auctionId}/planning/{kind}")]
    public async Task<IActionResult> Save(int auctionId, string kind, SavePlanningStateRequest request)
    {
        if (!await CanManage(auctionId)) return Forbid();
        if (kind is not ("draw" or "fixtures")) return NotFound();
        if (!await _db.Auctions.AnyAsync(a => a.Id == auctionId)) return NotFound();
        var json = request.State.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : request.State.GetRawText();
        if (json?.Length > MaxStateCharacters) return BadRequest(new { error = "The saved tournament plan is too large." });
        var row = await _db.AuctionPlanningStates.FindAsync(auctionId);
        if (row == null) { row = new AuctionPlanningState { AuctionId = auctionId }; _db.AuctionPlanningStates.Add(row); }
        if ((kind == "draw" && row.DrawLocked) || (kind == "fixtures" && row.FixturesLocked))
            return Conflict(new { error = kind == "draw"
                ? "Team assignments are locked. Unlock them from Auction Lifecycle before making changes."
                : "Fixtures are locked. Unlock them from Auction Lifecycle before making changes." });
        if (kind == "draw") row.DrawStateJson = json; else row.FixtureStateJson = json;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("api/auctions/{auctionId}/planning/{kind}/lock")]
    public async Task<IActionResult> Lock(int auctionId, string kind)
    {
        if (!await CanManage(auctionId)) return Forbid();
        if (kind is not ("draw" or "fixtures")) return NotFound();
        var row = await _db.AuctionPlanningStates.FindAsync(auctionId);
        if (row == null) return BadRequest(new { error = "There is no saved tournament plan to lock." });
        var hasState = kind == "draw" ? !string.IsNullOrWhiteSpace(row.DrawStateJson) : !string.IsNullOrWhiteSpace(row.FixtureStateJson);
        if (!hasState) return BadRequest(new { error = kind == "draw" ? "Complete and save the team draw before locking it." : "Generate and save fixtures before locking them." });
        if (kind == "draw") row.DrawLocked = true; else row.FixturesLocked = true;
        await _db.SaveChangesAsync();
        return Ok(new { drawLocked = row.DrawLocked, fixturesLocked = row.FixturesLocked });
    }

    [HttpPost("api/auctions/{auctionId}/planning/{kind}/unlock")]
    public async Task<IActionResult> Unlock(int auctionId, string kind)
    {
        if (!await CanManage(auctionId)) return Forbid();
        if (kind is not ("draw" or "fixtures")) return NotFound();
        var row = await _db.AuctionPlanningStates.FindAsync(auctionId);
        if (row == null) return NotFound();
        if (kind == "draw") row.DrawLocked = false; else row.FixturesLocked = false;
        await _db.SaveChangesAsync();
        return Ok(new { drawLocked = row.DrawLocked, fixturesLocked = row.FixturesLocked });
    }
}
