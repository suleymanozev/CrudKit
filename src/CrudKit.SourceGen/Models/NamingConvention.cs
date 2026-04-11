namespace CrudKit.SourceGen.Models;

/// <summary>
/// Resolved naming templates from [assembly: CrudKit(...)] attribute.
/// Only HooksNamingTemplate is relevant — DTO and mapper naming is user-controlled.
/// </summary>
internal sealed record NamingConvention
{
    public string HooksNamingTemplate { get; init; } = "{Name}Hooks";

    public string FormatHooksName(string entityName) => HooksNamingTemplate.Replace("{Name}", entityName);

    public static NamingConvention Default => new NamingConvention();
}
