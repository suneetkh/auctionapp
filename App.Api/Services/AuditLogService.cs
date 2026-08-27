using System.Text.Json;
using App.Api.Data;
using App.Api.Models;

namespace App.Api.Services;

public class AuditLogService
{
    private readonly AppDbContext _db;
    public AuditLogService(AppDbContext db) { _db = db; }

    public AuditLog Write(string entityType, int entityId, string action, object? before, object? after,
        string? reason, int? performedByUserId, string? ipAddress = null)
    {
        var log = new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            BeforeJson = before == null ? null : JsonSerializer.Serialize(before),
            AfterJson = after == null ? null : JsonSerializer.Serialize(after),
            Reason = reason,
            PerformedByUserId = performedByUserId,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        };
        _db.AuditLogs.Add(log);
        return log;
    }
}
