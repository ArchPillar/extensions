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
    /// resolver and method cache are reused for every assembly in one scan.</summary>
    public static Catalog? Build(AssemblyStringExtractor extractor, string assemblyPath, string sourceLanguage)
    {
        IReadOnlyList<RawCallSite> sites = extractor.Extract(assemblyPath);
        if (sites.Count == 0)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();
        foreach (RawCallSite site in sites)
        {
            // One entry per distinct (category, key, context): the same key under a different context is a
            // different string, exactly as the catalog model defines identity.
            if (!seen.Add(site.Category + "\0" + site.Key + "\0" + (site.Context ?? string.Empty)))
            {
                continue;
            }

            entries.Add(new CatalogEntry
            {
                Key = site.Key,
                Category = site.Category,
                Context = site.Context,
                SourceMessage = site.Default,
                SourceFingerprint = Fingerprint(site.Default, site.Context),
                State = TranslationState.NeedsTranslation
            });
        }

        return new Catalog { Culture = sourceLanguage, Entries = entries };
    }

    // The same stable source fingerprint the generator writes: a truncated SHA-256 over the NFC-normalized
    // source message and context, so a target reconciled against an IL-built template detects drift identically.
    internal static string Fingerprint(string source, string? context)
    {
        var normalized = source.Normalize(NormalizationForm.FormC) + "\0" + (context ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var builder = new StringBuilder(32);
        for (var index = 0; index < 16; index++)
        {
            builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
