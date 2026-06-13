using System.Collections.Concurrent;
using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Evaluates a CLDR plural-rule condition (for example <c>v = 0 and i % 10 = 2..4</c>) against a set
/// of <see cref="PluralOperands"/>. Conditions are compiled to predicates lazily and cached, so each
/// distinct condition string is parsed only once.
/// </summary>
internal static class CldrRuleEvaluator
{
    private static readonly ConcurrentDictionary<string, Func<PluralOperands, bool>> _cache =
        new(StringComparer.Ordinal);

    public static bool Matches(string condition, PluralOperands operands)
    {
        Func<PluralOperands, bool> predicate = _cache.GetOrAdd(condition, Compile);
        return predicate(operands);
    }

    private static Func<PluralOperands, bool> Compile(string condition)
    {
        Func<PluralOperands, bool>[] orGroups = Split(condition, " or ", CompileAndGroup);
        return operands => MatchesAny(orGroups, operands);
    }

    private static Func<PluralOperands, bool> CompileAndGroup(string group)
    {
        Func<PluralOperands, bool>[] relations = Split(group, " and ", CompileRelation);
        return operands => MatchesAll(relations, operands);
    }

    private static Func<PluralOperands, bool> CompileRelation(string relation)
    {
        var negate = relation.Contains(" != ");
        var sides = relation.Split([negate ? " != " : " = "], 2, StringSplitOptions.None);
        Func<PluralOperands, decimal> expression = CompileExpression(sides[0].Trim());
        (long Low, long High)[] ranges = ParseRanges(sides[1].Trim());
        return operands => InSet(expression(operands), ranges) != negate;
    }

    private static Func<PluralOperands, decimal> CompileExpression(string expression)
    {
        var parts = expression.Split([" % "], 2, StringSplitOptions.None);
        var operand = parts[0].Trim()[0];
        if (parts.Length == 1)
        {
            return operands => OperandValue(operand, operands);
        }

        var modulus = long.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        return operands => OperandValue(operand, operands) % modulus;
    }

    private static decimal OperandValue(char operand, PluralOperands operands) => operand switch
    {
        'n' => operands.N,
        'i' => operands.I,
        'v' => operands.V,
        'w' => operands.W,
        'f' => operands.F,
        't' => operands.T,
        'e' => operands.E,
        'c' => operands.C,
        _ => operands.N
    };

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
        var low = long.Parse(bounds[0], CultureInfo.InvariantCulture);
        var high = long.Parse(bounds[1], CultureInfo.InvariantCulture);
        return (low, high);
    }

    private static bool InSet(decimal value, (long Low, long High)[] ranges)
    {
        if (value != decimal.Truncate(value))
        {
            return false;
        }

        foreach ((var low, var high) in ranges)
        {
            if (value >= low && value <= high)
            {
                return true;
            }
        }

        return false;
    }

    private static Func<PluralOperands, bool>[] Split(
        string text,
        string separator,
        Func<string, Func<PluralOperands, bool>> factory)
    {
        var parts = text.Split([separator], StringSplitOptions.None);
        var result = new Func<PluralOperands, bool>[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            result[index] = factory(parts[index].Trim());
        }

        return result;
    }

    private static bool MatchesAny(Func<PluralOperands, bool>[] predicates, PluralOperands operands)
    {
        foreach (Func<PluralOperands, bool> predicate in predicates)
        {
            if (predicate(operands))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAll(Func<PluralOperands, bool>[] predicates, PluralOperands operands)
    {
        foreach (Func<PluralOperands, bool> predicate in predicates)
        {
            if (!predicate(operands))
            {
                return false;
            }
        }

        return true;
    }
}
