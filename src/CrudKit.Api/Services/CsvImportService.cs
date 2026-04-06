using System.Reflection;
using System.Text;
using CrudKit.Api.Models;
using CrudKit.Core.Attributes;

namespace CrudKit.Api.Services;

/// <summary>
/// Parses CSV content into property dictionaries for entity creation during import.
/// </summary>
public static class CsvImportService
{
    private static readonly HashSet<string> SystemFields = new(StringComparer.Ordinal)
    {
        "Id", "CreatedAt", "UpdatedAt", "DeletedAt", "TenantId", "RowVersion",
        "CreatedById", "UpdatedById", "DeletedById", "CreatedBy", "UpdatedBy", "DeletedBy"
    };

    /// <summary>
    /// Parses CSV content into a list of property dictionaries suitable for entity creation.
    /// Returns parsed rows and any parse-level errors.
    /// </summary>
    public static (List<Dictionary<string, object?>> Rows, List<ImportError> Errors) Parse<TEntity>(string csvContent)
        where TEntity : class
    {
        var rows = new List<Dictionary<string, object?>>();
        var errors = new List<ImportError>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return (rows, errors);

        // Parse header
        var headers = ParseCsvLine(lines[0]);

        // Build property map (case-insensitive)
        var entityProps = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<NotImportableAttribute>() == null)
            .Where(p => !SystemFields.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        // Parse data rows
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var values = ParseCsvLine(line);
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var rowIndex = i + 1; // 1-based, accounting for header

            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                var header = headers[j].Trim();
                if (!entityProps.TryGetValue(header, out var prop)) continue;

                try
                {
                    var converted = ConvertValue(values[j].Trim(), prop.PropertyType);
                    row[prop.Name] = converted;
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError
                    {
                        Row = rowIndex,
                        Field = prop.Name,
                        Message = $"Cannot convert '{values[j]}' to {prop.PropertyType.Name}: {ex.Message}"
                    });
                }
            }

            if (row.Count > 0)
                rows.Add(row);
        }

        return (rows, errors);
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType)
                return null;
            return Activator.CreateInstance(targetType);
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(string)) return value;
        if (underlyingType == typeof(Guid)) return Guid.Parse(value);
        if (underlyingType == typeof(DateTime)) return DateTime.Parse(value);
        if (underlyingType.IsEnum) return Enum.Parse(underlyingType, value, ignoreCase: true);

        return Convert.ChangeType(value, underlyingType);
    }

    private static string[] ParseCsvLine(string line)
    {
        // Simple CSV parser that handles quoted fields with commas
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else if (c != '\r') // skip carriage return
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
