using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Builds a gettext <c>Plural-Forms</c> value (<c>nplurals=N; plural=EXPR;</c>) for a culture from its
/// CLDR cardinal rules. The C expression returns, for each <c>n</c>, the index of the matching form in the
/// gettext ordering (the same order the Portable Object provider writes <c>msgstr[n]</c> in), so an external
/// gettext consumer selects the right form. The translation specializes the CLDR conditions to integers —
/// gettext's <c>n</c> is always an integer, so the operands <c>v w f t e c</c> are zero and <c>i</c> equals
/// <c>n</c> — and folds the resulting constants away.
/// </summary>
internal static class GettextPluralExpression
{
    public static string Build(IReadOnlyList<PluralCategory> order, IReadOnlyList<CldrPluralRule> rules)
    {
        var otherIndex = order.Count - 1;
        var builder = new StringBuilder();
        foreach (CldrPluralRule rule in rules)
        {
            var condition = TranslateCondition(rule.Condition);
            if (condition == "0")
            {
                continue;
            }

            var index = IndexOf(order, rule.Category);
            if (condition == "1")
            {
                builder.Append(index);
                return Format(order.Count, builder.ToString());
            }

            builder.Append('(').Append(condition).Append(") ? ").Append(index).Append(" : ");
        }

        builder.Append(otherIndex);
        return Format(order.Count, builder.ToString());
    }

    private static string Format(int nplurals, string expression) =>
        $"nplurals={nplurals}; plural={expression};";

    // condition = or-groups separated by " or "; each group = relations separated by " and ".
    private static string TranslateCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return "1";
        }

        var groups = new List<string>();
        foreach (var group in condition.Split([" or "], StringSplitOptions.None))
        {
            var translated = TranslateAndGroup(group.Trim());
            if (translated == "1")
            {
                return "1";
            }

            if (translated != "0")
            {
                groups.Add(translated);
            }
        }

        return groups.Count switch
        {
            0 => "0",
            1 => groups[0],
            _ => string.Join(" || ", groups)
        };
    }

    private static string TranslateAndGroup(string group)
    {
        var relations = new List<string>();
        foreach (var relation in group.Split([" and "], StringSplitOptions.None))
        {
            var translated = TranslateRelation(relation.Trim());
            if (translated == "0")
            {
                return "0";
            }

            if (translated != "1")
            {
                relations.Add(translated);
            }
        }

        return relations.Count switch
        {
            0 => "1",
            1 => relations[0],
            _ => string.Join(" && ", relations)
        };
    }

    // relation = "expr = ranges" or "expr != ranges", where expr is "operand" or "operand % modulus".
    private static string TranslateRelation(string relation)
    {
        var negate = relation.Contains(" != ");
        var sides = relation.Split([negate ? " != " : " = "], 2, StringSplitOptions.None);
        (long Low, long High)[] ranges = ParseRanges(sides[1].Trim());

        if (!TryExpression(sides[0].Trim(), out var expr))
        {
            // A zero operand (v/w/f/t/e/c): the value is a constant 0 in gettext, so evaluate now.
            var matches = InSet(0, ranges);
            return (matches != negate) ? "1" : "0";
        }

        var membership = Membership(expr, ranges);
        return negate ? $"!({membership})" : membership;
    }

    // Returns the C expression for an integer operand expression, or false for a constant-zero operand.
    private static bool TryExpression(string expression, out string result)
    {
        var parts = expression.Split([" % "], 2, StringSplitOptions.None);
        var operand = parts[0].Trim()[0];
        if (operand is not ('n' or 'i'))
        {
            result = string.Empty;
            return false;
        }

        result = parts.Length == 1 ? "n" : $"(n % {parts[1].Trim()})";
        return true;
    }

    private static string Membership(string expr, (long Low, long High)[] ranges)
    {
        var parts = new List<string>(ranges.Length);
        foreach ((var low, var high) in ranges)
        {
            parts.Add(low == high
                ? $"{expr} == {low.ToString(CultureInfo.InvariantCulture)}"
                : $"({expr} >= {low.ToString(CultureInfo.InvariantCulture)} && {expr} <= {high.ToString(CultureInfo.InvariantCulture)})");
        }

        return parts.Count == 1 ? parts[0] : $"({string.Join(" || ", parts)})";
    }

    private static (long Low, long High)[] ParseRanges(string list)
    {
        var items = list.Split(',');
        var ranges = new (long Low, long High)[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            ranges[index] = ParseRange(items[index].Trim());
        }

        return ranges;
    }

    private static (long Low, long High) ParseRange(string item)
    {
        if (!item.Contains(".."))
        {
            var single = long.Parse(item, CultureInfo.InvariantCulture);
            return (single, single);
        }

        var bounds = item.Split([".."], 2, StringSplitOptions.None);
        return (long.Parse(bounds[0], CultureInfo.InvariantCulture), long.Parse(bounds[1], CultureInfo.InvariantCulture));
    }

    private static int IndexOf(IReadOnlyList<PluralCategory> order, PluralCategory category)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == category)
            {
                return index;
            }
        }

        return order.Count - 1;
    }

    private static bool InSet(long value, (long Low, long High)[] ranges)
    {
        foreach ((var low, var high) in ranges)
        {
            if (value >= low && value <= high)
            {
                return true;
            }
        }

        return false;
    }
}
