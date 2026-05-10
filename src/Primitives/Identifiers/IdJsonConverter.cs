using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Identifiers;

internal sealed class IdJsonConverter<T> : JsonConverter<Id<T>>
{
    /// <inheritdoc />
    public override Id<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetGuid());

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        Id<T> value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
