using System.Text.Json;
using System.Text.Json.Serialization;
using CrudKit.Core.Models;

namespace CrudKit.Core.Serialization;

public class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Bu metod çağrıldıysa JSON'da alan var demektir → HasValue = true
        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return Optional<T>.From(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            JsonSerializer.Serialize(writer, value.Value, options);
        else
            writer.WriteNullValue();  // Geçerli JSON üret — Undefined alanlar null olarak serialize edilir
    }
}
