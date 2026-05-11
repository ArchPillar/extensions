using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Models;

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
        "Trimming",
        "IL2067:UnrecognizedReflectionPattern",
        Justification = "CreateConverter is only invoked by JsonSerializer after CanConvert returns true, which guarantees typeToConvert is a closed Id<T>. Id<T>'s public constructors are preserved transitively through the Id<T> type reference in CanConvert and the [DynamicallyAccessedMembers] annotation on IdJsonConverter._idType.")]
    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
        => new IdJsonConverter(typeToConvert);
}
