using System.Text.Json.Serialization;
using CrudKit.Core.Serialization;

namespace CrudKit.Core.Models;

/// <summary>
/// Distinguishes between "field was not sent" and "field was explicitly set to null" in Update DTOs.
/// HasValue=false → skip this field (absent from JSON). HasValue=true → apply the value (including null).
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public bool HasValue { get; }
    public T? Value { get; }

    private Optional(T? value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    /// <summary>Field was absent from JSON — do not touch this property.</summary>
    public static Optional<T> Undefined => new(default, false);

    /// <summary>Field was present in JSON — apply this value (including null).</summary>
    public static Optional<T> From(T? value) => new(value, true);

    public static implicit operator Optional<T>(T? value) => From(value);
}
