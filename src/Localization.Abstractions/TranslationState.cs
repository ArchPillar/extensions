namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The canonical translation-state machine. Each container format maps these onto its own
/// representation (XLIFF carries them natively, Portable Object infers them from <c>fuzzy</c> and an
/// empty <c>msgstr</c>, and ARB persists them in metadata).
/// </summary>
public enum TranslationState
{
    /// <summary>No translation has been provided yet.</summary>
    NeedsTranslation,

    /// <summary>A translation exists but the source drifted, or the placeholder set changed.</summary>
    NeedsReview,

    /// <summary>Translated, but not yet signed off.</summary>
    Translated,

    /// <summary>Reviewed and approved.</summary>
    Final
}
