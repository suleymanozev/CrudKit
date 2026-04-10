using System.Reflection;
using System.Text;
using CrudKit.Core.Attributes;

namespace CrudKit.Api.Services;

/// <summary>
/// Generates CSV content from a list of entities, respecting export exclusion attributes.
/// </summary>
public static class CsvExportService
{
    private static readonly HashSet<string> SystemFields = new(StringComparer.Ordinal)
    {
        "RowVersion", "TenantId"
    };

    /// <summary>
    /// Exports a collection of entities to a CSV string.
    /// Skips properties marked with <see cref="NotExportableAttribute"/> or <see cref="SkipResponseAttribute"/>,
    /// as well as internal system fields (RowVersion, TenantId).
    /// </summary>
    public static string Export<T>(IReadOnlyList<T> data) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<NotExportableAttribute>() is null)
            .Where(p => p.GetCustomAttribute<SkipResponseAttribute>() is null)
            .Where(p => !SystemFields.Contains(p.Name))
            .ToList();

        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(p.Name))));

        // Data rows
        foreach (var item in data)
        {
            var values = props.Select(p =>
            {
                var val = p.GetValue(item);
                if (val is DateTime dt)
                    return EscapeCsv(dt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                return EscapeCsv(val?.ToString() ?? "");
            });
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
