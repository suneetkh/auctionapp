using App.Api.Models;
using App.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Data;

public static class SeedData
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return; // already seeded

        var superAdmin = new User
        {
            Email = "admin@auction.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = UserRole.SuperAdmin,
            DisplayName = "Super Admin"
        };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();

        var auction = new Auction
        {
            Name = "Demo Premier Cricket Auction 2026",
            SportType = "Cricket",
            TournamentName = "Demo Premier League",
            DateTime = DateTime.UtcNow.AddDays(1),
            VenueOrOnlineLabel = "Online",
            CurrencyLabel = "₹",
            DefaultTeamBalance = 10_000_000,
            MinimumBidAmount = 100_000,
            BidIncrementAmount = 50_000,
            RosterMinSize = 11,
            RosterMaxSize = 18,
            Visibility = AuctionVisibility.PublicViewOnly,
            BiddingMode = BiddingMode.AdminControlled,
            Status = AuctionStatus.Draft,
            OwnerUserId = superAdmin.Id
        };
        db.Auctions.Add(auction);
        await db.SaveChangesAsync();

        db.AuctionRules.Add(new AuctionRules
        {
            AuctionId = auction.Id,
            UnsoldRoundsEnabled = true,
            MaxUnsoldRounds = 2,
            AllowReducedBasePriceInUnsold = true,
            AllowWheelSelectionFromPool = true,
            MinRemainingPurseRule = 200_000
        });
        await db.SaveChangesAsync();

        var teamNames = new[] { "Mumbai Mavericks", "Delhi Dragons", "Chennai Chargers", "Kolkata Knights", "Bangalore Blazers", "Punjab Panthers" };
        var teams = new List<Team>();
        foreach (var name in teamNames)
        {
            var team = new Team
            {
                AuctionId = auction.Id,
                Name = name,
                OpeningBalance = 10_000_000,
                IsActive = true
            };
            teams.Add(team);
            db.Teams.Add(team);
        }
        await db.SaveChangesAsync();

        var ledger = new TeamLedgerService(db);
        foreach (var t in teams) ledger.EnsureOpeningBalance(t);
        await db.SaveChangesAsync();

        var roles = new[] { "Batter", "Bowler", "All-Rounder", "Wicket-Keeper" };
        var firstNames = new[] { "Raj", "Vikram", "Aditya", "Rohan", "Karan", "Arjun", "Sanjay", "Anil", "Deepak", "Manish",
            "Suresh", "Ravi", "Amit", "Vijay", "Kunal", "Nitin", "Gaurav", "Rahul", "Ajay", "Sameer",
            "Harsh", "Yash", "Varun", "Dev", "Aryan", "Ishaan", "Kabir", "Om", "Reyansh", "Vivaan" };
        var rng = new Random(42);
        for (int i = 0; i < 28; i++)
        {
            var role = roles[rng.Next(roles.Length)];
            var basePrice = new[] { 200_000m, 500_000m, 1_000_000m, 2_000_000m }[rng.Next(4)];
            db.Players.Add(new Player
            {
                AuctionId = auction.Id,
                Name = $"{firstNames[i]} {(char)('A' + rng.Next(26))}.",
                Role = role,
                SkillTags = role == "Bowler" ? "Pace" : role == "Batter" ? "Top-order" : "Utility",
                BasePrice = basePrice,
                Status = PlayerStatus.Available
            });
        }
        await db.SaveChangesAsync();
    }
}
