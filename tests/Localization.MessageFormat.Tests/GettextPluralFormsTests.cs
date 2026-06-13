namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class GettextPluralFormsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("pl")]
    [InlineData("ru")]
    [InlineData("cs")]
    [InlineData("ar")]
    [InlineData("ja")]
    public void GettextPluralForms_ExpressionSelectsTheSameFormAsTheRuntime(string culture)
    {
        var header = PluralRules.GettextPluralForms(culture);
        (var nplurals, var expression) = Parse(header);

        var order = PluralRules.GettextOrder(culture).ToList();
        Assert.Equal(order.Count, nplurals);

        // The C expression must, for every integer n, yield the index of the form the runtime resolves.
        for (var n = 0; n <= 200; n++)
        {
            var expected = order.IndexOf(PluralRules.Cardinal(culture, PluralRules.Operands(n)));
            Assert.Equal(expected, (int)Evaluate(expression, n));
        }
    }

    [Fact]
    public void GettextPluralForms_ForThreePlusFormLanguage_IsARealExpressionNotZero()
    {
        // The bug was emitting "plural=0" for every language with more than two forms.
        var header = PluralRules.GettextPluralForms("pl");
        Assert.Contains("nplurals=4", header);
        Assert.DoesNotContain("plural=0;", header);
        Assert.Contains("n % 10", header);
    }

    private static (int Nplurals, string Expression) Parse(string header)
    {
        var parts = header.Split(';');
        var nplurals = int.Parse(parts[0].Split('=')[1].Trim());
        var expression = parts[1].Split(['='], 2)[1].Trim();
        return (nplurals, expression);
    }

    // A minimal evaluator for the C plural expression subset we emit: ternary ?:, || && !, the comparisons
    // == != >= <= > <, %, parentheses, integer literals, and the variable n.
    private static long Evaluate(string expression, long n) => new Evaluator(expression, n).Ternary();

    private sealed class Evaluator(string text, long n)
    {
        private int _pos;

        public long Ternary()
        {
            var condition = Or();
            if (Match("?"))
            {
                var whenTrue = Ternary();
                Expect(":");
                var whenFalse = Ternary();
                return condition != 0 ? whenTrue : whenFalse;
            }

            return condition;
        }

        private long Or()
        {
            var value = And();
            while (Match("||"))
            {
                value = (value != 0) | (And() != 0) ? 1 : 0;
            }

            return value;
        }

        private long And()
        {
            var value = Comparison();
            while (Match("&&"))
            {
                value = (value != 0) & (Comparison() != 0) ? 1 : 0;
            }

            return value;
        }

        private long Comparison()
        {
            var left = Modulo();
            foreach (var op in new[] { "==", "!=", ">=", "<=", ">", "<" })
            {
                if (Match(op))
                {
                    var right = Modulo();
                    var result = op switch
                    {
                        "==" => left == right,
                        "!=" => left != right,
                        ">=" => left >= right,
                        "<=" => left <= right,
                        ">" => left > right,
                        _ => left < right
                    };
                    return result ? 1 : 0;
                }
            }

            return left;
        }

        private long Modulo()
        {
            var value = Unary();
            while (Match("%"))
            {
                value %= Unary();
            }

            return value;
        }

        private long Unary()
        {
            if (Match("!"))
            {
                return Unary() == 0 ? 1 : 0;
            }

            return Primary();
        }

        private long Primary()
        {
            SkipSpace();
            if (Match("("))
            {
                var value = Ternary();
                Expect(")");
                return value;
            }

            if (Peek() == 'n')
            {
                _pos++;
                return n;
            }

            var start = _pos;
            while (_pos < text.Length && char.IsDigit(text[_pos]))
            {
                _pos++;
            }

            return long.Parse(text[start.._pos]);
        }

        private bool Match(string token)
        {
            SkipSpace();
            if (string.CompareOrdinal(text, _pos, token, 0, token.Length) == 0)
            {
                _pos += token.Length;
                return true;
            }

            return false;
        }

        private void Expect(string token)
        {
            if (!Match(token))
            {
                throw new FormatException($"Expected '{token}' at {_pos} in '{text}'.");
            }
        }

        private char Peek()
        {
            SkipSpace();
            return _pos < text.Length ? text[_pos] : '\0';
        }

        private void SkipSpace()
        {
            while (_pos < text.Length && text[_pos] == ' ')
            {
                _pos++;
            }
        }
    }
}
