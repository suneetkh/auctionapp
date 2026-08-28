using System.Text.Json.Serialization;

namespace App.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuctionUserAccess
{
    public int UserId { get; set; }
    public int AuctionId { get; set; }
}

public class AuctionPlanningState
{
    public int AuctionId { get; set; }
    public string? DrawStateJson { get; set; }
    public string? FixtureStateJson { get; set; }
    public bool DrawLocked { get; set; }
    public bool FixturesLocked { get; set; }
}

public class Auction
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SportType { get; set; } = "Cricket";
    public string TournamentName { get; set; } = "";
    public DateTime DateTime { get; set; } = DateTime.UtcNow;
    public string VenueOrOnlineLabel { get; set; } = "";
    public string CurrencyLabel { get; set; } = "Points";
    public decimal DefaultTeamBalance { get; set; } = 10000;
    public decimal MinimumBidAmount { get; set; } = 100;
    public decimal BidIncrementAmount { get; set; } = 50;
    public int? RosterMinSize { get; set; } = 5;
    public int? RosterMaxSize { get; set; } = 15;
    public AuctionVisibility Visibility { get; set; } = AuctionVisibility.Private;
    public BiddingMode BiddingMode { get; set; } = BiddingMode.AdminControlled;
    public AuctionStatus Status { get; set; } = AuctionStatus.Draft;
    public int CurrentRound { get; set; } = 1;
    public bool SelectionRevealPending { get; set; }
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Data URI (e.g. "data:image/png;base64,...") stored directly in the DB - simplest option
    // for a club-logo-sized image with no external storage dependency. Size-capped server-side.
    public string? LogoDataUri { get; set; }

    public AuctionRules? Rules { get; set; }
    public List<Team> Teams { get; set; } = new();
    public List<Player> Players { get; set; } = new();
}

public class AuctionRules
{
    public int AuctionId { get; set; }
    public bool UnsoldRoundsEnabled { get; set; } = true;
    public int MaxUnsoldRounds { get; set; } = 2;
    public bool AllowReducedBasePriceInUnsold { get; set; } = true;
    public decimal? CustomUnsoldMinBid { get; set; }
    public bool AllowWheelSelectionFromPool { get; set; } = true;
    public decimal? MinRemainingPurseRule { get; set; }
    public bool SoldAnimationEnabled { get; set; } = true;
    public string SoldAnimationStyle { get; set; } = "Stamp";
    public bool SoldSoundEnabled { get; set; } = true;
    public bool DrawSoundEnabled { get; set; } = true;
    public string SelectionDisplayMode { get; set; } = "Meter";
    public bool PublicLivePanelEnabled { get; set; } = false;
    public string? CategoryLimitsJson { get; set; }

    [JsonIgnore]
    public Auction? Auction { get; set; }
}

public class Team
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? TeamColor { get; set; }
    public int? OwnerUserId { get; set; }
    public string? ContactInfo { get; set; }
    public string? Notes { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public Auction? Auction { get; set; }
    [JsonIgnore]
    public List<TeamLedgerEntry> LedgerEntries { get; set; } = new();
}

public class Player
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public string Name { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? AgeOrDob { get; set; }
    public string Role { get; set; } = "";
    public string? SkillTags { get; set; }
    public decimal BasePrice { get; set; }
    public decimal? MinimumBidOverride { get; set; }
    public string? Notes { get; set; }
    public string? ContactInfo { get; set; }
    public PlayerStatus Status { get; set; } = PlayerStatus.Available;
    public int? TeamId { get; set; }
    public decimal? SalePrice { get; set; }
    public int? SoldRound { get; set; }
    public bool IsCaptain { get; set; }
    public decimal? CaptainCost { get; set; }

    [JsonIgnore]
    public Auction? Auction { get; set; }
}

public class Bid
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public decimal Amount { get; set; }
    public BidSource BidSource { get; set; }
    public int PlacedByUserId { get; set; }
    public int RoundNumber { get; set; }
    public bool IsValid { get; set; } = true;
    public string? InvalidReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Sale
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public decimal FinalAmount { get; set; }
    public int RoundNumber { get; set; }
    public int ConfirmedByUserId { get; set; }
    public SaleStatus SaleStatus { get; set; } = SaleStatus.Confirmed;
    public DateTime? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TeamLedgerEntry
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public LedgerTransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public int? RelatedPlayerId { get; set; }
    public int? RelatedSaleId { get; set; }
    public string? Reason { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Sponsor
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public string? Name { get; set; }
    // Same storage approach as Auction.LogoDataUri - base64 data URI directly in the DB,
    // size-capped server-side. No external storage dependency for this pilot.
    public string LogoDataUri { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuctionEvent
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public string EventType { get; set; } = "";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public int? PerformedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
