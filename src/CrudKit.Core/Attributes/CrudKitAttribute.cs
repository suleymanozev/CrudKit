namespace CrudKit.Core.Attributes;

/// <summary>
/// Assembly-level attribute for CrudKit conventions.
/// Controls naming patterns for source-generated DTOs, mappers, and hooks.
/// Use {Name} as placeholder for the entity name.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class CrudKitAttribute : Attribute
{
    /// <summary>Naming pattern for Create DTOs. Default: "Create{Name}". Must contain {Name}.</summary>
    public string CreateDto { get; set; } = "Create{Name}";

    /// <summary>Naming pattern for Update DTOs. Default: "Update{Name}". Must contain {Name}.</summary>
    public string UpdateDto { get; set; } = "Update{Name}";

    /// <summary>Naming pattern for Response DTOs. Default: "{Name}Response". Must contain {Name}.</summary>
    public string ResponseDto { get; set; } = "{Name}Response";

    /// <summary>Naming pattern for Mappers. Default: "{Name}Mapper". Must contain {Name}.</summary>
    public string Mapper { get; set; } = "{Name}Mapper";

    /// <summary>Naming pattern for Hook stubs. Default: "{Name}Hooks". Must contain {Name}.</summary>
    public string Hooks { get; set; } = "{Name}Hooks";
}
