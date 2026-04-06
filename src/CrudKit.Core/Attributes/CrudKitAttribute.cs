namespace CrudKit.Core.Attributes;

/// <summary>
/// Assembly-level attribute for CrudKit conventions.
/// Controls naming patterns for source-generated DTOs, mappers, and hooks.
/// Use {Name} as placeholder for the entity name.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class CrudKitAttribute : Attribute
{
    /// <summary>Naming template for Create DTOs. Default: "Create{Name}". Must contain {Name}.</summary>
    public string CreateDtoNamingTemplate { get; set; } = "Create{Name}";

    /// <summary>Naming template for Update DTOs. Default: "Update{Name}". Must contain {Name}.</summary>
    public string UpdateDtoNamingTemplate { get; set; } = "Update{Name}";

    /// <summary>Naming template for Response DTOs. Default: "{Name}Response". Must contain {Name}.</summary>
    public string ResponseDtoNamingTemplate { get; set; } = "{Name}Response";

    /// <summary>Naming template for Mappers. Default: "{Name}Mapper". Must contain {Name}.</summary>
    public string MapperNamingTemplate { get; set; } = "{Name}Mapper";

    /// <summary>Naming template for Hook stubs. Default: "{Name}Hooks". Must contain {Name}.</summary>
    public string HooksNamingTemplate { get; set; } = "{Name}Hooks";
}
