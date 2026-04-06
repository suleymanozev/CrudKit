namespace CrudKit.EntityFrameworkCore.Models;

/// <summary>One row per entity change, written by CrudKitDbContext for [Audited] entities.</summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;   // Create | Update | Delete
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedFields { get; set; }
}
