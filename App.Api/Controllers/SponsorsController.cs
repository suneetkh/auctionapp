using System.Security.Claims;
using App.Api.Data;
using App.Api.DTOs;
using App.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

[ApiController]
[Authorize]
public class SponsorsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SponsorsController(AppDbContext db) { _db = db; }

    // Same size cap as the auction logo - sponsor logos are small branding images, not a
    // photo gallery, and everything is stored as base64 directly in the SQLite file.
    private const int MaxSponsorLogoBytes = 500_000;

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

    [HttpGet("api/auctions/{auctionId}/sponsors")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSponsors(int auctionId)
    {
        var sponsors = await _db.Sponsors.Where(s => s.AuctionId == auctionId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        return Ok(sponsors);
    }

    [HttpPost("api/auctions/{auctionId}/sponsors")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> AddSponsor(int auctionId, CreateSponsorRequest req)
    {
        if (!await CanManageAuction(auctionId)) return Forbid();
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.LogoDataUri) || !req.LogoDataUri.StartsWith("data:image/"))
            return BadRequest(new { error = "Sponsor logo must be an image data URI (data:image/...)" });

        var commaIndex = req.LogoDataUri.IndexOf(',');
        var base64Part = commaIndex >= 0 ? req.LogoDataUri[(commaIndex + 1)..] : req.LogoDataUri;
        var approxBytes = (base64Part.Length * 3) / 4;
        if (approxBytes > MaxSponsorLogoBytes)
            return BadRequest(new { error = $"Sponsor logo is too large (max {MaxSponsorLogoBytes / 1000}KB). Please use a smaller image." });

        var maxSort = await _db.Sponsors.Where(s => s.AuctionId == auctionId)
            .Select(s => (int?)s.SortOrder).MaxAsync() ?? -1;

        var sponsor = new Sponsor
        {
            AuctionId = auctionId,
            Name = req.Name,
            LogoDataUri = req.LogoDataUri,
            SortOrder = maxSort + 1
        };
        _db.Sponsors.Add(sponsor);
        await _db.SaveChangesAsync();
        return Ok(sponsor);
    }

    [HttpDelete("api/sponsors/{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> DeleteSponsor(int id)
    {
        var sponsor = await _db.Sponsors.FindAsync(id);
        if (sponsor == null) return NotFound();
        if (!await CanManageAuction(sponsor.AuctionId)) return Forbid();

        _db.Sponsors.Remove(sponsor);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
