namespace App.Api.Models;

public enum UserRole { SuperAdmin, AuctionAdmin, TeamOwner, PublicViewer }

public enum AuctionStatus { Draft, Ready, Live, Paused, MainRoundComplete, UnsoldPoolOpen, Completed, Archived }

public enum AuctionVisibility { Private, PublicViewOnly, TeamLoginOnly }

public enum BiddingMode { AdminControlled, TeamOwnerLive, Hybrid }

public enum PlayerStatus { Available, Selected, Bidding, Sold, Unsold, ReauctionAvailable, Withdrawn, FinalUnsold }

public enum BidSource { Admin, TeamOwner }

public enum SaleStatus { Confirmed, Reversed }

public enum LedgerTransactionType { OpeningBalance, Purchase, Reversal, ManualAdjustment, CaptainAssignment, CaptainReversal }
