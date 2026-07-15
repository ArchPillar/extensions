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
