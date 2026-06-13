namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The features a container format supports. The reconciler reads these to decide how to represent
/// translation state and previous-source on each format.
/// </summary>
[Flags]
public enum FormatCapabilities
{
    /// <summary>No optional capabilities.</summary>
    None = 0,

    /// <summary>Disambiguation context distinct from the key.</summary>
    Context = 1 << 0,

    /// <summary>Translator comments.</summary>
    Comments = 1 << 1,

    /// <summary>Source references (file and position where a key is used).</summary>
    SourceReferences = 1 << 2,

    /// <summary>A native translation-state field (rather than an inferred one).</summary>
    ExplicitState = 1 << 3,

    /// <summary>Plurals expressed in the format's own native scheme.</summary>
    NativePlural = 1 << 4,

    /// <summary>Plurals expressed as ICU MessageFormat in the value.</summary>
    IcuPlural = 1 << 5,

    /// <summary>The ability to record the prior source text on drift.</summary>
    PreviousSource = 1 << 6
}
