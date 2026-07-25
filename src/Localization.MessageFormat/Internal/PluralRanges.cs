using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Parses a CLDR range list — comma-separated integers or <c>low..high</c> ranges (for example <c>1, 3..5</c>) —
/// into <c>(low, high)</c> pairs. The one owner of this parse, shared by the CLDR rule evaluator (which tests
/// membership) and the gettext <c>Plural-Forms</c> translator (which emits a C expression from it).
/// </summary>
internal static class PluralRanges
{
    public static (long Low, long High)[] Parse(string list)
    {
        var items = list.Split(',');
        var ranges = new (long Low, long High)[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            ranges[index] = ParseOne(items[index].Trim());
        }

        return ranges;
    }

    private static (long Low, long High) ParseOne(string item)
    {
        if (!item.Contains(".."))
        {
            var single = long.Parse(item, CultureInfo.InvariantCulture);
            return (single, single);
        }

        var bounds = item.Split([".."], 2, StringSplitOptions.None);
        return (long.Parse(bounds[0], CultureInfo.InvariantCulture), long.Parse(bounds[1], CultureInfo.InvariantCulture));
    }
}
