namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>A translatable call site recovered from a built assembly: the key, the in-code default, and the
/// category (the localizer's type argument, empty for a global/non-generic localizer). Produced both by the IL
/// call-site scan (<see cref="CallSiteExtractor"/>) and the annotation scan (<see cref="AnnotationExtractor"/>).</summary>
internal sealed record RawCallSite(string Key, string Default, string Category);
