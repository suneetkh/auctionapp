using App.Api.Data;
using App.Api.Models;

namespace App.Api.Services;

public class BidValidationResult
{
    public bool IsValid { get; set; }
    public string? Reason { get; set; }
    public static BidValidationResult Ok() => new() { IsValid = true };
    public static BidValidationResult Fail(string reason) => new() { IsValid = false, Reason = reason };
}

public static class BidIncrementRule
{
    public static decimal AlignUp(decimal amount, decimal increment)
    {
        if (increment <= 0) return amount;
        var remainder = amount % increment;
        return remainder == 0 ? amount : amount + increment - remainder;
    }

    public static string? Validate(decimal amount, decimal baseline, decimal increment, bool requireIncrease, string label)
    {
        if (increment <= 0)
            return "Bid increment must be greater than zero";

        var minimum = requireIncrease ? baseline + increment : baseline;
        if (amount < minimum)
            return requireIncrease
                ? $"{label} must be at least {minimum} (current highest {baseline} + increment {increment})"
                : $"{label} must be at least {minimum}";

        if ((amount - baseline) % increment != 0)
            return $"{label} must be {baseline} plus a whole multiple of the bid increment ({increment})";

        return null;
    }
}

public static class TeamBidCapacityRule
{
    public static decimal ReservePerRequiredSlot(Auction auction) =>
        Math.Max(auction.MinimumBidAmount, auction.Rules?.MinRemainingPurseRule ?? 0);

    public static decimal CalculateMaximumBid(Auction auction, int currentRosterSize, decimal availableBalance)
    {
        var requiredRosterSize = auction.RosterMinSize ?? 0;
        var slotsRemainingAfterThisPurchase = Math.Max(0, requiredRosterSize - (currentRosterSize + 1));
        var requiredReserve = slotsRemainingAfterThisPurchase * ReservePerRequiredSlot(auction);
        return Math.Max(0, availableBalance - requiredReserve);
    }
}

public class BidValidationService
{
    private readonly AppDbContext _db;
    private readonly TeamLedgerService _ledger;

    public BidValidationService(AppDbContext db, TeamLedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public BidValidationResult Validate(Auction auction, Player player, Team team, decimal amount)
    {
        if (auction.Status != AuctionStatus.Live && auction.Status != AuctionStatus.UnsoldPoolOpen)
            return BidValidationResult.Fail("Auction is not in a biddable state");

        if (player.Status != PlayerStatus.Selected && player.Status != PlayerStatus.Bidding)
            return BidValidationResult.Fail("Player is not currently up for bidding");

        if (!team.IsActive)
            return BidValidationResult.Fail("Team is not active");

        // Materialize first: SQLite stores decimal as TEXT, so ORDER BY on decimal
        // must happen client-side rather than being translated to SQL.
        var highestBid = _db.Bids
            .Where(b => b.PlayerId == player.Id && b.IsValid)
            .ToList()
            .OrderByDescending(b => b.Amount)
            .FirstOrDefault();

        if (highestBid == null)
        {
            var floor = player.MinimumBidOverride ?? player.BasePrice;
            var baseline = BidIncrementRule.AlignUp(Math.Max(floor, auction.MinimumBidAmount), auction.BidIncrementAmount);
            var incrementError = BidIncrementRule.Validate(amount, baseline, auction.BidIncrementAmount,
                requireIncrease: false, label: "First bid");
            if (incrementError != null)
                return BidValidationResult.Fail(incrementError);
        }
        else
        {
            if (highestBid.TeamId == team.Id)
                return BidValidationResult.Fail("Team is already the highest bidder");

            var incrementError = BidIncrementRule.Validate(amount, highestBid.Amount, auction.BidIncrementAmount,
                requireIncrease: true, label: "Bid");
            if (incrementError != null)
                return BidValidationResult.Fail(incrementError);
        }

        var availableBalance = _ledger.GetAvailableBalance(team.Id);
        if (amount > availableBalance)
            return BidValidationResult.Fail("Bid exceeds team's available balance");

        // Roster size and min remaining purse checks
        var rules = auction.Rules;
        var currentRosterSize = _db.Players.Count(p => p.TeamId == team.Id && (p.Status == PlayerStatus.Sold || p.IsCaptain));

        var maximumBid = TeamBidCapacityRule.CalculateMaximumBid(auction, currentRosterSize, availableBalance);
        if (amount > maximumBid)
            return BidValidationResult.Fail($"Maximum bid for {team.Name} is {maximumBid:0.##}. Enough purse must remain to complete the minimum roster.");

        if (rules != null)
        {
            if (auction.RosterMaxSize.HasValue && currentRosterSize >= auction.RosterMaxSize.Value)
                return BidValidationResult.Fail("Team has reached its maximum roster size");

        }
        else if (auction.RosterMaxSize.HasValue && currentRosterSize >= auction.RosterMaxSize.Value)
        {
            return BidValidationResult.Fail("Team has reached its maximum roster size");
        }

        return BidValidationResult.Ok();
    }
}
