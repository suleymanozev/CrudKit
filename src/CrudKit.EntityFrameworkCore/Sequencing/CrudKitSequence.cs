namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Tracks the current sequence value per entity type, tenant, and prefix.
/// Used by the AutoSequence attribute for atomic number generation.
/// </summary>
public class CrudKitSequence
{
    public Guid Id { get; set; }

    /// <summary>Entity type name, e.g. "Invoice".</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Tenant ID for multi-tenant isolation. Empty string for non-tenant scenarios.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Resolved prefix from template, e.g. "INV-2026" for "INV-{year}-{seq:5}".</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Current sequence counter value.</summary>
    public long CurrentValue { get; set; }
}
