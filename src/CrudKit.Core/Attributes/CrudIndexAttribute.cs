namespace CrudKit.Core.Attributes;

/// <summary>
/// Defines an index on one or more properties. For IMultiTenant entities,
/// TenantId is automatically prepended unless TenantAware is set to false.
/// Supports soft-delete partial index (WHERE DeletedAt IS NULL) when IsUnique is true.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CrudIndexAttribute : Attribute
{
    /// <summary>Property names to include in the index.</summary>
    public string[] Properties { get; }

    /// <summary>Whether the index enforces uniqueness. Default: false.</summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// Whether TenantId is automatically prepended for IMultiTenant entities.
    /// Default: true. Set to false for intentionally tenant-independent indexes.
    /// </summary>
    public bool TenantAware { get; set; } = true;

    public CrudIndexAttribute(params string[] properties)
    {
        Properties = properties;
    }
}
