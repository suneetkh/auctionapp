using App.Api.Models;

namespace App.Api.DTOs;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Email, string Role, string DisplayName, int UserId);

public record CreateAuctionRequest(
    string Name, string SportType, string TournamentName, DateTime DateTime, string VenueOrOnlineLabel,
    string CurrencyLabel, decimal DefaultTeamBalance, decimal MinimumBidAmount, decimal BidIncrementAmount,
    int? RosterMinSize, int? RosterMaxSize, AuctionVisibility Visibility, BiddingMode BiddingMode,
    bool UnsoldRoundsEnabled, int MaxUnsoldRounds, bool AllowReducedBasePriceInUnsold,
    decimal? CustomUnsoldMinBid, bool AllowWheelSelectionFromPool, decimal? MinRemainingPurseRule);

public record UpdateAuctionRequest(
    string? Name, string? SportType, string? TournamentName, DateTime? DateTime, string? VenueOrOnlineLabel,
    string? CurrencyLabel, decimal? DefaultTeamBalance, decimal? MinimumBidAmount, decimal? BidIncrementAmount,
    int? RosterMinSize, int? RosterMaxSize, AuctionVisibility? Visibility, BiddingMode? BiddingMode,
    bool? UnsoldRoundsEnabled, int? MaxUnsoldRounds, bool? AllowReducedBasePriceInUnsold,
    decimal? CustomUnsoldMinBid, bool? AllowWheelSelectionFromPool, decimal? MinRemainingPurseRule,
    bool? SoldAnimationEnabled, string? SoldAnimationStyle, bool? SoldSoundEnabled, bool? DrawSoundEnabled, string? SelectionDisplayMode, bool? PublicLivePanelEnabled);

public record CreateTeamRequest(string Name, string? LogoUrl, string? TeamColor, int? OwnerUserId, string? ContactInfo, string? Notes, decimal? OpeningBalance);
public record UpdateTeamRequest(string? Name, string? LogoUrl, string? TeamColor, int? OwnerUserId, string? ContactInfo, string? Notes, decimal? OpeningBalance, bool? IsActive);

public record CreatePlayerRequest(string Name, string? PhotoUrl, string? AgeOrDob, string Role, string? SkillTags,
    decimal BasePrice, decimal? MinimumBidOverride, string? Notes, string? ContactInfo,
    bool IsCaptain = false, int? CaptainTeamId = null, decimal? CaptainCost = null);
public record UpdatePlayerRequest(string? Name, string? PhotoUrl, string? AgeOrDob, string? Role, string? SkillTags,
    decimal? BasePrice, decimal? MinimumBidOverride, string? Notes, string? ContactInfo, PlayerStatus? Status);
public record SetCaptainRequest(bool IsCaptain, int? TeamId, decimal? Cost);

public record PlaceBidRequest(int PlayerId, int TeamId, decimal Amount);
public record SellRequest(int PlayerId, int TeamId, decimal Amount);
public record MarkUnsoldRequest(int PlayerId);
public record CompleteAuctionRequest(bool ForceConfirm);

public record ReverseSaleRequest(int SaleId, string Reason);
public record AdjustBalanceRequest(int TeamId, decimal Amount, string Reason);
public record ReassignPlayerRequest(int PlayerId, int NewTeamId, decimal NewAmount, string Reason);
public record CorrectionRequest(string Type, int? SaleId, int? TeamId, decimal? Amount, int? PlayerId, int? NewTeamId, decimal? NewAmount, string Reason, decimal? NewMinRemainingPurseRule = null);

public record CreateSponsorRequest(string? Name, string LogoDataUri);
