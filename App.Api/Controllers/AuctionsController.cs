using System.Security.Claims;
using App.Api.Data;
using App.Api.DTOs;
using App.Api.Hubs;
using App.Api.Models;
using App.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

[ApiController]
[Route("api/auctions")]
[Authorize]
public class AuctionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuctionLifecycleService _lifecycle;
    private readonly IAuctionBroadcaster _broadcaster;
    private readonly AuditLogService _audit;
    private readonly AuctionDeletionService _deletion;

    public AuctionsController(AppDbContext db, AuctionLifecycleService lifecycle, IAuctionBroadcaster broadcaster,
        AuditLogService audit, AuctionDeletionService deletion)
    {
        _db = db;
        _lifecycle = lifecycle;
        _broadcaster = broadcaster;
        _audit = audit;
        _deletion = deletion;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    private async Task<bool> CanManage(int auctionId)
    {
        if (CurrentRole == "SuperAdmin") return true;
        if (CurrentRole != "AuctionAdmin") return false;
        var auction = await _db.Auctions.FindAsync(auctionId);
        return auction != null && (auction.OwnerUserId == CurrentUserId ||
            await _db.AuctionUserAccess.AnyAsync(x => x.AuctionId == auctionId && x.UserId == CurrentUserId));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var query = _db.Auctions.AsQueryable();
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            query = query.Where(a => a.Visibility != AuctionVisibility.Private);
        }
        else if (CurrentRole == "AuctionAdmin")
        {
            query = query.Where(a => a.OwnerUserId == CurrentUserId ||
                _db.AuctionUserAccess.Any(x => x.AuctionId == a.Id && x.UserId == CurrentUserId));
        }
        var list = await query.OrderByDescending(a => a.Id).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOne(int id)
    {
        var auction = await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == id);
        if (auction == null) return NotFound();

        if (auction.Visibility == AuctionVisibility.Private)
        {
            if (!(User.Identity?.IsAuthenticated ?? false)) return Forbid();
            if (CurrentRole == "AuctionAdmin" && !await CanManage(id)) return Forbid();
        }
        return Ok(auction);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Create(CreateAuctionRequest req)
    {
        var auction = new Auction
        {
            Name = req.Name,
            SportType = req.SportType,
            TournamentName = req.TournamentName,
            DateTime = req.DateTime,
            VenueOrOnlineLabel = req.VenueOrOnlineLabel,
            CurrencyLabel = req.CurrencyLabel,
            DefaultTeamBalance = req.DefaultTeamBalance,
            MinimumBidAmount = req.MinimumBidAmount,
            BidIncrementAmount = req.BidIncrementAmount,
            RosterMinSize = req.RosterMinSize,
            RosterMaxSize = req.RosterMaxSize,
            Visibility = req.Visibility,
            BiddingMode = req.BiddingMode,
            Status = AuctionStatus.Draft,
            OwnerUserId = CurrentUserId
        };
        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();

        _db.AuctionRules.Add(new AuctionRules
        {
            AuctionId = auction.Id,
            UnsoldRoundsEnabled = req.UnsoldRoundsEnabled,
            MaxUnsoldRounds = req.MaxUnsoldRounds,
            AllowReducedBasePriceInUnsold = req.AllowReducedBasePriceInUnsold,
            CustomUnsoldMinBid = req.CustomUnsoldMinBid,
            AllowWheelSelectionFromPool = req.AllowWheelSelectionFromPool,
            MinRemainingPurseRule = req.MinRemainingPurseRule
        });
        await _db.SaveChangesAsync();
        _audit.Write("Auction", auction.Id, "Created", null, auction, null, CurrentUserId);
        await _db.SaveChangesAsync();

        return Ok(auction);
    }

    [HttpPatch("{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Update(int id, UpdateAuctionRequest req)
    {
        if (!await CanManage(id)) return Forbid();
        var auction = await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == id);
        if (auction == null) return NotFound();

        var presentationFieldsChanged = req.SoldAnimationEnabled.HasValue || req.SoldAnimationStyle != null || req.SoldSoundEnabled.HasValue || req.DrawSoundEnabled.HasValue || req.SelectionDisplayMode != null ||
            req.PublicLivePanelEnabled.HasValue;

        var ruleFieldsChanged = req.MinimumBidAmount.HasValue || req.BidIncrementAmount.HasValue ||
            req.RosterMinSize.HasValue || req.RosterMaxSize.HasValue || req.UnsoldRoundsEnabled.HasValue ||
            req.MaxUnsoldRounds.HasValue || req.AllowReducedBasePriceInUnsold.HasValue ||
            req.CustomUnsoldMinBid.HasValue || req.AllowWheelSelectionFromPool.HasValue || req.MinRemainingPurseRule.HasValue;

        if (ruleFieldsChanged && !_lifecycle.CanEditRules(auction.Status))
        {
            return Conflict(new { error = "Auction rules are locked once the auction is Live or beyond. Use the correction workflow instead.", code = "RULES_LOCKED" });
        }

        var before = new { auction.Name, auction.Status };

        if (req.Name != null) auction.Name = req.Name;
        if (req.SportType != null) auction.SportType = req.SportType;
        if (req.TournamentName != null) auction.TournamentName = req.TournamentName;
        if (req.DateTime.HasValue) auction.DateTime = req.DateTime.Value;
        if (req.VenueOrOnlineLabel != null) auction.VenueOrOnlineLabel = req.VenueOrOnlineLabel;
        if (req.CurrencyLabel != null) auction.CurrencyLabel = req.CurrencyLabel;
        if (req.DefaultTeamBalance.HasValue) auction.DefaultTeamBalance = req.DefaultTeamBalance.Value;
        if (req.MinimumBidAmount.HasValue) auction.MinimumBidAmount = req.MinimumBidAmount.Value;
        if (req.BidIncrementAmount.HasValue) auction.BidIncrementAmount = req.BidIncrementAmount.Value;
        if (req.RosterMinSize.HasValue) auction.RosterMinSize = req.RosterMinSize;
        if (req.RosterMaxSize.HasValue) auction.RosterMaxSize = req.RosterMaxSize;
        if (req.Visibility.HasValue) auction.Visibility = req.Visibility.Value;
        if (req.BiddingMode.HasValue) auction.BiddingMode = req.BiddingMode.Value;

        if (auction.Rules != null)
        {
            if (req.SoldAnimationStyle != null && req.SoldAnimationStyle is not ("Stamp" or "Hammer"))
                return BadRequest(new { error = "Sold animation style must be Stamp or Hammer." });
            if (req.SelectionDisplayMode != null && req.SelectionDisplayMode is not ("Meter" or "Wheel"))
                return BadRequest(new { error = "Player selection display must be Meter or Wheel." });
            if (req.UnsoldRoundsEnabled.HasValue) auction.Rules.UnsoldRoundsEnabled = req.UnsoldRoundsEnabled.Value;
            if (req.MaxUnsoldRounds.HasValue) auction.Rules.MaxUnsoldRounds = req.MaxUnsoldRounds.Value;
            if (req.AllowReducedBasePriceInUnsold.HasValue) auction.Rules.AllowReducedBasePriceInUnsold = req.AllowReducedBasePriceInUnsold.Value;
            if (req.CustomUnsoldMinBid.HasValue) auction.Rules.CustomUnsoldMinBid = req.CustomUnsoldMinBid;
            if (req.AllowWheelSelectionFromPool.HasValue) auction.Rules.AllowWheelSelectionFromPool = req.AllowWheelSelectionFromPool.Value;
            if (req.MinRemainingPurseRule.HasValue) auction.Rules.MinRemainingPurseRule = req.MinRemainingPurseRule;
            if (req.SoldAnimationEnabled.HasValue) auction.Rules.SoldAnimationEnabled = req.SoldAnimationEnabled.Value;
            if (req.SoldAnimationStyle != null) auction.Rules.SoldAnimationStyle = req.SoldAnimationStyle;
            if (req.SoldSoundEnabled.HasValue) auction.Rules.SoldSoundEnabled = req.SoldSoundEnabled.Value;
            if (req.DrawSoundEnabled.HasValue) auction.Rules.DrawSoundEnabled = req.DrawSoundEnabled.Value;
            if (req.SelectionDisplayMode != null) auction.Rules.SelectionDisplayMode = req.SelectionDisplayMode;
            if (req.PublicLivePanelEnabled.HasValue) auction.Rules.PublicLivePanelEnabled = req.PublicLivePanelEnabled.Value;
        }

        _audit.Write("Auction", auction.Id, "Updated", before, new { auction.Name, auction.Status }, null, CurrentUserId);
        await _db.SaveChangesAsync();
        if (presentationFieldsChanged)
        {
            await _broadcaster.BroadcastAsync(id, "auction_settings_changed", new
            {
                soldAnimationEnabled = auction.Rules?.SoldAnimationEnabled ?? true,
                soldAnimationStyle = auction.Rules?.SoldAnimationStyle ?? "Stamp",
                soldSoundEnabled = auction.Rules?.SoldSoundEnabled ?? true,
                drawSoundEnabled = auction.Rules?.DrawSoundEnabled ?? true,
                selectionDisplayMode = auction.Rules?.SelectionDisplayMode ?? "Meter",
                publicLivePanelEnabled = auction.Rules?.PublicLivePanelEnabled ?? false
            });
        }
        return Ok(auction);
    }

    public record SetLogoRequest(string? LogoDataUri);

    // Logo is stored directly in the DB as a data URI rather than on disk/cloud storage -
    // simplest option with no external dependency, so it's capped at ~500KB decoded to keep
    // the SQLite file from bloating (this is a club-logo-sized image, not a photo gallery).
    private const int MaxLogoBytes = 500_000;

    [HttpPost("{id}/logo")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> SetLogo(int id, SetLogoRequest req)
    {
        if (!await CanManage(id)) return Forbid();
        var auction = await _db.Auctions.FindAsync(id);
        if (auction == null) return NotFound();

        if (string.IsNullOrEmpty(req.LogoDataUri))
        {
            auction.LogoDataUri = null;
        }
        else
        {
            if (!req.LogoDataUri.StartsWith("data:image/"))
                return BadRequest(new { error = "Logo must be an image data URI (data:image/...)" });

            var commaIndex = req.LogoDataUri.IndexOf(',');
            var base64Part = commaIndex >= 0 ? req.LogoDataUri[(commaIndex + 1)..] : req.LogoDataUri;
            var approxBytes = (base64Part.Length * 3) / 4;
            if (approxBytes > MaxLogoBytes)
                return BadRequest(new { error = $"Logo image is too large (max {MaxLogoBytes / 1000}KB). Please use a smaller image." });

            auction.LogoDataUri = req.LogoDataUri;
        }

        _audit.Write("Auction", auction.Id, "LogoChanged", null, new { hasLogo = auction.LogoDataUri != null }, null, CurrentUserId);
        await _db.SaveChangesAsync();
        return Ok(new { logoDataUri = auction.LogoDataUri });
    }

    private async Task<IActionResult> Transition(int id, AuctionStatus to, string eventName)
    {
        if (!await CanManage(id)) return Forbid();
        var auction = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == id);
        if (auction == null) return NotFound();

        if (!AuctionLifecycleService.CanTransition(auction.Status, to))
            return Conflict(new { error = $"Cannot transition from {auction.Status} to {to}" });

        var before = auction.Status;
        auction.Status = to;
        _audit.Write("Auction", auction.Id, eventName, new { Status = before }, new { Status = to }, null, CurrentUserId);
        await _db.SaveChangesAsync();

        await _broadcaster.BroadcastAsync(id, "auction_status_changed", new { auctionId = id, status = to.ToString() });
        return Ok(auction);
    }

    [HttpPost("{id}/start")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Start(int id, [FromServices] AuctionLifecycleService lifecycle)
    {
        if (!await CanManage(id)) return Forbid();
        var (ok, error) = await lifecycle.ValidateStartAsync(id);
        if (!ok) return BadRequest(new { error });
        return await Transition(id, AuctionStatus.Live, "Started");
    }

    [HttpPost("{id}/pause")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public Task<IActionResult> Pause(int id) => Transition(id, AuctionStatus.Paused, "Paused");

    [HttpPost("{id}/resume")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public Task<IActionResult> Resume(int id) => Transition(id, AuctionStatus.Live, "Resumed");

    [HttpPost("{id}/complete")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Complete(int id, CompleteAuctionRequest req, [FromServices] AuctionLifecycleService lifecycle)
    {
        if (!await CanManage(id)) return Forbid();
        var (ok, error) = await lifecycle.ValidateCompleteAsync(id, req.ForceConfirm);
        if (!ok) return BadRequest(new { error });
        return await Transition(id, AuctionStatus.Completed, "Completed");
    }

    [HttpPost("{id}/archive")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public Task<IActionResult> Archive(int id) => Transition(id, AuctionStatus.Archived, "Archived");

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await CanManage(id)) return Forbid();

        var (success, error) = await _deletion.DeleteAsync(id);
        if (!success)
        {
            if (error == "Auction not found") return NotFound(new { error });
            return Conflict(new { error });
        }

        await _broadcaster.BroadcastAsync(id, "auction_deleted", new { auctionId = id });
        return Ok(new { message = "Auction permanently deleted" });
    }

    // Destructive: wipes all bids/sales/ledger history and rewinds the auction to Draft.
    // Intended for restarting a test/demo auction from scratch, not for correcting a single mistake
    // (use POST /correction for that — it preserves history and requires a reason per action).
    [HttpPost("{id}/reset")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Reset(int id, [FromServices] AuctionLifecycleService lifecycle)
    {
        if (!await CanManage(id)) return Forbid();
        var (ok, error) = await lifecycle.ResetAuctionAsync(id, CurrentUserId);
        if (!ok) return NotFound(new { error });

        await _broadcaster.BroadcastAsync(id, "auction_status_changed", new { auctionId = id, status = AuctionStatus.Draft.ToString() });
        var auction = await _db.Auctions.FindAsync(id);
        return Ok(auction);
    }

    [HttpPost("{id}/ready")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public Task<IActionResult> Ready(int id) => Transition(id, AuctionStatus.Ready, "MarkedReady");

    public record ReopenAuctionRequest(string Reason);

    // For an auction completed too early (e.g. force-completed with unresolved players).
    // Reopens into UnsoldPoolOpen so spin/sell/mark-unsold work again for whoever's left.
    [HttpPost("{id}/reopen")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Reopen(int id, ReopenAuctionRequest req, [FromServices] AuctionLifecycleService lifecycle)
    {
        if (!await CanManage(id)) return Forbid();
        var (ok, error) = await lifecycle.ReopenAuctionAsync(id, req.Reason, CurrentUserId);
        if (!ok) return BadRequest(new { error });

        await _broadcaster.BroadcastAsync(id, "auction_status_changed", new { auctionId = id, status = AuctionStatus.UnsoldPoolOpen.ToString() });
        var auction = await _db.Auctions.FindAsync(id);
        return Ok(auction);
    }
}
