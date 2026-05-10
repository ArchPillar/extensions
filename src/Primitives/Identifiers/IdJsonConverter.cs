using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Identifiers;

internal sealed class IdJsonConverter(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type idType)
    : JsonConverter<IId>
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private readonly Type _idType = idType;

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert == _idType;

    /// <inheritdoc />
    public override IId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return (IId)Activator.CreateInstance(_idType, reader.GetGuid())!;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        IId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
