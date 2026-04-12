namespace CrudKit.Core.Interfaces;

/// <summary>
/// Customize auto-sequence generation per entity. Resolved from DI automatically.
/// Use to provide tenant-specific templates or resolve custom placeholders.
/// </summary>
public interface ISequenceCustomizer<TEntity> where TEntity : class
{
    /// <summary>
    /// Override the template from [AutoSequence]. Return null to use the attribute template.
    /// Called once per entity creation with the current tenant ID.
    /// </summary>
    string? ResolveTemplate(string? tenantId) => null;

    /// <summary>
    /// Resolve custom placeholders in the template (e.g. {prefix} → "INV").
    /// Return null if no custom placeholders are used.
    /// </summary>
    Dictionary<string, string>? ResolvePlaceholders(string? tenantId) => null;
}
