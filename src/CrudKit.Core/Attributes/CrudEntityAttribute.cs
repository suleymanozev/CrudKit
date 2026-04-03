namespace CrudKit.Core.Attributes;

/// <summary>
/// Entity davranışını yapılandırır. Table, soft delete, audit, tenant, workflow gibi özellikleri açar.
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
    public int BulkLimit { get; set; } = 0; // 0 = global default kullan
}
