using System.Text.Json.Serialization;
using CrudKit.Core.Serialization;

namespace CrudKit.Core.Models;

/// <summary>
/// Update DTO'larında "alan gönderildi mi" ile "null gönderildi" arasındaki farkı tutar.
/// HasValue=false → alanı atla (JSON'da yoktu). HasValue=true → değeri uygula (null dahil).
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

    /// <summary>JSON'da alan yoktu — bu alana dokunma.</summary>
    public static Optional<T> Undefined => new(default, false);

    /// <summary>JSON'da alan vardı — bu değeri uygula (null dahil).</summary>
    public static Optional<T> From(T? value) => new(value, true);

    public static implicit operator Optional<T>(T? value) => From(value);
}
