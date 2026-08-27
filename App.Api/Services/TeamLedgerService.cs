using App.Api.Data;
using App.Api.Models;

namespace App.Api.Services;

public class TeamLedgerService
{
    private readonly AppDbContext _db;
    public TeamLedgerService(AppDbContext db) { _db = db; }

    public decimal GetAvailableBalance(int teamId)
    {
        var last = _db.TeamLedgerEntries
            .Where(l => l.TeamId == teamId)
            .OrderByDescending(l => l.Id)
            .FirstOrDefault();
        if (last != null) return last.BalanceAfter;

        var team = _db.Teams.Find(teamId);
        return team?.OpeningBalance ?? 0;
    }

    /// <summary>Writes a ledger entry atomically. Does NOT call SaveChanges - caller controls the transaction.</summary>
    public TeamLedgerEntry AddEntry(int teamId, LedgerTransactionType type, decimal amount, string? reason,
        int? relatedPlayerId = null, int? relatedSaleId = null, int? createdByUserId = null)
    {
        // For the very first (OpeningBalance) entry there is no prior ledger history,
        // so the balance-before must be zero rather than falling back to the team's
        // OpeningBalance column (which would double-count it in balance-after).
        var hasPriorEntries = _db.TeamLedgerEntries.Any(l => l.TeamId == teamId);
        var balanceBefore = hasPriorEntries ? GetAvailableBalance(teamId) : 0;
        var balanceAfter = balanceBefore + amount;
        var entry = new TeamLedgerEntry
        {
            TeamId = teamId,
            TransactionType = type,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            RelatedPlayerId = relatedPlayerId,
            RelatedSaleId = relatedSaleId,
            Reason = reason,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.TeamLedgerEntries.Add(entry);
        return entry;
    }

    public void EnsureOpeningBalance(Team team)
    {
        var hasEntries = _db.TeamLedgerEntries.Any(l => l.TeamId == team.Id);
        if (!hasEntries)
        {
            AddEntry(team.Id, LedgerTransactionType.OpeningBalance, team.OpeningBalance, "Opening balance");
        }
    }
}
