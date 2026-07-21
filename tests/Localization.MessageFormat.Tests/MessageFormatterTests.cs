using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class MessageFormatterTests
{
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl");
    private static readonly MessageFormatter _formatter = new();

    [Fact]
    public void Format_SimpleArgument_SubstitutesValue()
    {
        var result = _formatter.Format("Hello, {name}!", _english, ("name", "Ada"));

        Assert.Equal("Hello, Ada!", result);
    }

    [Fact]
    public void Format_QuotedLiteral_IsUnescaped()
    {
        var result = _formatter.Format("'{'literal'}'", _english);

        Assert.Equal("{literal}", result);
    }

    [Theory]
    [InlineData(0, "You have no messages")]
    [InlineData(1, "You have 1 message")]
    [InlineData(5, "You have 5 messages")]
    public void Format_Plural_SelectsBranchAndRendersPound(int count, string expected)
    {
        var result = _formatter.Format(
            "You have {count, plural, =0 {no messages} one {# message} other {# messages}}",
            _english,
            ("count", count));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_NaNPluralArgument_ThrowsMessageFormatExceptionNotOverflow()
    {
        // NaN/±Infinity are not representable as decimal; they must surface as the argument error, not a raw
        // OverflowException from the decimal cast.
        Assert.Throws<MessageFormatException>(() =>
            _formatter.Format("{count, plural, one {# item} other {# items}}", _english, ("count", double.NaN)));
    }

    [Fact]
    public void Format_Plural_UsesTargetCulturePluralRules()
    {
        // 5 is "many" in Polish, "other" in English — same Template, different branch.
        const string Template = "{count, plural, one {# plik} few {# pliki} many {# plików} other {# pliku}}";

        Assert.Equal("5 plików", _formatter.Format(Template, _polish, ("count", 5)));
        Assert.Equal("2 pliki", _formatter.Format(Template, _polish, ("count", 2)));
        Assert.Equal("1 plik", _formatter.Format(Template, _polish, ("count", 1)));
    }

    [Theory]
    [InlineData("1.0", "1 star")]     // displayed as "1" -> one
    [InlineData("1.50", "1.5 stars")] // displayed as "1.5" -> other
    [InlineData("2", "2 stars")]
    public void Format_DecimalPlural_SelectsByDisplayedDigits(string value, string expected)
    {
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        var result = _formatter.Format(
            "{n, plural, one {# star} other {# stars}}", _english, ("n", amount));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_PluralWithOffset_SubtractsOffsetFromPound()
    {
        var result = _formatter.Format(
            "{count, plural, offset:1 one {you and # other} other {you and # others}}",
            _english,
            ("count", 3));

        Assert.Equal("you and 2 others", result);
    }

    [Fact]
    public void Format_Select_ChoosesBranchByStringValue()
    {
        const string Template = "{gender, select, male {He} female {She} other {They}} replied";

        Assert.Equal("She replied", _formatter.Format(Template, _english, ("gender", "female")));
        Assert.Equal("They replied", _formatter.Format(Template, _english, ("gender", "unknown")));
    }

    [Fact]
    public void Format_NestedPluralInsideSelect_RendersCorrectly()
    {
        const string Template =
            "{gender, select, female {She has {count, plural, one {# cat} other {# cats}}} other {They have pets}}";

        Assert.Equal("She has 2 cats", _formatter.Format(Template, _english, ("gender", "female"), ("count", 2)));
    }

    [Fact]
    public void Format_MissingArgument_PassThroughByDefault()
    {
        var result = _formatter.Format("Hello, {name}!", _english);

        Assert.Equal("Hello, {name}!", result);
    }

    [Fact]
    public void Format_MissingArgument_ThrowsUnderThrowPolicy()
    {
        var strict = new MessageFormatter(MissingArgumentPolicy.Throw);

        MissingArgumentException exception =
            Assert.Throws<MissingArgumentException>(() => strict.Format("Hello, {name}!", _english));

        Assert.Equal("name", exception.ArgumentName);
    }

    [Fact]
    public void Format_TypedNumber_UsesCultureFormatting()
    {
        var result = _formatter.Format("{value, number, integer}", _english, ("value", 1234));

        Assert.Equal("1,234", result);
    }

    [Fact]
    public void Format_PoundAndDefaultNumber_GroupByCulture()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("1,234", _formatter.Format("{n, plural, other {#}}", _english, ("n", 1234)));
        Assert.Equal("1.234", _formatter.Format("{n, plural, other {#}}", german, ("n", 1234)));
        Assert.Equal("1,234", _formatter.Format("{n, number}", _english, ("n", 1234)));
        Assert.Equal("1.234,5", _formatter.Format("{n, number}", german, ("n", 1234.5)));
    }

    [Fact]
    public void Format_SelectMissingArgument_RespectsPolicy()
    {
        // PassThrough emits the placeholder; Throw raises — matching plural, not a silent fall to "other".
        Assert.Equal("{g}", _formatter.Format("{g, select, female {she} other {they}}", _english));

        var strict = new MessageFormatter(MissingArgumentPolicy.Throw);
        Assert.Throws<MissingArgumentException>(
            () => strict.Format("{g, select, female {she} other {they}}", _english));
    }

    [Fact]
    public void Format_NonNumericPluralArgument_ThrowsMessageFormatException() =>
        Assert.Throws<MessageFormatException>(
            () => _formatter.Format("{n, plural, other {#}}", _english, ("n", "not a number")));

    [Fact]
    public void Format_SuppliedNullPluralArgument_ThrowsMessageFormatExceptionNotMissing() =>
        Assert.Throws<MessageFormatException>(
            () => _formatter.Format("{n, plural, other {#}}", _english, ("n", null)));

    [Fact]
    public void Format_PoundInsideSelectInsidePlural_RendersThePluralNumber()
    {
        var result = _formatter.Format(
            "{n, plural, other {{g, select, other {#}}}}", _english, ("n", 5), ("g", "x"));

        Assert.Equal("5", result);
    }

    [Fact]
    public void Format_CurrencyCodeSkeleton_RendersSpecifiedCurrency()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("Price: $19.99", _formatter.Format("Price: {p, number, ::currency/USD}", _english, ("p", 19.99m)));
        Assert.Equal("Preis: 19,99\u00A0$", _formatter.Format("Preis: {p, number, ::currency/USD}", german, ("p", 19.99m)));
    }

    [Fact]
    public void Format_NamedCurrency_RendersCultureCurrency()
    {
        var enUs = CultureInfo.GetCultureInfo("en-US");

        Assert.Equal("$19.99", _formatter.Format("{p, number, currency}", enUs, ("p", 19.99m)));
        Assert.Equal("$5.00", _formatter.Format("{p, number, currency}", enUs, ("p", 5m)));
    }

    [Fact]
    public void Format_PercentStyle_IsIcuAligned()
    {
        Assert.Equal("50%", _formatter.Format("{r, number, percent}", _english, ("r", 0.5)));
    }

    [Fact]
    public void Format_FixedFractionSkeleton_RendersFixedDigits()
    {
        Assert.Equal("1.50", _formatter.Format("{q, number, ::.00}", _english, ("q", 1.5m)));
    }

    [Theory]
    [InlineData("{n, number, currnecy}")]        // typo -> unknown style
    [InlineData("{n, number, ::currency/US}")]   // malformed skeleton
    public void Format_InvalidNumberStyle_ThrowsAtParseIndependentOfValue(string template)
    {
        // Throws even though the number argument is absent, proving validation is at parse, not render.
        Assert.Throws<MessageFormatException>(() => _formatter.Format(template, _english));
    }

    [Fact]
    public void InvalidNumberStyle_IsReportedByTryValidate()
    {
        Assert.False(MessageSyntax.TryValidate("{n, number, ::scientific}", out MessageFormatError? error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("{v, number, ::percent unit-width-full-name}")]                    // unit-width is currency-only
    [InlineData("{v, number, ::compact-short currency/USD unit-width-iso-code}")]  // iso-code/full-name forbidden with compact currency
    public void Format_CurrencyWidthOnUnsupportedStem_ThrowsMessageFormatExceptionAtPositionMinusOne(string template)
    {
        // The width-rule validation runs at parse and carries no source offset, so Position is -1.
        MessageFormatException exception = Assert.Throws<MessageFormatException>(() => _formatter.Format(template, _english));
        Assert.Equal(-1, exception.Position);
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(21, "21st")]
    public void Format_SelectOrdinal_RendersEnglishOrdinalSuffixes(int value, string expected)
    {
        var result = _formatter.Format(
            "{n, selectordinal, one {#st} two {#nd} few {#rd} other {#th}}", _english, ("n", value));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, "ZERO")]
    [InlineData(1, "ONE")]
    [InlineData(2, "TWO")]
    [InlineData(3, "FEW")]
    [InlineData(5, "MANY")]
    [InlineData(10, "OTHER")]
    public void Format_SelectOrdinal_WelshSpansAllSixCategories(int value, string expected)
    {
        // Welsh ordinal rules are the only CLDR ordinal set that uses all six categories
        // (confirmed against Intl.PluralRules('cy', {type:'ordinal'})).
        const string Template =
            "{n, selectordinal, zero {ZERO} one {ONE} two {TWO} few {FEW} many {MANY} other {OTHER}}";
        var welsh = CultureInfo.GetCultureInfo("cy");

        Assert.Equal(expected, _formatter.Format(Template, welsh, ("n", value)));
    }

    [Fact]
    public void Format_PluralMissingOtherBranch_RendersEmptyStringForNonMatchingValue()
    {
        // No 'other' branch and 5 doesn't match 'one': the renderer silently drops to an empty message
        // rather than throwing (MessageRenderer.SelectPluralBranch falls through to EmptyMessage).
        var result = _formatter.Format("{n, plural, one {# item}}", _english, ("n", 5));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_PluralMissingArgument_RespectsPolicy()
    {
        // Mirrors Format_SelectMissingArgument_RespectsPolicy: plural must follow the same contract as select.
        Assert.Equal("{count}", _formatter.Format("{count, plural, other {#}}", _english));

        var strict = new MessageFormatter(MissingArgumentPolicy.Throw);
        MissingArgumentException exception = Assert.Throws<MissingArgumentException>(
            () => strict.Format("{count, plural, other {#}}", _english));
        Assert.Equal("count", exception.ArgumentName);
    }

    [Fact]
    public void Format_NumberArgument_WrongType_FallsBackToStringRepresentation()
    {
        // A string can't be converted to decimal; NumberFormatting.FormatSpec falls back to the value's own
        // string representation rather than throwing (string is not IFormattable, so value.ToString() wins).
        var result = _formatter.Format("{v, number, integer}", _english, ("v", "abc"));

        Assert.Equal("abc", result);
    }

    [Fact]
    public void Format_Select_NonStringArgumentValue_CoercesViaToString()
    {
        // RenderSelect keys branches on value?.ToString(); an int argument must coerce to its decimal string.
        var result = _formatter.Format("{code, select, 1 {one} 2 {two} other {other}}", _english, ("code", 1));

        Assert.Equal("one", result);
    }

    [Theory]
    [InlineData(0, "ZERO")]
    [InlineData(1, "ONE")]
    [InlineData(2, "TWO")]
    [InlineData(3, "FEW")]
    [InlineData(11, "MANY")]
    [InlineData(100, "OTHER")]
    public void Format_Plural_ArabicSpansAllSixCategories(int value, string expected)
    {
        const string Template =
            "{n, plural, zero {ZERO} one {ONE} two {TWO} few {FEW} many {MANY} other {OTHER}}";
        var arabic = CultureInfo.GetCultureInfo("ar");

        Assert.Equal(expected, _formatter.Format(Template, arabic, ("n", value)));
    }

    [Theory]
    [InlineData(1, "ONE")]
    [InlineData(2, "FEW")]
    [InlineData(5, "MANY")]
    [InlineData(11, "MANY")]
    [InlineData(21, "ONE")]
    public void Format_Plural_RussianDistinguishesFewAndMany(int value, string expected)
    {
        const string Template = "{n, plural, one {ONE} few {FEW} many {MANY} other {OTHER}}";
        var russian = CultureInfo.GetCultureInfo("ru");

        Assert.Equal(expected, _formatter.Format(Template, russian, ("n", value)));
    }

    [Theory]
    [InlineData(1, "ONE")]
    [InlineData(3, "FEW")]
    [InlineData(5, "OTHER")]
    public void Format_Plural_CzechDistinguishesFewFromOther(int value, string expected)
    {
        const string Template = "{n, plural, one {ONE} few {FEW} many {MANY} other {OTHER}}";
        var czech = CultureInfo.GetCultureInfo("cs");

        Assert.Equal(expected, _formatter.Format(Template, czech, ("n", value)));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("3.5")]
    [InlineData("100.5")]
    public void Format_Plural_CzechMf8Seam_AnyVisibleFractionForcesMany(string value)
    {
        // MF-8: category selection follows the value's *visible* fraction digits. Czech maps any fractional
        // value to "many" regardless of magnitude (3 alone would be "few"; 3.5 is "many").
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);
        const string Template = "{n, plural, one {ONE} few {FEW} many {MANY} other {OTHER}}";
        var czech = CultureInfo.GetCultureInfo("cs");

        Assert.Equal("MANY", _formatter.Format(Template, czech, ("n", amount)));
    }

    [Theory]
    [InlineData(0, "ZERO")]
    [InlineData(1, "ONE")]
    [InlineData(2, "TWO")]
    [InlineData(3, "FEW")]
    [InlineData(6, "MANY")]
    [InlineData(100, "OTHER")]
    public void Format_Plural_WelshSpansAllSixCategories(int value, string expected)
    {
        const string Template =
            "{n, plural, zero {ZERO} one {ONE} two {TWO} few {FEW} many {MANY} other {OTHER}}";
        var welsh = CultureInfo.GetCultureInfo("cy");

        Assert.Equal(expected, _formatter.Format(Template, welsh, ("n", value)));
    }

    [Fact]
    public void Format_QuotedPound_IsLiteralInsidePluralBranch()
    {
        // '#' quoted with apostrophes must render as a literal '#' character, distinct from the unquoted
        // PoundPart later in the same branch, which still substitutes the plural's number.
        var result = _formatter.Format("{n, plural, other {'#' of #}}", _english, ("n", 3));

        Assert.Equal("# of 3", result);
    }

    [Fact]
    public void Format_NestedPluralInsidePlural_RendersBothLevels()
    {
        const string Template =
            "{n, plural, other {# outer with {m, plural, one {# inner} other {# inners}}}}";

        Assert.Equal("5 outer with 1 inner", _formatter.Format(Template, _english, ("n", 5), ("m", 1)));
        Assert.Equal("5 outer with 2 inners", _formatter.Format(Template, _english, ("n", 5), ("m", 2)));
    }

    [Fact]
    public void Format_NestedSelectInsideSelect_RendersBothLevels()
    {
        const string Template =
            "{a, select, x {A:{b, select, y {AY} other {AOther}}} other {Other}}";

        Assert.Equal("A:AY", _formatter.Format(Template, _english, ("a", "x"), ("b", "y")));
        Assert.Equal("A:AOther", _formatter.Format(Template, _english, ("a", "x"), ("b", "z")));
        Assert.Equal("Other", _formatter.Format(Template, _english, ("a", "q"), ("b", "y")));
    }

    [Fact]
    public void Format_CompactWhitespaceGrammar_ParsesSameAsSpacedForm()
    {
        Assert.Equal("x", _formatter.Format("{n,plural,other{x}}", _english, ("n", 5)));
    }

    [Fact]
    public void Format_IrregularWhitespaceGrammar_ParsesSameAsSpacedForm()
    {
        const string Template = "{n,\n  plural,\n  one {# item}\n  other {# items}\n}";

        Assert.Equal("1 item", _formatter.Format(Template, _english, ("n", 1)));
        Assert.Equal("5 items", _formatter.Format(Template, _english, ("n", 5)));
    }

    [Theory]
    [InlineData("{name")]                                              // unterminated argument
    [InlineData("a } b")]                                               // stray closing brace
    [InlineData("{n, plural, banana {x} other {y}}")]                   // invalid plural category
    [InlineData("{n, plural, one {a} one {b} other {c}}")]              // duplicate plural selector
    [InlineData("{g, select, male {a} male {b} other {c}}")]            // duplicate select selector
    public void Format_GrammarParseErrors_ThrowThroughThePublicFormatEntryPoint(string template)
    {
        // These grammar errors are otherwise asserted only via MessageParser or MessageSyntax directly.
        // Format shares the same parse path through the template cache and must surface them the same way.
        Assert.Throws<MessageFormatException>(() => _formatter.Format(template, _english));
    }

    [Fact]
    public void Format_MalformedTemplate_NegativeCachesTheParseError()
    {
        // A malformed template must be parsed exactly once: after the first (throwing) Format, its parse
        // failure lives in the template cache as a negative entry, so a second Format never re-parses.
        var formatter = new MessageFormatter();
        const string Malformed = "{name";

        MessageFormatException thrown =
            Assert.Throws<MessageFormatException>(() => formatter.Format(Malformed, _english));

        Assert.True(formatter.TryGetCachedParseError(Malformed, out MessageFormatException? cached));
        Assert.NotNull(cached);
        Assert.Equal(thrown.Message, cached!.Message);
        Assert.Equal(thrown.Position, cached.Position);
    }

    [Fact]
    public void Format_SameMalformedTemplateTwice_ThrowsIdenticalMessageAndPosition()
    {
        // The cached rethrow must preserve the public contract: same exception type, Message, and Position
        // as the fresh parse would produce on every call.
        var formatter = new MessageFormatter();
        const string Malformed = "{n, plural, banana {x} other {y}}";

        MessageFormatException first =
            Assert.Throws<MessageFormatException>(() => formatter.Format(Malformed, _english));
        MessageFormatException second =
            Assert.Throws<MessageFormatException>(() => formatter.Format(Malformed, _english));

        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.Position, second.Position);
    }

    [Fact]
    public void Format_ThreeOrMoreArguments_AllSubstituted()
    {
        var result = _formatter.Format("{a} {b} {c}", _english, ("a", "1"), ("b", "2"), ("c", "3"));

        Assert.Equal("1 2 3", result);
    }

    [Fact]
    public void Format_UntypedArgument_NumericIFormattable_UsesCultureAwareToString()
    {
        // A bare {n} placeholder bound to a numeric value takes the IFormattable culture-aware path
        // (MessageRenderer.FormatValue), distinct from every other numeric test which goes through
        // 'number'/'plural'.
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("1234", _formatter.Format("{n}", _english, ("n", 1234)));
        Assert.Equal("1234.5", _formatter.Format("{n}", _english, ("n", 1234.5m)));
        Assert.Equal("1234,5", _formatter.Format("{n}", german, ("n", 1234.5m)));
    }

    [Fact]
    public void Format_UnknownTypeToken_FallsBackToFormattableCultureString()
    {
        // An unrecognized type keyword (neither number/date/time/plural/select/selectordinal) resolves no
        // format string (MessageRenderer.ResolveFormat returns null) and falls back to the default
        // IFormattable.ToString(null, culture) rendering rather than erroring.
        var result = _formatter.Format("{v, banana}", _english, ("v", 42));

        Assert.Equal("42", result);
    }

    [Fact]
    public void Format_ArgumentNameWithDigitsAndUnderscore_Substitutes()
    {
        var result = _formatter.Format("{arg_1}", _english, ("arg_1", "x"));

        Assert.Equal("x", result);
    }
}
