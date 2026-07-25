namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class PluralRulesTests
{
    [Theory]
    // English: one only for an integer 1.
    [InlineData("en", 0, PluralCategory.Other)]
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en", 2, PluralCategory.Other)]
    // Polish: one / few / many.
    [InlineData("pl", 1, PluralCategory.One)]
    [InlineData("pl", 2, PluralCategory.Few)]
    [InlineData("pl", 4, PluralCategory.Few)]
    [InlineData("pl", 5, PluralCategory.Many)]
    [InlineData("pl", 22, PluralCategory.Few)]
    [InlineData("pl", 25, PluralCategory.Many)]
    [InlineData("pl", 112, PluralCategory.Many)]
    // Czech.
    [InlineData("cs", 1, PluralCategory.One)]
    [InlineData("cs", 3, PluralCategory.Few)]
    [InlineData("cs", 5, PluralCategory.Other)]
    // Russian.
    [InlineData("ru", 1, PluralCategory.One)]
    [InlineData("ru", 2, PluralCategory.Few)]
    [InlineData("ru", 5, PluralCategory.Many)]
    [InlineData("ru", 11, PluralCategory.Many)]
    [InlineData("ru", 21, PluralCategory.One)]
    // Arabic: the full six-way split.
    [InlineData("ar", 0, PluralCategory.Zero)]
    [InlineData("ar", 1, PluralCategory.One)]
    [InlineData("ar", 2, PluralCategory.Two)]
    [InlineData("ar", 3, PluralCategory.Few)]
    [InlineData("ar", 11, PluralCategory.Many)]
    [InlineData("ar", 100, PluralCategory.Other)]
    // Welsh.
    [InlineData("cy", 3, PluralCategory.Few)]
    [InlineData("cy", 6, PluralCategory.Many)]
    // Japanese has a single form.
    [InlineData("ja", 1, PluralCategory.Other)]
    [InlineData("ja", 5, PluralCategory.Other)]
    // Base-language fallback (de-AT -> de) and unknown cultures.
    [InlineData("de-AT", 1, PluralCategory.One)]
    [InlineData("de-AT", 2, PluralCategory.Other)]
    [InlineData("xx", 1, PluralCategory.Other)]
    public void Cardinal_MatchesCldr(string culture, int value, PluralCategory expected) =>
        Assert.Equal(expected, PluralRules.Cardinal(culture, PluralRules.Operands(value, 0)));

    [Theory]
    // English ordinals: 1st, 2nd, 3rd, 4th, ... 11th/12th/13th, 21st.
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en", 2, PluralCategory.Two)]
    [InlineData("en", 3, PluralCategory.Few)]
    [InlineData("en", 4, PluralCategory.Other)]
    [InlineData("en", 11, PluralCategory.Other)]
    [InlineData("en", 21, PluralCategory.One)]
    // Welsh ordinals span all six categories.
    [InlineData("cy", 0, PluralCategory.Zero)]
    [InlineData("cy", 1, PluralCategory.One)]
    [InlineData("cy", 3, PluralCategory.Few)]
    [InlineData("cy", 5, PluralCategory.Many)]
    [InlineData("cy", 10, PluralCategory.Other)]
    // A language without ordinal rules falls back to other.
    [InlineData("pl", 3, PluralCategory.Other)]
    public void Ordinal_MatchesCldr(string culture, int value, PluralCategory expected) =>
        Assert.Equal(expected, PluralRules.Ordinal(culture, PluralRules.Operands(value, 0)));

    [Fact]
    public void Cardinal_UsesVisibleFractionDigits()
    {
        // English: with one visible fraction digit (v != 0) 1.0 is "other", not "one".
        Assert.Equal(PluralCategory.Other, PluralRules.Cardinal("en", PluralRules.Operands(1.0m, 1)));
        // With zero visible digits the same value is "one".
        Assert.Equal(PluralCategory.One, PluralRules.Cardinal("en", PluralRules.Operands(1m, 0)));
        // Czech: any fractional value is "many".
        Assert.Equal(PluralCategory.Many, PluralRules.Cardinal("cs", PluralRules.Operands(1.5m, 1)));
        // Polish: a fractional value falls through to "other".
        Assert.Equal(PluralCategory.Other, PluralRules.Cardinal("pl", PluralRules.Operands(1.5m, 1)));
    }

    [Theory]
    [InlineData("1", 0, 1, 0, 0, 0, 0)]
    [InlineData("1.0", 1, 1, 1, 0, 0, 0)]
    [InlineData("1.50", 2, 1, 2, 1, 50, 5)]
    [InlineData("123", 0, 123, 0, 0, 0, 0)]
    public void Operands_ComputesCldrOperands(string value, int visibleFractionDigits, long i, int v, int w, long f, long t)
    {
        PluralOperands operands = PluralRules.Operands(
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), visibleFractionDigits);

        Assert.Equal(i, operands.I);
        Assert.Equal(v, operands.V);
        Assert.Equal(w, operands.W);
        Assert.Equal(f, operands.F);
        Assert.Equal(t, operands.T);
    }

    [Fact]
    public void Operands_VeryLargeValue_DoesNotOverflow()
    {
        // Values with more than 19 integer/fraction digits formerly threw OverflowException via long.Parse.
        Exception? exception = Record.Exception(() =>
        {
            PluralRules.Operands(123456789012345678901234m, 0);   // 24 integer digits
            PluralRules.Operands(0.12345678901234567890123m, 23); // 23 fraction digits
            PluralRules.Cardinal("en", PluralRules.Operands(123456789012345678901234m, 0));
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Operands_VisibleFractionDigits_PadWithZeros()
    {
        PluralOperands operands = PluralRules.Operands(1m, 2);

        Assert.Equal(2, operands.V);
        Assert.Equal(0, operands.W);
        Assert.Equal(0, operands.F);
    }

    [Fact]
    public void Operands_ExponentOmitted_DefaultsToZero()
    {
        // The exponent parameter is optional and additive (m7): every non-compact caller (the standard
        // number/message path) omits it, so E must stay 0 -- the byte-identity invariant for non-compact
        // plural selection.
        PluralOperands operands = PluralRules.Operands(2m, 0);

        Assert.Equal(0, operands.E);
    }

    [Fact]
    public void Operands_Exponent_IsThreadedIntoOperands()
    {
        PluralOperands operands = PluralRules.Operands(2m, 0, 6);

        Assert.Equal(6, operands.E);
    }

    [Theory]
    // m7: CldrPluralData's "Many" rule for these Romance languages is
    // "e = 0 and i != 0 and i % 1000000 = 0 and v = 0 or e != 0..5" -- the "e != 0..5" disjunct is how CLDR
    // selects "many" for a million-scale-and-above COMPACT value. Before the fix, e was hard-coded 0 for
    // every caller, so this disjunct could never fire and a mantissa outside 0..1 (e.g. compacted "2") wrongly
    // fell through to "other". With the true compact exponent (6 for a million-scale bucket, verified against
    // the ICU oracle in CompactNotationTests -- log10(divisor), divisor 1,000,000) threaded in, it resolves
    // to "many" as CLDR's rule requires. (The rendered compact SUFFIX text does not change for these locales,
    // because CLDR-48 does not define a distinct "many" compact pattern for them -- verified by exhaustively
    // scanning CldrCompactData.g.cs: no compact bucket for ca/es/fr/it/lld/pt/scn/vec carries a
    // PluralCategory.Many variant, so pattern selection falls back to "other" either way. The category itself
    // is still wrong today, and this is the CLDR-correct, oracle-consistent value TR35 requires.)
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("ca")]
    public void Cardinal_CompactExponent_SelectsManyForMillionScaleMantissa(string culture)
    {
        // Mantissa 2 (e.g. the compacted "2" of "2M"/"2 millions"): the "one" rule (i = 0,1 / n = 1) never
        // matches, so before the fix this always fell through to "other".
        Assert.Equal(PluralCategory.Other, PluralRules.Cardinal(culture, PluralRules.Operands(2m, 0)));
        Assert.Equal(PluralCategory.Many, PluralRules.Cardinal(culture, PluralRules.Operands(2m, 0, 6)));
    }

    [Theory]
    // Regression: the FIRST disjunct of the same "Many" rule ("e = 0 and i != 0 and i % 1000000 = 0 and
    // v = 0") is the STANDARD (non-compact) path -- an exact multiple of a million typed out in full, e.g.
    // French/Spanish "2 000 000 d'habitants" needs "many" for its unit-pattern elision. This must stay
    // byte-identical before and after m7: it already worked at the default exponent 0 and must keep working.
    [InlineData("fr")]
    [InlineData("es")]
    public void Cardinal_StandardExactMillion_StillSelectsMany_NonCompactPathUnaffected(string culture)
    {
        Assert.Equal(PluralCategory.Many, PluralRules.Cardinal(culture, PluralRules.Operands(2000000m, 0)));
    }

    [Fact]
    public void GettextOrder_ReturnsUsedCategories_OtherLast()
    {
        Assert.Equal(new[] { PluralCategory.One, PluralCategory.Other }, PluralRules.GettextOrder("en"));
        Assert.Equal(
            new[] { PluralCategory.One, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other },
            PluralRules.GettextOrder("pl"));
        Assert.Equal(new[] { PluralCategory.Other }, PluralRules.GettextOrder("ja"));
    }

    [Fact]
    public void CldrVersion_IsRecorded()
    {
        Assert.Equal("48", PluralRules.CldrVersion);
    }
}
