namespace CrudKit.EntityFrameworkCore.Models;

/// <summary>Tracks per-entity, per-tenant, per-year document number counters.</summary>
public class SequenceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public long CurrentVal { get; set; }
}
