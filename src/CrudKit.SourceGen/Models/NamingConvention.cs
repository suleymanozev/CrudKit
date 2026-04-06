namespace CrudKit.SourceGen.Models;

/// <summary>
/// Resolved naming templates from [assembly: CrudKit(...)] attribute.
/// </summary>
internal sealed record NamingConvention
{
    public string CreateDtoNamingTemplate   { get; init; } = "Create{Name}";
    public string UpdateDtoNamingTemplate   { get; init; } = "Update{Name}";
    public string ResponseDtoNamingTemplate { get; init; } = "{Name}Response";
    public string MapperNamingTemplate      { get; init; } = "{Name}Mapper";
    public string HooksNamingTemplate       { get; init; } = "{Name}Hooks";

    public string FormatCreateDtoName(string entityName)   => CreateDtoNamingTemplate.Replace("{Name}", entityName);
    public string FormatUpdateDtoName(string entityName)   => UpdateDtoNamingTemplate.Replace("{Name}", entityName);
    public string FormatResponseDtoName(string entityName) => ResponseDtoNamingTemplate.Replace("{Name}", entityName);
    public string FormatMapperName(string entityName)      => MapperNamingTemplate.Replace("{Name}", entityName);
    public string FormatHooksName(string entityName)       => HooksNamingTemplate.Replace("{Name}", entityName);

    public static NamingConvention Default => new NamingConvention();
}
