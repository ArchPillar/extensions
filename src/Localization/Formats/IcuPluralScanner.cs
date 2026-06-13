using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// A clean top-level ICU cardinal plural: its argument name and its category-keyword branches (the raw
/// branch body text). Only the gettext-representable shape is captured.
/// </summary>
internal sealed class IcuPluralShape
{
    public required string ArgumentName { get; init; }

    public required IReadOnlyDictionary<PluralCategory, string> Branches { get; init; }
}

/// <summary>Maps between a <see cref="PluralCategory"/> and its ICU keyword.</summary>
internal static class PluralCategoryKeyword
{
    public static string Of(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other"
    };

    public static bool TryParse(string keyword, out PluralCategory category)
    {
        switch (keyword)
        {
            case "zero": category = PluralCategory.Zero; return true;
            case "one": category = PluralCategory.One; return true;
            case "two": category = PluralCategory.Two; return true;
            case "few": category = PluralCategory.Few; return true;
            case "many": category = PluralCategory.Many; return true;
            case "other": category = PluralCategory.Other; return true;
            default: category = PluralCategory.Other; return false;
        }
    }
}

/// <summary>
/// Recognizes a message that is exactly one top-level ICU cardinal <c>plural</c> with category-keyword
/// branches (no <c>selectordinal</c>, no <c>offset</c>, no explicit <c>=N</c> selectors, no surrounding
/// text). Anything else is reported as not scannable, so the caller keeps it as opaque ICU.
/// </summary>
internal static class IcuPluralScanner
{
    public static bool TryScan(string text, out IcuPluralShape? shape)
    {
        shape = null;
        try
        {
            return new Scanner(text).Run(out shape);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Scanner
    {
        private readonly string _text;
        private int _pos;

        public Scanner(string text)
        {
            _text = text;
        }

        private bool AtEnd => _pos >= _text.Length;

        private char Current => _pos < _text.Length ? _text[_pos] : '\0';

        public bool Run(out IcuPluralShape? shape)
        {
            shape = null;
            SkipWhitespace();
            Expect('{');
            SkipWhitespace();
            var name = ReadIdentifier();
            if (name.Length == 0 || !HasPluralType())
            {
                return false;
            }

            if (Matches("offset:"))
            {
                return false;
            }

            if (!TryReadBranches(out IReadOnlyDictionary<PluralCategory, string>? branches))
            {
                return false;
            }

            Expect('}');
            SkipWhitespace();
            if (!AtEnd)
            {
                return false;
            }

            shape = new IcuPluralShape { ArgumentName = name, Branches = branches! };
            return true;
        }

        private bool HasPluralType()
        {
            SkipWhitespace();
            Expect(',');
            SkipWhitespace();
            var type = ReadWord();
            SkipWhitespace();
            Expect(',');
            SkipWhitespace();
            return type == "plural";
        }

        private bool TryReadBranches(out IReadOnlyDictionary<PluralCategory, string>? branches)
        {
            var result = new Dictionary<PluralCategory, string>();
            branches = result;
            while (!AtEnd && Current != '}')
            {
                var selector = ReadWord();
                if (!PluralCategoryKeyword.TryParse(selector, out PluralCategory category))
                {
                    return false;
                }

                SkipWhitespace();
                result[category] = ExtractBody();
                SkipWhitespace();
            }

            return true;
        }

        private string ExtractBody()
        {
            Expect('{');
            var start = _pos;
            var depth = 1;
            while (!AtEnd && depth > 0)
            {
                depth += StepBody();
            }

            if (depth != 0)
            {
                throw new FormatException("Unterminated plural branch.");
            }

            var body = _text[start..(_pos - 1)];
            return body;
        }

        private int StepBody()
        {
            var c = _text[_pos];
            if (c == '\'')
            {
                SkipQuote();
                return 0;
            }

            _pos++;
            return c switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };
        }

        private void SkipQuote()
        {
            var next = _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';
            if (next == '\'')
            {
                _pos += 2;
                return;
            }

            if (next is not '{' and not '}' and not '#')
            {
                _pos++;
                return;
            }

            _pos++;
            while (!AtEnd && _text[_pos] != '\'')
            {
                _pos++;
            }

            if (!AtEnd)
            {
                _pos++;
            }
        }

        private string ReadIdentifier()
        {
            var start = _pos;
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            {
                _pos++;
            }

            return _text[start.._pos];
        }

        private string ReadWord()
        {
            var start = _pos;
            while (!AtEnd && !char.IsWhiteSpace(Current) && Current is not '{' and not '}' and not ',')
            {
                _pos++;
            }

            return _text[start.._pos];
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current))
            {
                _pos++;
            }
        }

        private bool Matches(string token) =>
            _pos + token.Length <= _text.Length && string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0;

        private void Expect(char expected)
        {
            if (AtEnd || Current != expected)
            {
                throw new FormatException($"Expected '{expected}'.");
            }

            _pos++;
        }
    }
}
