using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Rewrites a well-formed ICU MessageFormat string so that every <c>plural</c>/<c>selectordinal</c>/
/// <c>select</c> construct missing its required <c>other</c> branch gains an empty one. It tracks source
/// offsets (the parser discards them) with the same brace and apostrophe-quoting rules as the grammar, so
/// the insertion lands exactly before each construct's closing brace. One instance rewrites one string.
/// </summary>
internal sealed class OtherBranchInserter
{
    private const string OtherBranch = " other {}";

    private readonly string _text;
    private readonly List<int> _offsets = [];
    private int _pos;

    private OtherBranchInserter(string text)
    {
        _text = text;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    public static string Insert(string text)
    {
        var inserter = new OtherBranchInserter(text);
        inserter.ScanMessage();
        if (inserter._offsets.Count == 0)
        {
            return text;
        }

        inserter._offsets.Sort();
        var builder = new StringBuilder(text);
        for (var i = inserter._offsets.Count - 1; i >= 0; i--)
        {
            builder.Insert(inserter._offsets[i], OtherBranch);
        }

        return builder.ToString();
    }

    private void ScanMessage()
    {
        while (!AtEnd && Current != '}')
        {
            var c = Current;
            if (c == '{')
            {
                ScanArgument();
            }
            else if (c == '\'')
            {
                SkipQuoted();
            }
            else
            {
                _pos++;
            }
        }
    }

    private void ScanArgument()
    {
        _pos++;
        SkipWhitespace();
        ReadToken();
        SkipWhitespace();
        if (Current == '}')
        {
            _pos++;
            return;
        }

        _pos++;
        SkipWhitespace();
        var type = ReadToken();
        switch (type)
        {
            case "plural":
            case "selectordinal":
                ScanBranches(allowNumeric: true);
                break;
            case "select":
                ScanBranches(allowNumeric: false);
                break;
            default:
                ScanToClose();
                break;
        }
    }

    private void ScanBranches(bool allowNumeric)
    {
        SkipWhitespace();
        if (Current == ',')
        {
            _pos++;
        }

        SkipWhitespace();
        if (allowNumeric && Matches("offset:"))
        {
            _pos += "offset:".Length;
            ReadToken();
        }

        var hasOther = false;
        SkipWhitespace();
        while (!AtEnd && Current != '}')
        {
            if (allowNumeric && Current == '=')
            {
                _pos++;
                ReadToken();
            }
            else if (string.Equals(ReadToken(), "other", StringComparison.Ordinal))
            {
                hasOther = true;
            }

            SkipWhitespace();
            if (Current == '{')
            {
                _pos++;
                ScanMessage();
                if (Current == '}')
                {
                    _pos++;
                }
            }

            SkipWhitespace();
        }

        if (!hasOther)
        {
            _offsets.Add(_pos);
        }

        if (Current == '}')
        {
            _pos++;
        }
    }

    private void ScanToClose()
    {
        while (!AtEnd && Current != '}')
        {
            _pos++;
        }

        if (Current == '}')
        {
            _pos++;
        }
    }

    private void SkipQuoted()
    {
        var next = _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';
        if (next == '\'')
        {
            _pos += 2;
            return;
        }

        if (next is not ('{' or '}' or '#'))
        {
            _pos++;
            return;
        }

        _pos++;
        while (!AtEnd)
        {
            if (Current != '\'')
            {
                _pos++;
                continue;
            }

            if (_pos + 1 < _text.Length && _text[_pos + 1] == '\'')
            {
                _pos += 2;
                continue;
            }

            _pos++;
            return;
        }
    }

    private string ReadToken()
    {
        var builder = new StringBuilder();
        while (!AtEnd && !IsTerminator(Current))
        {
            builder.Append(Current);
            _pos++;
        }

        return builder.ToString();
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

    private static bool IsTerminator(char c) => char.IsWhiteSpace(c) || c is '{' or '}' or ',';
}
