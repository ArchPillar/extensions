using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Renders a parsed <see cref="Message"/> against an argument set and a target culture: substitutes
/// arguments, resolves <c>plural</c>/<c>selectordinal</c> categories (in the target culture) and
/// <c>select</c> branches, and renders <c>#</c> as the value minus the construct's offset.
/// </summary>
internal static class MessageRenderer
{
    public static string Render(
        Message message,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy)
    {
        var builder = new StringBuilder();
        RenderInto(builder, message, culture, arguments, policy, pound: null);
        return builder.ToString();
    }

    private static void RenderInto(
        StringBuilder builder,
        Message message,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy,
        decimal? pound)
    {
        foreach (MessagePart part in message.Parts)
        {
            RenderPart(builder, part, culture, arguments, policy, pound);
        }
    }

    private static void RenderPart(
        StringBuilder builder,
        MessagePart part,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy,
        decimal? pound)
    {
        switch (part)
        {
            case LiteralPart literal:
                builder.Append(literal.Text);
                break;
            case PoundPart:
                builder.Append(FormatValue(pound ?? 0m, culture));
                break;
            case ArgumentPart argument:
                RenderArgument(builder, argument, culture, arguments, policy);
                break;
            case PluralPart plural:
                RenderPlural(builder, plural, culture, arguments, policy);
                break;
            case SelectPart select:
                RenderSelect(builder, select, culture, arguments, policy);
                break;
            default:
                break;
        }
    }

    private static void RenderArgument(
        StringBuilder builder,
        ArgumentPart argument,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy)
    {
        if (!arguments.TryGetValue(argument.Name, out var value))
        {
            AppendMissing(builder, argument.Name, policy);
            return;
        }

        builder.Append(argument.Type is null
            ? FormatValue(value, culture)
            : FormatTyped(value, argument.Type, argument.Style, culture));
    }

    private static void RenderPlural(
        StringBuilder builder,
        PluralPart plural,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy)
    {
        if (!arguments.TryGetValue(plural.ArgumentName, out var value) || value is null)
        {
            AppendMissing(builder, plural.ArgumentName, policy);
            return;
        }

        var number = ToNumber(value);
        Message branch = SelectPluralBranch(plural, number, culture);
        RenderInto(builder, branch, culture, arguments, policy, number - plural.Offset);
    }

    private static Message SelectPluralBranch(PluralPart plural, decimal number, CultureInfo culture)
    {
        if (TryExplicitBranch(plural, number, out Message? exact))
        {
            return exact!;
        }

        PluralOperands operands = PluralRules.Operands(number - plural.Offset);
        PluralCategory category = plural.Ordinal
            ? PluralRules.Ordinal(culture.Name, operands)
            : PluralRules.Cardinal(culture.Name, operands);
        return FindCategoryBranch(plural, category)
            ?? FindCategoryBranch(plural, PluralCategory.Other)
            ?? EmptyMessage;
    }

    private static bool TryExplicitBranch(PluralPart plural, decimal number, out Message? branch)
    {
        foreach (KeyValuePair<PluralSelector, Message> pair in plural.Branches)
        {
            if (pair.Key.ExplicitValue is int explicitValue && explicitValue == number)
            {
                branch = pair.Value;
                return true;
            }
        }

        branch = null;
        return false;
    }

    private static Message? FindCategoryBranch(PluralPart plural, PluralCategory category)
    {
        foreach (KeyValuePair<PluralSelector, Message> pair in plural.Branches)
        {
            if (pair.Key.Category == category)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static void RenderSelect(
        StringBuilder builder,
        SelectPart select,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        MissingArgumentPolicy policy)
    {
        arguments.TryGetValue(select.ArgumentName, out var value);
        var key = value?.ToString() ?? string.Empty;
        if (!select.Branches.TryGetValue(key, out Message? branch))
        {
            select.Branches.TryGetValue("other", out branch);
        }

        if (branch is not null)
        {
            RenderInto(builder, branch, culture, arguments, policy, pound: null);
        }
    }

    private static void AppendMissing(StringBuilder builder, string name, MissingArgumentPolicy policy)
    {
        if (policy == MissingArgumentPolicy.Throw)
        {
            throw new MissingArgumentException(name);
        }

        builder.Append('{').Append(name).Append('}');
    }

    private static string FormatValue(object? value, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, culture)
            : value.ToString() ?? string.Empty;
    }

    private static string FormatTyped(object? value, string type, string? style, CultureInfo culture)
    {
        if (value is not IFormattable formattable)
        {
            return value?.ToString() ?? string.Empty;
        }

        return formattable.ToString(ResolveFormat(type, style), culture);
    }

    private static string? ResolveFormat(string type, string? style) => type switch
    {
        "number" => NumberStyle(style),
        "date" => DateStyle(style),
        "time" => TimeStyle(style),
        _ => null
    };

    private static string? NumberStyle(string? style) => style switch
    {
        "integer" => "N0",
        "percent" => "P",
        "currency" => "C",
        _ => null
    };

    private static string? DateStyle(string? style) => style switch
    {
        "short" => "d",
        "medium" => "g",
        "long" => "D",
        "full" => "F",
        _ => null
    };

    private static string? TimeStyle(string? style) => style switch
    {
        "short" => "t",
        _ => "T"
    };

    private static decimal ToNumber(object value) => value switch
    {
        decimal d => d,
        double db => (decimal)db,
        float fl => (decimal)fl,
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
    };

    private static Message EmptyMessage { get; } = new([]);
}
