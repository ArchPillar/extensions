using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// A single-pass recursive-descent parser for the supported ICU MessageFormat grammar. One instance
/// parses one source string; it is not reusable or thread-safe.
/// </summary>
internal sealed class MessageGrammarParser
{
    private readonly string _text;
    private int _pos;

    public MessageGrammarParser(string text)
    {
        _text = text;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    public Message ParseFull()
    {
        Message message = ParseMessage(allowPound: false);
        if (!AtEnd)
        {
            throw Error("Unexpected '}'.");
        }

        return message;
    }

    private Message ParseMessage(bool allowPound)
    {
        var parts = new List<MessagePart>();
        var literal = new StringBuilder();
        while (!AtEnd && Current != '}')
        {
            var c = Current;
            if (c == '{')
            {
                FlushLiteral(parts, literal);
                parts.Add(ParseArgument(allowPound));
            }
            else if (allowPound && c == '#')
            {
                FlushLiteral(parts, literal);
                parts.Add(PoundPart.Instance);
                _pos++;
            }
            else if (c == '\'')
            {
                AppendQuoted(literal);
            }
            else
            {
                literal.Append(c);
                _pos++;
            }
        }

        FlushLiteral(parts, literal);
        return new Message(parts);
    }

    private static void FlushLiteral(List<MessagePart> parts, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        parts.Add(new LiteralPart(literal.ToString()));
        literal.Clear();
    }

    private void AppendQuoted(StringBuilder literal)
    {
        var next = _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';
        if (next == '\'')
        {
            literal.Append('\'');
            _pos += 2;
            return;
        }

        if (!IsSyntax(next))
        {
            literal.Append('\'');
            _pos++;
            return;
        }

        AppendQuotedRun(literal);
    }

    private void AppendQuotedRun(StringBuilder literal)
    {
        _pos++;
        while (!AtEnd)
        {
            if (Current != '\'')
            {
                literal.Append(Current);
                _pos++;
                continue;
            }

            if (_pos + 1 < _text.Length && _text[_pos + 1] == '\'')
            {
                literal.Append('\'');
                _pos += 2;
                continue;
            }

            _pos++;
            return;
        }
    }

    // allowPound carries whether '#' is active in the current context (true inside a plural), so a select
    // nested in a plural keeps '#' as the plural number while a top-level select treats it as a literal.
    private MessagePart ParseArgument(bool allowPound)
    {
        _pos++;
        SkipWhitespace();
        var name = ReadIdentifier();
        SkipWhitespace();
        if (Current == '}')
        {
            _pos++;
            return new ArgumentPart(name, null, null);
        }

        Expect(',');
        SkipWhitespace();
        var type = ReadKeyword();
        return type switch
        {
            "plural" => FinishPlural(name, ordinal: false),
            "selectordinal" => FinishPlural(name, ordinal: true),
            "select" => FinishSelect(name, allowPound),
            _ => FinishSimple(name, type)
        };
    }

    private MessagePart FinishSimple(string name, string type)
    {
        SkipWhitespace();
        string? style = null;
        if (Current == ',')
        {
            _pos++;
            style = ReadStyle();
        }

        Expect('}');
        return new ArgumentPart(name, type, style);
    }

    private MessagePart FinishPlural(string name, bool ordinal)
    {
        SkipWhitespace();
        Expect(',');
        var offset = ReadOffset();
        IReadOnlyDictionary<PluralSelector, Message> branches = ReadPluralBranches();
        Expect('}');
        return new PluralPart(name, ordinal, offset, branches);
    }

    private MessagePart FinishSelect(string name, bool allowPound)
    {
        SkipWhitespace();
        Expect(',');
        IReadOnlyDictionary<string, Message> branches = ReadSelectBranches(allowPound);
        Expect('}');
        return new SelectPart(name, branches);
    }

    private int ReadOffset()
    {
        SkipWhitespace();
        if (!Matches("offset:"))
        {
            return 0;
        }

        _pos += "offset:".Length;
        var value = ReadInteger();
        SkipWhitespace();
        return value;
    }

    private IReadOnlyDictionary<PluralSelector, Message> ReadPluralBranches()
    {
        var branches = new Dictionary<PluralSelector, Message>();
        SkipWhitespace();
        while (!AtEnd && Current != '}')
        {
            PluralSelector selector = ReadPluralSelector();
            Message body = ReadBranchBody(allowPound: true);
            if (!branches.ContainsKey(selector))
            {
                branches.Add(selector, body);
            }

            SkipWhitespace();
        }

        return branches;
    }

    private IReadOnlyDictionary<string, Message> ReadSelectBranches(bool allowPound)
    {
        var branches = new Dictionary<string, Message>(StringComparer.Ordinal);
        SkipWhitespace();
        while (!AtEnd && Current != '}')
        {
            var key = ReadKeyword();
            Message body = ReadBranchBody(allowPound);
            if (!branches.ContainsKey(key))
            {
                branches.Add(key, body);
            }

            SkipWhitespace();
        }

        return branches;
    }

    private Message ReadBranchBody(bool allowPound)
    {
        SkipWhitespace();
        Expect('{');
        Message body = ParseMessage(allowPound);
        Expect('}');
        return body;
    }

    private PluralSelector ReadPluralSelector()
    {
        if (Current == '=')
        {
            _pos++;
            return new PluralSelector(ReadInteger(), null);
        }

        var keyword = ReadKeyword();
        return new PluralSelector(null, MapCategory(keyword));
    }

    private PluralCategory MapCategory(string keyword) => keyword switch
    {
        "zero" => PluralCategory.Zero,
        "one" => PluralCategory.One,
        "two" => PluralCategory.Two,
        "few" => PluralCategory.Few,
        "many" => PluralCategory.Many,
        "other" => PluralCategory.Other,
        _ => throw Error($"Invalid plural category '{keyword}'.")
    };

    private string ReadIdentifier()
    {
        var builder = new StringBuilder();
        while (!AtEnd && IsIdentifierChar(Current))
        {
            builder.Append(Current);
            _pos++;
        }

        if (builder.Length == 0)
        {
            throw Error("Expected an argument name.");
        }

        return builder.ToString();
    }

    private string ReadKeyword()
    {
        var builder = new StringBuilder();
        while (!AtEnd && !IsKeywordTerminator(Current))
        {
            builder.Append(Current);
            _pos++;
        }

        if (builder.Length == 0)
        {
            throw Error("Expected a keyword.");
        }

        return builder.ToString();
    }

    private string ReadStyle()
    {
        SkipWhitespace();
        var builder = new StringBuilder();
        while (!AtEnd && Current != '}')
        {
            builder.Append(Current);
            _pos++;
        }

        return builder.ToString().TrimEnd();
    }

    private int ReadInteger()
    {
        var builder = new StringBuilder();
        while (!AtEnd && char.IsDigit(Current))
        {
            builder.Append(Current);
            _pos++;
        }

        if (builder.Length == 0)
        {
            throw Error("Expected an integer.");
        }

        return int.Parse(builder.ToString(), CultureInfo.InvariantCulture);
    }

    private void SkipWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(Current))
        {
            _pos++;
        }
    }

    private bool Matches(string token)
    {
        if (_pos + token.Length > _text.Length)
        {
            return false;
        }

        return string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0;
    }

    private void Expect(char expected)
    {
        if (AtEnd || Current != expected)
        {
            throw Error($"Expected '{expected}'.");
        }

        _pos++;
    }

    private MessageFormatException Error(string message) => new(message, _pos);

    private static bool IsSyntax(char c) => c is '{' or '}' or '#';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsKeywordTerminator(char c) =>
        char.IsWhiteSpace(c) || c is '{' or '}' or ',';
}
