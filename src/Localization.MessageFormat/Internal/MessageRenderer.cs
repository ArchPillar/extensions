using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Renders a parsed <see cref="Message"/> against an argument set and a target culture: substitutes
/// arguments, resolves <c>plural</c>/<c>selectordinal</c> categories (in the target culture) and
/// <c>select</c> branches, and renders <c>#</c> as the value minus the construct's offset.
/// </summary>
/// <remarks>
/// The hot path is allocation-conscious: a literal-only message returns its text directly (no
/// allocation), arguments are looked up over the argument array rather than a dictionary, and a
/// thread-local <see cref="StringBuilder"/> is reused so a dynamic render allocates only the result.
/// </remarks>
internal static class MessageRenderer
{
    [ThreadStatic]
    private static StringBuilder? _pooledBuilder;

    public static string Render(
        Message message,
        CultureInfo culture,
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy)
    {
        if (TryGetLiteral(message, out var literal))
        {
            return literal;
        }

        StringBuilder builder = _pooledBuilder ?? new StringBuilder();
        _pooledBuilder = null;
        try
        {
            builder.Clear();
            RenderInto(builder, message, culture, arguments, policy, pound: null);
            return builder.ToString();
        }
        finally
        {
            _pooledBuilder = builder;
        }
    }

    private static bool TryGetLiteral(Message message, out string literal)
    {
        if (message.Parts.Count == 0)
        {
            literal = string.Empty;
            return true;
        }

        if (message.Parts.Count == 1 && message.Parts[0] is LiteralPart only)
        {
            literal = only.Text;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static void RenderInto(
        StringBuilder builder,
        Message message,
        CultureInfo culture,
        (string Name, object? Value)[] arguments,
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
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy,
        decimal? pound)
    {
        switch (part)
        {
            case LiteralPart literal:
                builder.Append(literal.Text);
                break;
            case PoundPart:
                builder.Append(FormatNumber(pound ?? 0m, culture));
                break;
            case ArgumentPart argument:
                RenderArgument(builder, argument, culture, arguments, policy);
                break;
            case PluralPart plural:
                RenderPlural(builder, plural, culture, arguments, policy);
                break;
            case SelectPart select:
                RenderSelect(builder, select, culture, arguments, policy, pound);
                break;
            default:
                break;
        }
    }

    private static void RenderArgument(
        StringBuilder builder,
        ArgumentPart argument,
        CultureInfo culture,
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy)
    {
        if (!TryGetArgument(arguments, argument.Name, out var value))
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
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy)
    {
        if (!TryGetArgument(arguments, plural.ArgumentName, out var value))
        {
            AppendMissing(builder, plural.ArgumentName, policy);
            return;
        }

        // A supplied-but-null or non-numeric argument is a caller error, not a missing translation argument,
        // so report it as a format error (with an accurate message) rather than "no value was supplied".
        if (!TryToNumber(value, out var number))
        {
            throw new MessageFormatException($"Argument '{plural.ArgumentName}' is not a number.", -1);
        }

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
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy,
        decimal? pound)
    {
        if (!TryGetArgument(arguments, select.ArgumentName, out var value))
        {
            AppendMissing(builder, select.ArgumentName, policy);
            return;
        }

        var key = value?.ToString() ?? string.Empty;
        if (!select.Branches.TryGetValue(key, out Message? branch))
        {
            select.Branches.TryGetValue("other", out branch);
        }

        if (branch is not null)
        {
            // Thread the enclosing plural's number so a '#' inside a select-within-a-plural renders it.
            RenderInto(builder, branch, culture, arguments, policy, pound);
        }
    }

    private static bool TryGetArgument(
        (string Name, object? Value)[] arguments,
        string name,
        out object? value)
    {
        foreach ((var argumentName, var argumentValue) in arguments)
        {
            if (string.Equals(argumentName, name, StringComparison.Ordinal))
            {
                value = argumentValue;
                return true;
            }
        }

        value = null;
        return false;
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

        // A plain "{n, number}" (no/unknown style) uses the locale's default number format, which groups —
        // matching the "integer" style and ICU. Only the explicit styles take a fixed format string.
        if (type == "number" && NumberStyle(style) is null)
        {
            return FormatNumber(value, culture);
        }

        return formattable.ToString(ResolveFormat(type, style), culture);
    }

    // Formats a number with the locale's grouping separators (ICU's default for "#" and "{n, number}"):
    // grouped, and up to three fraction digits with trailing zeros trimmed — so an integer groups with no
    // decimals and a fractional value keeps its digits.
    private static string FormatNumber(object? value, CultureInfo culture) =>
        value is IFormattable formattable
            ? formattable.ToString("#,##0.###", culture)
            : value?.ToString() ?? string.Empty;

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

    private static bool TryToNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal d:
                number = d;
                return true;
            case double db:
                number = (decimal)db;
                return true;
            case float fl:
                number = (decimal)fl;
                return true;
            case null:
                number = 0m;
                return false;
            default:
                try
                {
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    number = 0m;
                    return false;
                }
        }
    }

    private static Message EmptyMessage { get; } = new([]);
}
