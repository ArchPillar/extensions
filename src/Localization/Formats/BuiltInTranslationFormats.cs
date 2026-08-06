namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The translation formats the library ships with (XLIFF, ARB, PO for authoring; APLOC for the deploy bundle),
/// assembled into a fresh <see cref="TranslationFormatRegistry"/>. This is the default format set; each call
/// returns a new registry, so support stays per-instance with no shared static state. Start from it to register a
/// custom format.
/// </summary>
public static class BuiltInTranslationFormats
{
    /// <summary>Creates a registry with the built-in XLIFF, ARB, PO, and APLOC formats registered.</summary>
    /// <returns>A new <see cref="TranslationFormatRegistry"/> containing the built-in formats.</returns>
    public static TranslationFormatRegistry CreateRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        registry.Register(new AplocTranslationFormat());
        return registry;
    }
}
