using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Builds the source-language template <see cref="Catalog"/> for an assembly from its IL (Decision D-K),
/// replacing the read of the generator's baked attribute. One entry per distinct (category, key), the source
/// default as the value, the same drift fingerprint the generator computes, and <c>NeedsTranslation</c> state.
/// The translation comment, when the author wrote one, comes from a source scan
/// (<see cref="SourceCommentScanner"/>) joined by identity — comments cannot be read from the binary. The source
/// files an entry is used in come from the PDB, recorded relative to the project root (Decision D-N).
/// </summary>
internal static class TemplateBuilder
{
    /// <summary>Returns the template for <paramref name="assemblyPath"/>, or <see langword="null"/> when the
    /// assembly has no translatable strings. The <paramref name="extractor"/> is shared across a batch so its
    /// resolver and method cache are reused for every assembly in one scan. <paramref name="comments"/> carries the
    /// translation comments scanned from the project's source (empty when there is no source root); each entry's
    /// comment is looked up by its key and default. <paramref name="referenceRoot"/> is the project directory file
    /// references are recorded relative to; a reference outside it is dropped rather than recorded as a
    /// machine-specific absolute path, and <see langword="null"/> records no references at all (references are
    /// opt-in). <paramref name="includeAnnotations"/> folds in strings carried
    /// by display annotations (<c>[DisplayName]</c> / <c>[Display]</c> / <c>[Description]</c> and the
    /// <c>[Localized…]</c> twins); pass <see langword="false"/> to opt out and emit only the IL call sites. IL call
    /// sites take precedence over an annotation on the same (category, key), whose file references are unioned.</summary>
    public static Catalog? Build(
        AssemblyStringExtractor extractor,
        string assemblyPath,
        string sourceLanguage,
        CommentIndex? comments = null,
        string? referenceRoot = null,
        bool includeAnnotations = true)
    {
        (IReadOnlyList<RawCallSite> callSites, IReadOnlyList<RawCallSite> annotations) = extractor.Extract(assemblyPath, includeAnnotations);
        if (callSites.Count == 0 && annotations.Count == 0)
        {
            return null;
        }

        CommentIndex resolvedComments = comments ?? CommentIndex.Empty;
        // One entry per distinct (category, key) — the same category-qualified identity the reconciler indexes by.
        // The first site wins the default (and so the fingerprint); every site contributes its file, because "where
        // is this string used" is the union over all of them, not just the first.
        var order = new List<string>();
        var byIdentity = new Dictionary<string, (RawCallSite First, SortedSet<string> Files)>(StringComparer.Ordinal);
        foreach (RawCallSite site in callSites.Concat(annotations))
        {
            var identity = TranslationKey.ComposeQualified(site.Category, site.Key);
            if (!byIdentity.TryGetValue(identity, out (RawCallSite First, SortedSet<string> Files) group))
            {
                group = (site, new SortedSet<string>(StringComparer.Ordinal));
                byIdentity[identity] = group;
                order.Add(identity);
            }

            if (Relativize(site.File, referenceRoot) is { } file)
            {
                group.Files.Add(file);
            }
        }

        var entries = new List<CatalogEntry>(order.Count);
        foreach (var identity in order)
        {
            (RawCallSite site, SortedSet<string> files) = byIdentity[identity];
            entries.Add(new CatalogEntry
            {
                Key = site.Key,
                Category = site.Category,
                SourceMessage = site.Default,
                Comment = resolvedComments.Lookup(site.Key, site.Default),
                References = [.. files.Select(file => new SourceReference(file, 0, 0))],
                SourceFingerprint = Fingerprint(site.Default),
                State = TranslationState.NeedsTranslation
            });
        }

        return new Catalog { Culture = sourceLanguage, Entries = entries };
    }

    // A file reference as a path relative to the project root, with '/' separators so the catalog is identical on
    // every machine and OS. A path that is absent, has no root to resolve against, or falls outside the project
    // (a deterministic /pathmap build, a loose --assembly, a linked file) is dropped: no reference is honest, a
    // machine-specific absolute path is not — and the reconciler preserves whatever the catalog already had.
    private static string? Relativize(string? file, string? referenceRoot)
    {
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(referenceRoot))
        {
            return null;
        }

        if (!Path.IsPathRooted(file))
        {
            return null;
        }

        var relative = Path.GetRelativePath(referenceRoot!, file!);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        return relative.Replace('\\', '/');
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
