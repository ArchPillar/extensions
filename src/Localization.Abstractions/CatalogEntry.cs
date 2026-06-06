namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A single entry in a <see cref="Catalog"/>: one distinct <c>(Key, Context)</c> pair, its source
/// default, and (in a target catalog) its translation and state. Format providers convert between this
/// model and their on-disk representation.
/// </summary>
public sealed record CatalogEntry
{
    /// <summary>The stable symbolic key.</summary>
    public required string Key { get; init; }

    /// <summary>The optional disambiguation context.</summary>
    public string? Context { get; init; }

    /// <summary>The source-language default message (ICU MessageFormat).</summary>
    public required string SourceMessage { get; init; }

    /// <summary>The translated message, or <see langword="null"/>/empty in a template or untranslated entry.</summary>
    public string? TranslatedMessage { get; init; }

    /// <summary>The translator comment, if any.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// The prior source default recorded when the source drifted, so a translator can diff against it.
    /// Set by reconciliation; <see langword="null"/> otherwise.
    /// </summary>
    public string? PreviousSource { get; init; }

    /// <summary>The source locations where the key is used.</summary>
    public IReadOnlyList<SourceReference> References { get; init; } = [];

    /// <summary>The placeholder names the source message references.</summary>
    public IReadOnlyList<string> Placeholders { get; init; } = [];

    /// <summary>
    /// A stable fingerprint of the source default the translation was made against, used to detect drift.
    /// </summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>The translation state.</summary>
    public TranslationState State { get; init; } = TranslationState.NeedsTranslation;
}
