using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Identifiers;

/// <summary>
/// <see cref="JsonConverterFactory"/> that matches any closed <c>Id&lt;T&gt;</c>
/// and serializes it as a plain Guid string — wire shape identical to
/// <see cref="Guid"/>.
/// </summary>
public sealed class IdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(Id<>);

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "CreateConverter is only reachable from JsonSerializer, which is already annotated [RequiresDynamicCode]. IdJsonConverter<T> has no static initializers or complex generic constraints.")]
    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        Type entityType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(IdJsonConverter<>).MakeGenericType(entityType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
