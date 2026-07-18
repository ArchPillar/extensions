using System.Collections.Concurrent;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Parses the supported subset of CLDR number patterns (the standard decimal/percent/currency patterns and,
/// later, compact patterns) into <see cref="NumberPattern"/>. Sole owner of the pattern grammar: quoted
/// literals (<c>'…'</c>, <c>''</c> = apostrophe), <c>¤</c>/<c>¤¤</c>, <c>%</c>, <c>-</c>, and an optional
/// <c>positive;negative</c> subpattern pair. Parsed once per distinct pattern (bounded: patterns come from
/// the pinned CLDR tables). Unsupported syntax throws <see cref="FormatException"/> — pinned data is
/// validated at generation, so a runtime throw is a bug guard, not a user error.
/// </summary>
internal static class NumberPatternParser
{
    private static readonly ConcurrentDictionary<string, NumberPattern> _cache = new(StringComparer.Ordinal);

    /// <summary>Parses <paramref name="pattern"/>, returning a cached instance for a repeated pattern string.</summary>
    /// <param name="pattern">A CLDR number pattern, e.g. <c>#,##0.00</c> or <c>¤#,##0.00;¤-#,##0.00</c>.</param>
    /// <exception cref="FormatException">The pattern uses syntax this parser does not support.</exception>
    public static NumberPattern Parse(string pattern) => _cache.GetOrAdd(pattern, ParseCore);

    private static NumberPattern ParseCore(string pattern)
    {
        var separator = IndexOfUnquoted(pattern, ';');
        if (separator >= 0 && IndexOfUnquoted(pattern, ';', separator + 1) >= 0)
        {
            throw new FormatException($"Pattern '{pattern}' has more than one subpattern separator.");
        }

        var positive = separator < 0 ? pattern : Slice(pattern, 0, separator);
        (List<PatternToken> positivePrefix, var body, List<PatternToken> positiveSuffix) = SplitSubpattern(positive);
        (var minInteger, var minFraction, var maxFraction) = ParseBody(body, pattern);

        List<PatternToken>? negativePrefix = null;
        List<PatternToken>? negativeSuffix = null;
        if (separator >= 0)
        {
            // Per CLDR, the negative subpattern contributes affixes only; its digit body is ignored.
            var negative = Slice(pattern, separator + 1, pattern.Length);
            (negativePrefix, _, negativeSuffix) = SplitSubpattern(negative);
        }

        return new NumberPattern(
            positivePrefix, positiveSuffix, negativePrefix, negativeSuffix, minInteger, minFraction, maxFraction);
    }

    // Locates the digit body (the span from the first to the last unquoted '#' or '0') and parses the
    // affixes on either side.
    private static (List<PatternToken> Prefix, string Body, List<PatternToken> Suffix) SplitSubpattern(string text)
    {
        var start = -1;
        var end = -1;
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '\'')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && c is '#' or '0')
            {
                if (start < 0)
                {
                    start = index;
                }

                end = index;
            }
        }

        if (start < 0)
        {
            throw new FormatException($"Pattern segment '{text}' has no digit body.");
        }

        List<PatternToken> prefix = ParseAffix(Slice(text, 0, start));
        var body = Slice(text, start, end + 1);
        List<PatternToken> suffix = ParseAffix(Slice(text, end + 1, text.Length));
        return (prefix, body, suffix);
    }

    private static List<PatternToken> ParseAffix(string text)
    {
        var tokens = new List<PatternToken>();
        var literal = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\'')
            {
                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    literal.Append('\'');
                    index += 2;
                    continue;
                }

                index++;
                while (index < text.Length)
                {
                    if (text[index] == '\'')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '\'')
                        {
                            literal.Append('\'');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    literal.Append(text[index]);
                    index++;
                }

                continue;
            }

            if (c == '¤')
            {
                var run = 1;
                while (index + run < text.Length && text[index + run] == '¤')
                {
                    run++;
                }

                Flush(tokens, literal);
                tokens.Add(run switch
                {
                    1 => new PatternToken(PatternTokenKind.CurrencySymbol, string.Empty),
                    2 => new PatternToken(PatternTokenKind.CurrencyCode, string.Empty),
                    _ => throw new FormatException($"Affix '{text}' uses an unsupported currency placeholder run of {run}.")
                });
                index += run;
                continue;
            }

            if (c == '%')
            {
                Flush(tokens, literal);
                tokens.Add(new PatternToken(PatternTokenKind.PercentSign, string.Empty));
                index++;
                continue;
            }

            if (c == '‰')
            {
                throw new FormatException($"Affix '{text}' uses the unsupported per-mille sign.");
            }

            if (c == '-')
            {
                Flush(tokens, literal);
                tokens.Add(new PatternToken(PatternTokenKind.MinusSign, string.Empty));
                index++;
                continue;
            }

            literal.Append(c);
            index++;
        }

        Flush(tokens, literal);
        return tokens;
    }

    private static void Flush(List<PatternToken> tokens, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        tokens.Add(new PatternToken(PatternTokenKind.Literal, literal.ToString()));
        literal.Clear();
    }

    private static (int MinInteger, int MinFraction, int MaxFraction) ParseBody(string body, string pattern)
    {
        var dot = body.IndexOf('.');
        var integerPart = dot < 0 ? body : Slice(body, 0, dot);
        var fractionPart = dot < 0 ? string.Empty : Slice(body, dot + 1, body.Length);

        var minInteger = 0;
        foreach (var c in integerPart)
        {
            if (c == '0')
            {
                minInteger++;
            }
            else if (c is not ('#' or ','))
            {
                throw new FormatException($"Pattern '{pattern}' has an unsupported integer body character '{c}'.");
            }
        }

        var minFraction = 0;
        var maxFraction = 0;
        var optional = false;
        foreach (var c in fractionPart)
        {
            if (c == '0')
            {
                if (optional)
                {
                    throw new FormatException($"Pattern '{pattern}' places '0' after '#' in the fraction body.");
                }

                minFraction++;
                maxFraction++;
            }
            else if (c == '#')
            {
                optional = true;
                maxFraction++;
            }
            else
            {
                throw new FormatException($"Pattern '{pattern}' has an unsupported fraction body character '{c}'.");
            }
        }

        return (minInteger, minFraction, maxFraction);
    }

    private static int IndexOfUnquoted(string text, char target, int start = 0)
    {
        var quoted = false;
        for (var index = start; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Slice(string text, int start, int end) =>
#if NETSTANDARD2_0
        text.Substring(start, end - start);
#else
        text[start..end];
#endif
}
