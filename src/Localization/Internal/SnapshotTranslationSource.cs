using System.Globalization;

namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// The merged catalog snapshot presented as an <see cref="ITranslationSource"/>, so the loaded catalogs are
/// just another layer in the same resolution as any custom source — no special path. It looks the composite
/// key up under the category for the requested culture, falling back through parent cultures; the in-code
/// default is ignored (it is the engine's terminal fallback, not a source result).
/// </summary>
internal sealed class SnapshotTranslationSource(TranslationSnapshot snapshot) : ITranslationSource
{
    /// <inheritdoc />
    public string? Resolve(CultureInfo culture, string category, string key, string defaultMessage)
    {
        CultureInfo? current = culture;
        while (current is not null && !string.IsNullOrEmpty(current.Name))
        {
            if (snapshot.ByCulture.TryGetValue(current.Name, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCategory)
                && byCategory.TryGetValue(category, out IReadOnlyDictionary<string, string>? map)
                && map.TryGetValue(key, out var message))
            {
                return message;
            }

            current = current.Parent;
        }

        return null;
    }
}
