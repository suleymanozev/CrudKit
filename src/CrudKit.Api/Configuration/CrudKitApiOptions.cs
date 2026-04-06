using System.Reflection;
using CrudKit.Core.Interfaces;

namespace CrudKit.Api.Configuration;

public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public string ApiPrefix { get; set; } = "/api";
    public int BulkLimit { get; set; } = 10_000;
    public Assembly? ScanModulesFromAssembly { get; set; }
    public bool EnableIdempotency { get; set; }

    /// <summary>
    /// When true, entities decorated with [Audited] have their changes logged
    /// to the __crud_audit_logs table. Default: false.
    /// </summary>
    public bool AuditTrailEnabled { get; set; }

    /// <summary>
    /// When true, export endpoints are generated for all entities by default.
    /// Individual entities can opt out with [NotExportable] or force opt in with [Exportable].
    /// </summary>
    public bool ExportEnabled { get; set; }

    /// <summary>
    /// When true, import endpoints are generated for all entities by default.
    /// Individual entities can opt out with [NotImportable] or force opt in with [Importable].
    /// </summary>
    public bool ImportEnabled { get; set; }

    /// <summary>
    /// Enables audit trail logging for entities decorated with [Audited].
    /// Changes are written to the __crud_audit_logs table on Create, Update, and Delete.
    /// </summary>
    public CrudKitApiOptions UseAuditTrail()
    {
        AuditTrailEnabled = true;
        return this;
    }

    /// <summary>
    /// Enables export endpoints globally. All entities get a GET /export endpoint
    /// unless the entity is marked [NotExportable]. Entity-level [Exportable] always wins.
    /// </summary>
    public CrudKitApiOptions UseExport()
    {
        ExportEnabled = true;
        return this;
    }

    /// <summary>
    /// Enables import endpoints globally. All entities get a POST /import endpoint
    /// unless the entity is marked [NotImportable]. Entity-level [Importable] always wins.
    /// </summary>
    public CrudKitApiOptions UseImport()
    {
        ImportEnabled = true;
        return this;
    }

    // Store global hook types to register in DI
    internal List<Type> GlobalHookTypes { get; } = new();

    /// <summary>
    /// Registers a global hook that runs for all entity CRUD operations.
    /// Multiple global hooks can be registered — they run in registration order.
    /// </summary>
    public CrudKitApiOptions UseGlobalHook<T>() where T : class, IGlobalCrudHook
    {
        GlobalHookTypes.Add(typeof(T));
        return this;
    }
}
