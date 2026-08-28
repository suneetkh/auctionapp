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
[Route("api/auctions/{auctionId}")]
[Authorize]
public class LiveAuctionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BidValidationService _bidValidation;
    private readonly TeamLedgerService _ledger;
    private readonly SaleService _sale;
    private readonly WheelSelectionService _wheel;
    private readonly UnsoldPoolService _unsoldPool;
    private readonly CorrectionService _correction;
    private readonly AuditLogService _audit;
    private readonly IAuctionBroadcaster _broadcaster;

    public LiveAuctionController(AppDbContext db, BidValidationService bidValidation, TeamLedgerService ledger,
        SaleService sale, WheelSelectionService wheel, UnsoldPoolService unsoldPool, CorrectionService correction,
        AuditLogService audit, IAuctionBroadcaster broadcaster)
    {
        _db = db;
        _bidValidation = bidValidation;
        _ledger = ledger;
        _sale = sale;
        _wheel = wheel;
        _unsoldPool = unsoldPool;
        _correction = correction;
        _audit = audit;
        _broadcaster = broadcaster;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    private async Task<Auction?> LoadAuction(int auctionId) =>
        await _db.Auctions.Include(a => a.Rules).FirstOrDefaultAsync(a => a.Id == auctionId);

    private async Task<bool> CanOperate(int auctionId)
    {
        if (CurrentRole == "SuperAdmin") return true;
        if (CurrentRole != "AuctionAdmin") return false;
        var auction = await _db.Auctions.FindAsync(auctionId);
        return auction != null && (auction.OwnerUserId == CurrentUserId ||
            await _db.AuctionUserAccess.AnyAsync(x => x.AuctionId == auctionId && x.UserId == CurrentUserId));
    }

    [HttpPost("spin")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Spin(int auctionId)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return BadRequest(new { error = "Auction must be Live or in UnsoldPoolOpen state to spin" });

        var alreadySelected = await _db.Players.AnyAsync(p => p.AuctionId == auctionId &&
            (p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));
        if (alreadySelected)
            return Conflict(new { error = "A player is already selected and awaiting Sold/Unsold. Resolve it before spinning again." });

        var eligibleStatus = auction.Status == AuctionStatus.UnsoldPoolOpen
            ? PlayerStatus.ReauctionAvailable
            : PlayerStatus.Available;
        if (!await _db.Players.AnyAsync(p => p.AuctionId == auctionId && p.Status == eligibleStatus))
            return BadRequest(new { error = "No eligible players remain to select" });

        // Keep the selected player private until the operator's wheel/meter has actually stopped.
        // Persisting this flag also prevents a public-display refresh from revealing the result early.
        auction.SelectionRevealPending = true;
        await _db.SaveChangesAsync();
        await _broadcaster.BroadcastAsync(auctionId, "player_selecting", new { });

        try
        {
            var player = await _wheel.SpinAndSelectAsync(auction, CurrentUserId);
            if (player != null) return Ok(player);

            auction.SelectionRevealPending = false;
            await _db.SaveChangesAsync();
            await _broadcaster.BroadcastAsync(auctionId, "player_selection_cancelled", new { });
            return BadRequest(new { error = "No eligible players remain to select" });
        }
        catch
        {
            auction.SelectionRevealPending = false;
            await _db.SaveChangesAsync();
            await _broadcaster.BroadcastAsync(auctionId, "player_selection_cancelled", new { });
            throw;
        }
    }

    [HttpPost("finalize-selection")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> FinalizeSelection(int auctionId)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.AuctionId == auctionId &&
            (p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));
        if (player == null) return Conflict(new { error = "There is no selected player to reveal" });

        auction.SelectionRevealPending = false;
        await _db.SaveChangesAsync();
        await _broadcaster.BroadcastAsync(auctionId, "player_selected", new { player });
        return Ok(player);
    }

    public record SelectPlayerRequest(int PlayerId);

    [HttpPost("select-player")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> SelectPlayer(int auctionId, SelectPlayerRequest req)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return BadRequest(new { error = "Auction must be Live or in UnsoldPoolOpen state" });

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == req.PlayerId && p.AuctionId == auctionId);
        if (player == null) return NotFound(new { error = "Player not found" });

        var eligible = auction.Status == AuctionStatus.UnsoldPoolOpen
            ? PlayerStatus.ReauctionAvailable : PlayerStatus.Available;
        if (player.Status != eligible)
            return Conflict(new { error = "Player is not eligible for selection (already processed or not in the correct pool)" });

        var alreadySelected = await _db.Players.AnyAsync(p => p.AuctionId == auctionId && p.Id != player.Id &&
            (p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));
        if (alreadySelected)
            return Conflict(new { error = "Another player is already selected and awaiting Sold/Unsold. Resolve it before selecting a new one." });

        player.Status = PlayerStatus.Selected;
        _audit.Write("Player", player.Id, "Selected", null, new { player.Status }, "Manual selection", CurrentUserId);
        await _db.SaveChangesAsync();

        await _broadcaster.BroadcastAsync(auctionId, "player_selected", new { player });
        return Ok(player);
    }

    [HttpPost("bids")]
    public async Task<IActionResult> PlaceBid(int auctionId, PlaceBidRequest req)
    {
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();

        // TeamOwner may only bid for their own team; AuctionAdmin/SuperAdmin may bid on behalf of any team
        if (CurrentRole == "TeamOwner")
        {
            var ownedTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == req.TeamId && t.OwnerUserId == CurrentUserId);
            if (ownedTeam == null) return Forbid();
            if (auction.BiddingMode == BiddingMode.AdminControlled)
                return Forbid();
        }
        else if (CurrentRole != "AuctionAdmin" && CurrentRole != "SuperAdmin")
        {
            return Forbid();
        }
        else if (!await CanOperate(auctionId))
        {
            return Forbid();
        }

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == req.PlayerId && p.AuctionId == auctionId);
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == req.TeamId && t.AuctionId == auctionId);
        if (player == null || team == null) return NotFound();

        var result = _bidValidation.Validate(auction, player, team, req.Amount);

        var bid = new Bid
        {
            AuctionId = auctionId,
            PlayerId = req.PlayerId,
            TeamId = req.TeamId,
            Amount = req.Amount,
            BidSource = CurrentRole == "TeamOwner" ? BidSource.TeamOwner : BidSource.Admin,
            PlacedByUserId = CurrentUserId,
            RoundNumber = auction.CurrentRound,
            IsValid = result.IsValid,
            InvalidReason = result.Reason
        };
        _db.Bids.Add(bid);

        if (result.IsValid && player.Status == PlayerStatus.Selected)
        {
            player.Status = PlayerStatus.Bidding;
        }

        await _db.SaveChangesAsync();

        if (result.IsValid)
        {
            await _broadcaster.BroadcastAsync(auctionId, "bid_placed", new { bid, player, availableBalance = _ledger.GetAvailableBalance(team.Id) });
            return Ok(bid);
        }
        else
        {
            await _broadcaster.BroadcastAsync(auctionId, "bid_rejected", new { bid });
            return BadRequest(new { error = result.Reason, bid });
        }
    }

    [HttpPost("bids/undo-last")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> UndoLastBid(int auctionId)
    {
        if (!await CanOperate(auctionId)) return Forbid();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.AuctionId == auctionId &&
            (p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));
        if (player == null)
            return Conflict(new { error = "There is no current player with a bid to undo" });

        var bid = await _db.Bids
            .Where(b => b.AuctionId == auctionId && b.PlayerId == player.Id && b.IsValid)
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();
        if (bid == null)
            return Conflict(new { error = "There is no valid bid to undo" });

        var before = new { bid.IsValid, bid.InvalidReason, player.Status };
        bid.IsValid = false;
        bid.InvalidReason = "Undone by auction operator";

        var hasPreviousValidBid = await _db.Bids.AnyAsync(b =>
            b.AuctionId == auctionId && b.PlayerId == player.Id && b.IsValid && b.Id != bid.Id);
        player.Status = hasPreviousValidBid ? PlayerStatus.Bidding : PlayerStatus.Selected;

        _audit.Write("Bid", bid.Id, "Undone", before,
            new { bid.IsValid, bid.InvalidReason, player.Status }, "Undo via live auction console", CurrentUserId);
        _db.AuctionEvents.Add(new AuctionEvent
        {
            AuctionId = auctionId,
            EventType = "bid_undone",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { bidId = bid.Id, playerId = player.Id })
        });
        await _db.SaveChangesAsync();

        await _broadcaster.BroadcastAsync(auctionId, "bid_undone", new { bid, player });
        return Ok(new { bid, player });
    }

    [HttpPost("sell")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Sell(int auctionId, SellRequest req)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var result = await _sale.SellPlayerAsync(auctionId, req.PlayerId, req.TeamId, req.Amount, CurrentUserId);
        if (!result.Success) return BadRequest(new { error = result.Error });

        await _broadcaster.BroadcastAsync(auctionId, "player_sold", new { player = result.Player, sale = result.Sale });
        await _broadcaster.BroadcastAsync(auctionId, "balance_updated", new { teamId = req.TeamId, availableBalance = _ledger.GetAvailableBalance(req.TeamId) });

        await CheckMainRoundComplete(auctionId);
        return Ok(new { sale = result.Sale, player = result.Player });
    }

    [HttpPost("mark-unsold")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> MarkUnsold(int auctionId, MarkUnsoldRequest req)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var result = await _sale.MarkUnsoldAsync(auctionId, req.PlayerId, CurrentUserId);
        if (!result.Success) return BadRequest(new { error = result.Error });

        await _broadcaster.BroadcastAsync(auctionId, "player_unsold", new { player = result.Player });
        await CheckMainRoundComplete(auctionId);
        return Ok(new { player = result.Player });
    }

    private async Task CheckMainRoundComplete(int auctionId)
    {
        var auction = await _db.Auctions.FindAsync(auctionId);
        if (auction == null) return;
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen) return;

        if (await _unsoldPool.IsMainRoundCompleteAsync(auctionId))
        {
            var before = new { auction.Status };
            auction.Status = AuctionStatus.MainRoundComplete;
            _audit.Write("Auction", auctionId, "MainRoundAutoCompleted", before, new { auction.Status },
                "All players processed (Sold/Unsold/Withdrawn/FinalUnsold)", null);
            await _db.SaveChangesAsync();
            await _broadcaster.BroadcastAsync(auctionId, "auction_status_changed", new { auctionId, status = auction.Status.ToString() });
        }
    }

    [HttpPost("open-unsold-pool")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> OpenUnsoldPool(int auctionId)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();

        var (ok, error) = await _unsoldPool.OpenUnsoldPoolAsync(auction, CurrentUserId);
        if (!ok) return BadRequest(new { error });

        await _broadcaster.BroadcastAsync(auctionId, "unsold_pool_opened", new { auctionId, round = auction.CurrentRound });
        await _broadcaster.BroadcastAsync(auctionId, "auction_status_changed", new { auctionId, status = auction.Status.ToString() });
        return Ok(auction);
    }

    [HttpPost("next-round")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> NextRound(int auctionId)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();

        var rules = auction.Rules;
        var maxRounds = rules?.MaxUnsoldRounds ?? 1;
        if (auction.CurrentRound >= maxRounds + 1)
        {
            await _unsoldPool.FinalizeRemainingUnsoldAsync(auctionId, CurrentUserId);
            auction.Status = AuctionStatus.MainRoundComplete;
            await _db.SaveChangesAsync();
            await _broadcaster.BroadcastAsync(auctionId, "auction_status_changed", new { auctionId, status = auction.Status.ToString() });
            return Ok(new { message = "Final unsold round reached; remaining players marked FinalUnsold", auction });
        }

        var (ok, error) = await _unsoldPool.OpenUnsoldPoolAsync(auction, CurrentUserId);
        if (!ok) return BadRequest(new { error });

        await _broadcaster.BroadcastAsync(auctionId, "unsold_pool_opened", new { auctionId, round = auction.CurrentRound });
        return Ok(auction);
    }

    [HttpPost("correction")]
    [Authorize(Roles = "SuperAdmin,AuctionAdmin")]
    public async Task<IActionResult> Correction(int auctionId, CorrectionRequest req)
    {
        if (!await CanOperate(auctionId)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { error = "Reason is required for all corrections" });

        (bool Success, string? Error) result;
        switch (req.Type.ToLowerInvariant())
        {
            case "reverse_sale":
                if (!req.SaleId.HasValue) return BadRequest(new { error = "SaleId required" });
                result = await _correction.ReverseSaleAsync(req.SaleId.Value, req.Reason, CurrentUserId);
                break;
            case "adjust_balance":
                if (!req.TeamId.HasValue || !req.Amount.HasValue) return BadRequest(new { error = "TeamId and Amount required" });
                result = await _correction.AdjustBalanceAsync(req.TeamId.Value, req.Amount.Value, req.Reason, CurrentUserId);
                break;
            case "reassign_player":
                if (!req.PlayerId.HasValue || !req.NewTeamId.HasValue || !req.NewAmount.HasValue)
                    return BadRequest(new { error = "PlayerId, NewTeamId, NewAmount required" });
                result = await _correction.ReassignPlayerAsync(req.PlayerId.Value, req.NewTeamId.Value, req.NewAmount.Value, req.Reason, CurrentUserId);
                break;
            case "adjust_min_purse_rule":
                result = await _correction.AdjustMinRemainingPurseRuleAsync(auctionId, req.NewMinRemainingPurseRule, req.Reason, CurrentUserId);
                break;
            default:
                return BadRequest(new { error = "Unknown correction type" });
        }

        if (!result.Success) return BadRequest(new { error = result.Error });

        await _broadcaster.BroadcastAsync(auctionId, "correction_applied", new { type = req.Type, reason = req.Reason });
        return Ok(new { message = "Correction applied" });
    }

    [HttpGet("state")]
    [AllowAnonymous]
    public async Task<IActionResult> GetState(int auctionId, [FromQuery] bool publicView = false)
    {
        var auction = await LoadAuction(auctionId);
        if (auction == null) return NotFound();

        var currentPlayer = await _db.Players.FirstOrDefaultAsync(p => p.AuctionId == auctionId &&
            (p.Status == PlayerStatus.Selected || p.Status == PlayerStatus.Bidding));

        if (publicView && auction.SelectionRevealPending)
            currentPlayer = null;

        var recentBids = currentPlayer != null
            ? await _db.Bids.Where(b => b.PlayerId == currentPlayer.Id).OrderByDescending(b => b.Id).Take(20).ToListAsync()
            : new List<Bid>();

        var recentSales = await _db.Sales.Where(s => s.AuctionId == auctionId && s.SaleStatus == SaleStatus.Confirmed)
            .OrderByDescending(s => s.Id).Take(10).ToListAsync();

        var unsoldCount = await _db.Players.CountAsync(p => p.AuctionId == auctionId && p.Status == PlayerStatus.Unsold);

        return Ok(new { auction, currentPlayer, recentBids, recentSales, unsoldCount });
    }
}
