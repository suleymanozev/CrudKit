using System.Reflection;
using CrudKit.Api.Tenancy;
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;

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
    /// When true, failed SaveChanges operations are also logged to the audit trail
    /// with action prefixed as "Failed" (e.g. "FailedCreate"). Default: false.
    /// Set via UseAuditTrail().EnableAuditFailedOperations().
    /// </summary>
    internal bool AuditFailedOperations { get; set; }

    /// <summary>
    /// Custom <see cref="CrudKit.Core.Interfaces.IAuditWriter"/> implementation type.
    /// When null the default DbAuditWriter is used.
    /// </summary>
    internal Type? CustomAuditWriterType { get; set; }

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
    /// When true, all enum properties on entities are stored as strings in the database.
    /// Default: false (stored as integers).
    /// </summary>
    public bool EnumAsStringEnabled { get; set; }

    /// <summary>
    /// Enables audit trail logging for entities decorated with [Audited].
    /// Returns <see cref="AuditTrailOptions"/> for further audit-specific configuration.
    /// </summary>
    public AuditTrailOptions UseAuditTrail()
    {
        AuditTrailEnabled = true;
        return new AuditTrailOptions(this);
    }

    /// <summary>
    /// Enables audit trail logging with a custom <see cref="CrudKit.Core.Interfaces.IAuditWriter"/>.
    /// Returns <see cref="AuditTrailOptions"/> for further audit-specific configuration.
    /// </summary>
    public AuditTrailOptions UseAuditTrail<TAuditWriter>()
        where TAuditWriter : class, CrudKit.Core.Interfaces.IAuditWriter
    {
        AuditTrailEnabled = true;
        CustomAuditWriterType = typeof(TAuditWriter);
        return new AuditTrailOptions(this);
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

    /// <summary>
    /// Stores all enum properties as their string name rather than integer value.
    /// Affects all entities managed by CrudKitDbContext.
    /// </summary>
    public CrudKitApiOptions UseEnumAsString()
    {
        EnumAsStringEnabled = true;
        return this;
    }

    // Tenant resolution — set via UseMultiTenancy().ResolveFrom*()
    internal Func<HttpContext, string?>? TenantResolver { get; set; }

    /// <summary>
    /// When true, requests where the tenant cannot be resolved return 400.
    /// Set via UseMultiTenancy().RejectUnresolvedTenant().
    /// </summary>
    internal bool TenantRejectUnresolved { get; set; }

    /// <summary>
    /// Cross-tenant access policy. null = no cross-tenant access allowed.
    /// Set via UseMultiTenancy().CrossTenantPolicy().
    /// </summary>
    internal CrossTenantPolicy? CrossTenantPolicyInstance { get; set; }

    /// <summary>
    /// Enables multi-tenancy support. Returns <see cref="MultiTenancyOptions"/>
    /// to configure how the tenant ID is resolved from each request.
    /// </summary>
    public MultiTenancyOptions UseMultiTenancy()
    {
        return new MultiTenancyOptions(this);
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
