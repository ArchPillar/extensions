namespace ArchPillar.Extensions.Localization.Formats;

// The translation formats the library ships with. The runtime's catalog providers, the store, and the HTTP
// loader all parse the same set, so it is defined here once instead of being re-listed at every construction
// site. Each call returns a fresh registry, keeping format support per-component with no shared static state.
internal static class BuiltInTranslationFormats
{
    public static TranslationFormatRegistry CreateRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }
}
