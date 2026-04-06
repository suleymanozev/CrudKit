namespace CrudKit.Api.Helpers;

using System.Reflection;
using CrudKit.Core.Attributes;

/// <summary>
/// Resolves whether a feature is enabled for a given entity type.
/// Priority: Entity attribute > Global flag.
/// Entity [NotFeature] = force disable. Entity [Feature] = force enable.
/// No attribute = use global flag.
/// </summary>
public static class FeatureResolver
{
    /// <summary>
    /// Returns true if export is enabled for TEntity.
    /// [NotExportable] on class → false. [Exportable] on class → true. Else: globalFlag.
    /// </summary>
    public static bool IsExportEnabled<TEntity>(bool globalFlag)
        => Resolve<TEntity, ExportableAttribute, NotExportableAttribute>(globalFlag);

    /// <summary>
    /// Returns true if import is enabled for TEntity.
    /// [NotImportable] on class → false. [Importable] on class → true. Else: globalFlag.
    /// </summary>
    public static bool IsImportEnabled<TEntity>(bool globalFlag)
        => Resolve<TEntity, ImportableAttribute, NotImportableAttribute>(globalFlag);

    /// <summary>
    /// Returns true if audit trail is enabled for TEntity.
    /// [NotAudited] on class → false. [Audited] on class → true. Else: globalFlag (AuditTrailEnabled).
    /// </summary>
    public static bool IsAuditEnabled<TEntity>(bool globalFlag)
        => Resolve<TEntity, AuditedAttribute, NotAuditedAttribute>(globalFlag);

    /// <summary>
    /// Generic resolution: TDisable wins, then TEnable wins, then falls back to globalFlag.
    /// </summary>
    private static bool Resolve<TEntity, TEnable, TDisable>(bool globalFlag)
        where TEnable : Attribute
        where TDisable : Attribute
    {
        var type = typeof(TEntity);

        // Entity-level explicit override takes priority
        if (type.GetCustomAttribute<TDisable>() != null) return false;
        if (type.GetCustomAttribute<TEnable>() != null) return true;

        // Fall back to global flag
        return globalFlag;
    }
}
