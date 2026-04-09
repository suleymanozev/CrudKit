using System.Text.RegularExpressions;

namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Parses AutoSequence templates and generates formatted sequence values.
/// </summary>
public static partial class SequenceGenerator
{
    /// <summary>
    /// Resolves date tokens in the template and extracts the prefix (everything before {seq}).
    /// Returns the prefix and the zero-padding width.
    /// </summary>
    public static (string Prefix, int Padding) ResolvePrefix(string template, DateOnly date)
    {
        var resolved = template
            .Replace("{year}", date.Year.ToString())
            .Replace("{month}", date.Month.ToString("D2"))
            .Replace("{day}", date.Day.ToString("D2"));

        var match = SeqPattern().Match(resolved);
        if (!match.Success)
            throw new ArgumentException($"Template must contain {{seq:N}} token. Got: '{template}'");

        var padding = int.Parse(match.Groups[1].Value);
        var prefix = resolved[..match.Index];

        return (prefix, padding);
    }

    /// <summary>
    /// Formats the final sequence string: prefix + zero-padded value.
    /// </summary>
    public static string FormatSequenceValue(string prefix, long value, int padding)
    {
        return $"{prefix}{value.ToString($"D{padding}")}";
    }

    [GeneratedRegex(@"\{seq:(\d+)\}")]
    private static partial Regex SeqPattern();
}
