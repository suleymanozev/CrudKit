namespace CrudKit.Api.Models;

/// <summary>
/// Result returned by the CSV/XLSX import endpoint.
/// </summary>
public class ImportResult
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Failed { get; set; }
    public List<ImportError> Errors { get; set; } = new();
}

/// <summary>
/// Describes a single error encountered during import for a specific row/field.
/// </summary>
public class ImportError
{
    public int Row { get; set; }
    public string? Field { get; set; }
    public string Message { get; set; } = string.Empty;
}
