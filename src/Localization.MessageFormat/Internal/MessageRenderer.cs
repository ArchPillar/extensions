using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Renders a parsed <see cref="Message"/> against an argument set and a target culture: substitutes
/// arguments, resolves <c>plural</c>/<c>selectordinal</c> categories (in the target culture) and
/// <c>select</c> branches, and renders <c>#</c> as the value minus the construct's offset.
/// </summary>
/// <remarks>
/// A stack-only <c>ref struct</c> carries the render-constant context — the output builder, culture,
/// argument set and missing-argument policy — as fields, so the recursive walk threads only <c>pound</c>
/// (the enclosing plural's value), which alone varies per branch, and the renderer itself costs no heap
/// allocation. The hot path is allocation-conscious: a literal-only message returns its text directly,
/// arguments are looked up over the argument array rather than a dictionary, and a thread-local
/// <see cref="StringBuilder"/> is reused. A simple substitution then allocates only the rendered result;
/// plural and number rendering additionally allocate the small operand and culture-fallback intermediates.
/// </remarks>
internal readonly ref struct MessageRenderer
{
    [ThreadStatic]
    private static StringBuilder? _pooledBuilder;

    private readonly StringBuilder _builder;
    private readonly CultureInfo _culture;
    private readonly (string Name, object? Value)[] _arguments;
    private readonly MissingArgumentPolicy _policy;

    private MessageRenderer(
        StringBuilder builder,
        CultureInfo culture,
        (string Name, object? Value)[] arguments,
        MissingArgumentPolicy policy)
    {
        _builder = builder;
        _culture = culture;
        _arguments = arguments;
        _policy = policy;
    }

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
            new MessageRenderer(builder, culture, arguments, policy).RenderInto(message, pound: null);
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

    private void RenderInto(Message message, decimal? pound)
    {
        foreach (MessagePart part in message.Parts)
        {
            RenderPart(part, pound);
        }
    }

    private void RenderPart(MessagePart part, decimal? pound)
    {
        switch (part)
        {
            case LiteralPart literal:
                _builder.Append(literal.Text);
                break;
            case PoundPart:
                _builder.Append(NumberFormatting.Format(pound ?? 0m, null, _culture));
                break;
            case ArgumentPart argument:
                RenderArgument(argument);
                break;
            case PluralPart plural:
                RenderPlural(plural);
                break;
            case SelectPart select:
                RenderSelect(select, pound);
                break;
            default:
                break;
        }
    }

    private void RenderArgument(ArgumentPart argument)
    {
        if (!TryGetArgument(argument.Name, out var value))
        {
            AppendMissing(argument.Name);
            return;
        }

        _builder.Append(argument.Type is null
            ? FormatValue(value)
            : FormatTyped(value, argument.Type, argument.Style));
    }

    private void RenderPlural(PluralPart plural)
    {
        if (!TryGetArgument(plural.ArgumentName, out var value))
        {
            AppendMissing(plural.ArgumentName);
            return;
        }

        // A supplied-but-null or non-numeric argument is a caller error, not a missing translation argument,
        // so report it as a format error (with an accurate message) rather than "no value was supplied".
        if (!TryToNumber(value, out var number))
        {
            throw new MessageFormatException($"Argument '{plural.ArgumentName}' is not a number.", -1);
        }

        Message branch = SelectPluralBranch(plural, number);
        RenderInto(branch, number - plural.Offset);
    }

    private Message SelectPluralBranch(PluralPart plural, decimal number)
    {
        if (TryExplicitBranch(plural, number, out Message? exact))
        {
            return exact!;
        }

        var adjusted = number - plural.Offset;
        PluralOperands operands = PluralRules.Operands(adjusted, NumberFormatting.VisibleFractionDigits(adjusted));
        PluralCategory category = plural.Ordinal
            ? PluralRules.Ordinal(_culture.Name, operands)
            : PluralRules.Cardinal(_culture.Name, operands);
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

    private void RenderSelect(SelectPart select, decimal? pound)
    {
        if (!TryGetArgument(select.ArgumentName, out var value))
        {
            AppendMissing(select.ArgumentName);
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
            RenderInto(branch, pound);
        }
    }

    private bool TryGetArgument(string name, out object? value)
    {
        foreach ((var argumentName, var argumentValue) in _arguments)
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

    private void AppendMissing(string name)
    {
        if (_policy == MissingArgumentPolicy.Throw)
        {
            throw new MissingArgumentException(name);
        }

        _builder.Append('{').Append(name).Append('}');
    }

    private string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, _culture)
            : value.ToString() ?? string.Empty;
    }

    private string FormatTyped(object? value, string type, string? style)
    {
        if (type == "number")
        {
            return NumberFormatting.Format(value, style, _culture);
        }

        if (value is not IFormattable formattable)
        {
            return value?.ToString() ?? string.Empty;
        }

        return formattable.ToString(ResolveFormat(type, style), _culture);
    }

    private static string? ResolveFormat(string type, string? style) => type switch
    {
        "date" => DateStyle(style),
        "time" => TimeStyle(style),
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

    private static bool TryToNumber(object? value, out decimal number) =>
        NumberFormatting.TryToDecimal(value, out number);

    private static Message EmptyMessage { get; } = new([]);
}
