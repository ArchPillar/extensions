using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The culture fallback chain: a culture and each of its parents up to — but not including — the invariant culture.
/// The one owner of that walk, shared by the cold paths — loaded-culture tracking, culture-scoped listing, and the
/// manifest provider — so every path resolves the same chain (for example <c>de-AT → de</c>, stopping before
/// invariant). It is an iterator, so it allocates an enumerator per call; the allocation-free lookup hot path
/// (<c>TranslationSnapshot.Lookup</c>) walks the chain inline instead.
/// </summary>
internal static class CultureChain
{
    /// <summary>The culture and each parent, most specific first, ending before the invariant culture.</summary>
    public static IEnumerable<CultureInfo> Of(CultureInfo culture)
    {
        for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            yield return current;
        }
    }
}
