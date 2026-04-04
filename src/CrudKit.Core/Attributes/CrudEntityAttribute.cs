namespace CrudKit.Core.Attributes;

/// <summary>
/// Configures entity behavior: table mapping, soft delete, audit logging, multi-tenancy, workflow, and bulk operations.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CrudEntityAttribute : Attribute
{
    public string Table { get; set; } = string.Empty;
    public bool SoftDelete { get; set; }
    public bool Audit { get; set; }
    public bool MultiTenant { get; set; }
    public string? Workflow { get; set; }
    public string[]? WorkflowProtected { get; set; }
    public string? NumberingPrefix { get; set; }
    public bool NumberingYearlyReset { get; set; } = true;
    public bool EnableBulkUpdate { get; set; }
    public int BulkLimit { get; set; } = 0; // 0 = use global default from CrudKitApiOptions

    /// <summary>
    /// Property name on the entity that holds the owner user ID.
    /// Used with PermScope.Own to filter entities by owner.
    /// </summary>
    public string? OwnerField { get; set; }
}
