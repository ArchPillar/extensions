using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The gettext representation of an ICU cardinal plural: the source singular/plural (gettext
/// <c>msgid</c>/<c>msgid_plural</c>) and the translated forms indexed in the target language's
/// gettext order (gettext <c>msgstr[n]</c>). The ICU argument name is recorded so the reverse
/// conversion is exact.
/// </summary>
internal sealed record GettextPlural(
    string ArgumentName,
    string SingularSource,
    string PluralSource,
    IReadOnlyList<string> TranslatedForms);

/// <summary>
/// Converts between an ICU cardinal-plural message and its gettext form. Only a clean top-level
/// <c>plural</c> (not <c>selectordinal</c>, no <c>offset</c>, no explicit <c>=N</c> selectors, source
/// expressed with <c>one</c>/<c>other</c>) is representable; anything else returns <see langword="null"/>
/// so the provider keeps the message as opaque ICU text. ICU recognition and emission are owned by
/// <see cref="MessageSyntax"/>; this type only maps between the ICU branches and the gettext form order.
/// </summary>
internal static class PoPluralConverter
{
    public static GettextPlural? ToGettext(string sourceIcu, string? translatedIcu, string targetCulture)
    {
        if (MessageSyntax.RecognizeCardinalPlural(sourceIcu) is not { } source
            || !source.Branches.TryGetValue(PluralCategory.One, out var singular)
            || !source.Branches.TryGetValue(PluralCategory.Other, out var pluralSource))
        {
            return null;
        }

        IReadOnlyList<PluralCategory> order = PluralRules.GettextOrder(targetCulture);
        IReadOnlyDictionary<PluralCategory, string> translatedBranches = TranslatedBranches(translatedIcu);

        var forms = new string[order.Count];
        for (var index = 0; index < order.Count; index++)
        {
            forms[index] = translatedBranches.TryGetValue(order[index], out var body) ? body : string.Empty;
        }

        return new GettextPlural(source.ArgumentName, singular, pluralSource, forms);
    }

    public static (string Source, string? Translated) FromGettext(GettextPlural plural, string targetCulture)
    {
        var source = MessageSyntax.BuildCardinalPlural(
            plural.ArgumentName,
            [(PluralCategory.One, plural.SingularSource), (PluralCategory.Other, plural.PluralSource)]);

        IReadOnlyList<PluralCategory> order = PluralRules.GettextOrder(targetCulture);
        var branches = new List<(PluralCategory Category, string Body)>();
        var anyTranslated = false;
        for (var index = 0; index < order.Count && index < plural.TranslatedForms.Count; index++)
        {
            branches.Add((order[index], plural.TranslatedForms[index]));
            anyTranslated |= plural.TranslatedForms[index].Length > 0;
        }

        return anyTranslated
            ? (source, MessageSyntax.BuildCardinalPlural(plural.ArgumentName, branches))
            : (source, null);
    }

    private static IReadOnlyDictionary<PluralCategory, string> TranslatedBranches(string? translatedIcu) =>
        translatedIcu is not null && MessageSyntax.RecognizeCardinalPlural(translatedIcu) is { } shape
            ? shape.Branches
            : new Dictionary<PluralCategory, string>();
}
