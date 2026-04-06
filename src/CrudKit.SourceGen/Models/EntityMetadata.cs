using System.Collections.Generic;

namespace CrudKit.SourceGen.Models;

/// <summary>
/// Immutable snapshot of a [CrudEntity]-decorated class, used by all sub-generators.
/// </summary>
internal sealed class EntityMetadata
{
    public string Name { get; }
    public string Namespace { get; }

    /// <summary>Fully-qualified class name (Namespace.Name).</summary>
    public string FullName { get; }

    /// <summary>Database table name from CrudEntityAttribute.Table.</summary>
    public string Table { get; }

    // CrudEntityAttribute flags
    public bool MultiTenant { get; }
    public bool ReadOnly { get; }
    public bool IsCreateEnabled { get; }
    public bool IsUpdateEnabled { get; }
    public bool IsDeleteEnabled { get; }
    public bool EnableBulkUpdate { get; }
    public bool EnableBulkDelete { get; }
    public string? Workflow { get; }

    // Interface implementation flags (detected from base-type list)
    public bool ImplementsIEntity { get; }
    public bool ImplementsISoftDeletable { get; }
    public bool ImplementsIMultiTenant { get; }

    /// <summary>
    /// All declared properties excluding system fields (Id, CreatedAt, UpdatedAt, DeletedAt, TenantId, RowVersion).
    /// </summary>
    public IReadOnlyList<PropertyMetadata> Properties { get; }

    public EntityMetadata(
        string name,
        string @namespace,
        string fullName,
        string table,
        bool multiTenant,
        bool readOnly,
        bool isCreateEnabled,
        bool isUpdateEnabled,
        bool isDeleteEnabled,
        bool enableBulkUpdate,
        bool enableBulkDelete,
        string? workflow,
        bool implementsIEntity,
        bool implementsISoftDeletable,
        bool implementsIMultiTenant,
        IReadOnlyList<PropertyMetadata> properties)
    {
        Name = name;
        Namespace = @namespace;
        FullName = fullName;
        Table = table;
        MultiTenant = multiTenant;
        ReadOnly = readOnly;
        IsCreateEnabled = isCreateEnabled;
        IsUpdateEnabled = isUpdateEnabled;
        IsDeleteEnabled = isDeleteEnabled;
        EnableBulkUpdate = enableBulkUpdate;
        EnableBulkDelete = enableBulkDelete;
        Workflow = workflow;
        ImplementsIEntity = implementsIEntity;
        ImplementsISoftDeletable = implementsISoftDeletable;
        ImplementsIMultiTenant = implementsIMultiTenant;
        Properties = properties;
    }
}
