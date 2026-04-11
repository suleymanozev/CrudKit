namespace CrudKit.Core.Attributes;

/// <summary>
/// Assembly-level attribute for CrudKit conventions.
/// Controls naming patterns for source-generated hooks.
/// Use {Name} as placeholder for the entity name.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class CrudKitAttribute : Attribute
{
    /// <summary>Naming template for Hook stubs. Default: "{Name}Hooks". Must contain {Name}.</summary>
    public string HooksNamingTemplate { get; set; } = "{Name}Hooks";
}
