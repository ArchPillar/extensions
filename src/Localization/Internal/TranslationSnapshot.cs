namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// An immutable, fully-built view of the loaded overrides. Reload builds a new snapshot in memory and
/// swaps the reference atomically, so readers never lock and never observe a half-built table.
/// </summary>
internal sealed class TranslationSnapshot
{
    public TranslationSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byCulture)
    {
        ByCulture = byCulture;
    }

    /// <summary>Maps a culture (case-insensitive) to its composite-key-to-message overrides.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByCulture { get; }

    public static TranslationSnapshot Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
}
