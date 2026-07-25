namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// ARB's on-disk member-naming convention — the human-facing identity for a catalog entry in an ARB file,
/// distinct from <see cref="TranslationKey"/>, the in-memory composite. ARB's flat JSON object holds one
/// member per entry, so the translation category has to live in the member name (the structured formats,
/// XLIFF and PO, keep the bare key and carry the category in a separate note). A global (uncategorized)
/// entry is written as its <em>bare</em> key — matching standard ARB and what translation tools expect —
/// with the sole exception of a key that begins with <c>@</c>, which is escaped with a leading <c>::</c> so
/// it is never confused with ARB's <c>@</c>-metadata members. Keys are stable symbolic identifiers and never
/// begin with the <c>::</c> separator, so a bare key is never ambiguous with a qualified or escaped member.
/// </summary>
internal static class ArbMemberKey
{
    private const string CategorySeparator = "::";
    private const string MetadataPrefix = "@";

    // Qualifies a key with its category (and context when present) into the on-disk member identity — for
    // example Acme.Labels::save, greeting (global), or Acme.Menu::post (#verb). A global key beginning with
    // "@" is escaped as ::@key. Throws ArgumentNullException when key is null.
    public static string Qualify(string category, string key, string? context)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        string member;
        if (string.IsNullOrEmpty(category))
        {
            // Global namespace: the bare key is the member, except a key beginning with "@" is escaped with
            // the separator so it is never read back as an ARB "@"-metadata member.
            member = key.StartsWith(MetadataPrefix, StringComparison.Ordinal) ? CategorySeparator + key : key;
        }
        else
        {
            member = category + CategorySeparator + key;
        }

        return string.IsNullOrEmpty(context) ? member : member + " (#" + context + ")";
    }

    // Recovers the bare key from a qualified identity, given the entry's known category and context (read
    // from the entry's own metadata). The known prefix and suffix are stripped exactly once, so the key is
    // recovered regardless of its content.
    public static string Unqualify(string qualified, string category, string? context)
    {
        var key = qualified ?? string.Empty;

        var prefix = (category ?? string.Empty) + CategorySeparator;
        if (key.StartsWith(prefix, StringComparison.Ordinal))
        {
            key = key[prefix.Length..];
        }

        if (!string.IsNullOrEmpty(context))
        {
            var suffix = " (#" + context + ")";
            if (key.Length >= suffix.Length && key.EndsWith(suffix, StringComparison.Ordinal))
            {
                key = key[..^suffix.Length];
            }
        }

        return key;
    }
}
