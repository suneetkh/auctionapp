using App.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuctionUserAccess> AuctionUserAccess => Set<AuctionUserAccess>();
    public DbSet<AuctionPlanningState> AuctionPlanningStates => Set<AuctionPlanningState>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionRules> AuctionRules => Set<AuctionRules>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<TeamLedgerEntry> TeamLedgerEntries => Set<TeamLedgerEntry>();
    public DbSet<AuctionEvent> AuctionEvents => Set<AuctionEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Sponsor> Sponsors => Set<Sponsor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<AuctionUserAccess>().HasKey(x => new { x.UserId, x.AuctionId });
        modelBuilder.Entity<AuctionUserAccess>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AuctionUserAccess>().HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AuctionPlanningState>().HasKey(x => x.AuctionId);
        modelBuilder.Entity<AuctionPlanningState>().HasOne<Auction>().WithOne().HasForeignKey<AuctionPlanningState>(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuctionRules>().HasKey(r => r.AuctionId);
        modelBuilder.Entity<AuctionRules>()
            .HasOne(r => r.Auction)
            .WithOne(a => a.Rules)
            .HasForeignKey<AuctionRules>(r => r.AuctionId);

        modelBuilder.Entity<Team>()
            .HasOne(t => t.Auction)
            .WithMany(a => a.Teams)
            .HasForeignKey(t => t.AuctionId);

        modelBuilder.Entity<Player>()
            .HasOne(p => p.Auction)
            .WithMany(a => a.Players)
            .HasForeignKey(p => p.AuctionId);

        modelBuilder.Entity<Auction>().Property(a => a.DefaultTeamBalance).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Auction>().Property(a => a.MinimumBidAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Auction>().Property(a => a.BidIncrementAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Team>().Property(t => t.OpeningBalance).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Player>().Property(p => p.BasePrice).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Player>().Property(p => p.MinimumBidOverride).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Player>().Property(p => p.SalePrice).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Player>().Property(p => p.CaptainCost).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Bid>().Property(b => b.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Sale>().Property(s => s.FinalAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<TeamLedgerEntry>().Property(l => l.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<TeamLedgerEntry>().Property(l => l.BalanceBefore).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<TeamLedgerEntry>().Property(l => l.BalanceAfter).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<AuctionRules>().Property(r => r.MinRemainingPurseRule).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<AuctionRules>().Property(r => r.CustomUnsoldMinBid).HasColumnType("decimal(18,2)");
    }
}
