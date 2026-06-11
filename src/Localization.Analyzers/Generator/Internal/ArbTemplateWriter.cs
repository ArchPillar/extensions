using System.Globalization;
using System.Text;
using ArchPillar.Extensions.Localization.Detection;

namespace ArchPillar.Extensions.Localization.Generator.Internal;

/// <summary>
/// Serializes the extracted sites to the source-language ARB template by hand (no
/// <c>System.Text.Json</c> dependency in the Roslyn host). One entry per distinct key, ordered by
/// source reference, with the source default as the value and metadata (comment, context, placeholders,
/// references, fingerprint) under the sibling <c>@key</c>.
/// </summary>
internal static class ArbTemplateWriter
{
    public static string Write(IReadOnlyList<TranslationSite> sites, string sourceLanguage)
    {
        var members = new List<string> { JsonString("@@locale") + ": " + JsonString(sourceLanguage) };
        foreach (TranslationSite site in OrderAndDistinct(sites))
        {
            // The member is the category-qualified identity, so the same key under two categories (or two
            // contexts) produces two distinct members instead of duplicate JSON keys.
            var member = QualifiedKey.Qualify(site.Category, site.Key, site.Context);
            members.Add(JsonString(member) + ": " + JsonString(site.DefaultMessage));
            members.Add(JsonString("@" + member) + ": " + Metadata(site));
        }

        return "{\n  " + string.Join(",\n  ", members) + "\n}\n";
    }

    private static List<TranslationSite> OrderAndDistinct(IReadOnlyList<TranslationSite> sites)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<TranslationSite>();
        foreach (TranslationSite site in sites
            .OrderBy(s => s.Reference.FilePath, StringComparer.Ordinal)
            .ThenBy(s => s.Reference.Line))
        {
            if (seen.Add(site.Category + "\0" + site.Key + "\0" + (site.Context ?? string.Empty)))
            {
                ordered.Add(site);
            }
        }

        return ordered;
    }

    private static string Metadata(TranslationSite site)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(site.Comment))
        {
            parts.Add(JsonString("description") + ": " + JsonString(site.Comment!));
        }

        if (!string.IsNullOrEmpty(site.Context))
        {
            parts.Add(JsonString("context") + ": " + JsonString(site.Context!));
        }

        if (!string.IsNullOrEmpty(site.Category))
        {
            parts.Add(JsonString("x-category") + ": " + JsonString(site.Category));
        }

        if (site.Placeholders.Count > 0)
        {
            parts.Add(JsonString("placeholders") + ": " + Placeholders(site.Placeholders));
        }

        parts.Add(JsonString("x-references") + ": [" + JsonString(Reference(site.Reference)) + "]");
        parts.Add(JsonString("x-source-fingerprint") + ": " + JsonString(Fingerprint.Compute(site.DefaultMessage, site.Context)));
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Placeholders(IReadOnlyList<string> placeholders)
    {
        var entries = placeholders.Select(name => JsonString(name) + ": {}");
        return "{ " + string.Join(", ", entries) + " }";
    }

    private static string Reference(SourceReference reference) =>
        reference.FilePath + ":" + reference.Line.ToString(CultureInfo.InvariantCulture) + ":" + reference.Column.ToString(CultureInfo.InvariantCulture);

    private static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            AppendEscaped(builder, character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void AppendEscaped(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '"':
                builder.Append("\\\"");
                break;
            case '\\':
                builder.Append("\\\\");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            default:
                AppendLiteral(builder, character);
                break;
        }
    }

    private static void AppendLiteral(StringBuilder builder, char character)
    {
        if (character < ' ')
        {
            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(character);
        }
    }
}
