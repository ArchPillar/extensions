using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Builds the source-language template <see cref="Catalog"/> for an assembly from its IL (Decision D-K),
/// replacing the read of the generator's baked attribute. One entry per distinct (category, key, context), the
/// source default as the value, the same drift fingerprint the generator computes, and <c>NeedsTranslation</c>
/// state.
/// </summary>
internal static class TemplateBuilder
{
    /// <summary>Returns the template for <paramref name="assemblyPath"/>, or <see langword="null"/> when the
    /// assembly has no translatable strings. The <paramref name="extractor"/> is shared across a batch so its
    /// resolver and method cache are reused for every assembly in one scan. <paramref name="includeAnnotations"/>
    /// folds in strings carried by display annotations (<c>[DisplayName]</c> / <c>[Display]</c> /
    /// <c>[Description]</c> and the <c>[Localized…]</c> twins); pass <see langword="false"/> to opt out and emit
    /// only the IL call sites. IL call sites take precedence over an annotation on the same (category, key, context).</summary>
    public static Catalog? Build(AssemblyStringExtractor extractor, string assemblyPath, string sourceLanguage, bool includeAnnotations = true)
    {
        (IReadOnlyList<RawCallSite> callSites, IReadOnlyList<RawCallSite> annotations) = extractor.Extract(assemblyPath, includeAnnotations);
        if (callSites.Count == 0 && annotations.Count == 0)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();
        foreach (RawCallSite site in callSites.Concat(annotations))
        {
            // One entry per distinct (category, key) — the same category-qualified identity the reconciler
            // indexes by.
            if (!seen.Add(TranslationKey.ComposeQualified(site.Category, site.Key)))
            {
                continue;
            }

            entries.Add(new CatalogEntry
            {
                Key = site.Key,
                Category = site.Category,
                SourceMessage = site.Default,
                SourceFingerprint = Fingerprint(site.Default),
                State = TranslationState.NeedsTranslation
            });
        }

        return new Catalog { Culture = sourceLanguage, Entries = entries };
    }

    // A stable source fingerprint: a truncated SHA-256 over the NFC-normalized source message, so a target
    // reconciled against an IL-built template detects drift identically.
    internal static string Fingerprint(string source)
    {
        var normalized = source.Normalize(NormalizationForm.FormC);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var builder = new StringBuilder(32);
        for (var index = 0; index < 16; index++)
        {
            builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
