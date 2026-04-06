namespace CrudKit.SourceGen.Models;

/// <summary>
/// Resolved naming patterns from [assembly: CrudKit(...)] attribute.
/// </summary>
internal sealed record NamingConvention
{
    public string CreateDtoPattern   { get; init; } = "Create{Name}";
    public string UpdateDtoPattern   { get; init; } = "Update{Name}";
    public string ResponseDtoPattern { get; init; } = "{Name}Response";
    public string MapperPattern      { get; init; } = "{Name}Mapper";
    public string HooksPattern       { get; init; } = "{Name}Hooks";

    public string FormatCreateDto(string entityName)   => CreateDtoPattern.Replace("{Name}", entityName);
    public string FormatUpdateDto(string entityName)   => UpdateDtoPattern.Replace("{Name}", entityName);
    public string FormatResponseDto(string entityName) => ResponseDtoPattern.Replace("{Name}", entityName);
    public string FormatMapper(string entityName)      => MapperPattern.Replace("{Name}", entityName);
    public string FormatHooks(string entityName)       => HooksPattern.Replace("{Name}", entityName);

    public static NamingConvention Default => new NamingConvention();
}
