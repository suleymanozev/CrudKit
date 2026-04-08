namespace CrudKit.Core.Models;

/// <summary>
/// Represents a single audit trail entry. Passed to <see cref="CrudKit.Core.Interfaces.IAuditWriter"/>.
/// </summary>
public class AuditEntry
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>"Create", "Update", or "Delete".</summary>
    public string Action { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>JSON representation of previous values.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON representation of current values.</summary>
    public string? NewValues { get; set; }

    /// <summary>JSON array of changed property names.</summary>
    public string? ChangedFields { get; set; }
}
