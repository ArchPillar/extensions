using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class NumberSkeletonTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Parse_Currency_SetsUnitAndCode()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD");

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal("USD", spec.CurrencyCode);
    }

    [Theory]
    [InlineData("::.00", 2, 2)]
    [InlineData("::.##", 0, 2)]
    [InlineData("::.0#", 1, 2)]
    [InlineData("::.0", 1, 1)]
    [InlineData("::.#", 0, 1)]
    public void Parse_Fraction_SetsMinMax(string skeleton, int min, int max)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);

        Assert.Equal(min, spec.MinFractionDigits);
        Assert.Equal(max, spec.MaxFractionDigits);
    }

    [Theory]
    [InlineData("::precision-integer")]
    [InlineData("::.")]
    public void Parse_Integer_SetsZeroFractionDigits(string skeleton)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);

        Assert.Equal(0, spec.MinFractionDigits);
        Assert.Equal(0, spec.MaxFractionDigits);
    }

    [Fact]
    public void Parse_Percent_SetsUnit()
    {
        Assert.Equal(NumberUnit.Percent, NumberSkeleton.Parse("::percent").Unit);
        Assert.Equal(NumberUnit.Percent, NumberSkeleton.Parse("::%").Unit);
    }

    [Fact]
    public void Parse_GroupOff_DisablesGrouping()
    {
        Assert.False(NumberSkeleton.Parse("::group-off").Grouping);
        Assert.True(NumberSkeleton.Parse("::group-auto").Grouping);
    }

    [Fact]
    public void Parse_GroupOffAlias_DisablesGrouping()
    {
        Assert.False(NumberSkeleton.Parse("::,_").Grouping);
    }

    [Fact]
    public void Parse_ConflictingGroupStems_LastStemWins()
    {
        Assert.True(NumberSkeleton.Parse("::group-off group-auto").Grouping);
        Assert.False(NumberSkeleton.Parse("::group-auto group-off").Grouping);
    }

    [Fact]
    public void Parse_CombinedStems_MergeIntoOneSpec()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD .00");

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal("USD", spec.CurrencyCode);
        Assert.Equal(2, spec.MinFractionDigits);
        Assert.Equal(2, spec.MaxFractionDigits);
    }

    [Fact]
    public void Parse_ConflictingUnitStems_LastStemWins()
    {
        NumberFormatSpec percentThenCurrency = NumberSkeleton.Parse("::percent currency/USD");
        Assert.Equal(NumberUnit.Currency, percentThenCurrency.Unit);
        Assert.Equal("USD", percentThenCurrency.CurrencyCode);

        NumberFormatSpec currencyThenPercent = NumberSkeleton.Parse("::currency/USD percent");
        Assert.Equal(NumberUnit.Percent, currencyThenPercent.Unit);
        Assert.Equal("USD", currencyThenPercent.CurrencyCode);
    }

    [Fact]
    public void Parse_ConflictingFractionStems_LastStemWins()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::.00 .##");

        Assert.Equal(0, spec.MinFractionDigits);
        Assert.Equal(2, spec.MaxFractionDigits);
    }

    [Theory]
    [InlineData("::scientific")]
    [InlineData("::unit/length-meter")]
    [InlineData("::currency/US")]        // malformed: not 3 letters
    [InlineData("::currency/")]          // malformed: no code
    [InlineData("::.0a")]                // malformed fraction
    [InlineData("::.#0")]                // malformed: # before 0
    public void Parse_UnsupportedOrMalformed_Throws(string skeleton)
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
    }

    [Theory]
    [InlineData("::compact-short")]
    [InlineData("::K")]
    public void Parse_CompactShortStems_SetShortNotation(string skeleton)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);
        Assert.Equal(NumberNotation.CompactShort, spec.Notation);
        Assert.Equal(NumberUnit.Decimal, spec.Unit);
    }

    [Theory]
    [InlineData("::compact-long")]
    [InlineData("::KK")]
    public void Parse_CompactLongStems_SetLongNotation(string skeleton)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);
        Assert.Equal(NumberNotation.CompactLong, spec.Notation);
        Assert.Equal(NumberUnit.Decimal, spec.Unit);
    }

    [Fact]
    public void Parse_CompactCurrency_KeepsCurrencyUnit()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD compact-short");
        Assert.Equal(NumberNotation.CompactShort, spec.Notation);
        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal("USD", spec.CurrencyCode);
    }

    [Fact]
    public void Parse_DefaultSkeleton_IsStandardNotation()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD");
        Assert.Equal(NumberNotation.Standard, spec.Notation);
    }

    [Theory]
    [InlineData("::compact-short .00")]
    [InlineData("::.00 compact-short")]
    [InlineData("::compact-long precision-integer")]
    public void Parse_CompactWithFractionOverride_Throws(string skeleton)
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
    }

    [Theory]
    [InlineData("::percent compact-short")]
    [InlineData("::percent compact-long")]
    public void Parse_CompactPercent_Throws(string skeleton)
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
    }

    [Fact]
    public void Parse_CompactWithGroupOff_Succeeds()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::compact-short group-off");
        Assert.Equal(NumberNotation.CompactShort, spec.Notation);
        Assert.False(spec.Grouping);
    }

    [Fact]
    public void Parse_CurrencyWithGroupOff_DisablesGroupingKeepsCurrency()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD group-off");

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal("USD", spec.CurrencyCode);
        Assert.False(spec.Grouping);
    }

    [Theory]
    [InlineData("::currency/USD .0#", 1, 2)]
    [InlineData("::currency/USD .##", 0, 2)]
    public void Parse_CurrencyWithRangedFraction_IsSupported(string skeleton, int min, int max)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal(min, spec.MinFractionDigits);
        Assert.Equal(max, spec.MaxFractionDigits);
    }

    [Theory]
    [InlineData("::currency/usd", "usd")]   // lowercase is valid (resolves case-insensitively)
    [InlineData("::currency/ABC", "ABC")]   // unassigned 3-letter code is valid at parse (format-only check)
    [InlineData("::currency/ZZZ", "ZZZ")]
    public void Parse_Currency_ThreeAsciiLetters_IsValid(string skeleton, string expectedCode)
    {
        NumberFormatSpec spec = NumberSkeleton.Parse(skeleton);

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal(expectedCode, spec.CurrencyCode);
    }

    [Fact]
    public void Parse_Currency_LowercaseCode_RendersSymbolCaseInsensitively()
    {
        // usd resolves the same $ symbol as USD in en-US — ICU treats the code case-insensitively.
        var rendered = NumberFormatting.Format(19.99m, "::currency/usd", _en);
        Assert.Equal("$19.99", rendered);
    }

    [Theory]
    [InlineData("::currency/123")]    // digits, not letters
    [InlineData("::currency/u$d")]    // symbol embedded
    [InlineData("::currency/US$")]    // symbol at the end
    [InlineData("::currency/US")]     // too short
    [InlineData("::currency/USDX")]   // too long
    [InlineData("::currency/")]       // empty code — same length gate, zero letters
    public void Parse_Currency_NotThreeAsciiLetters_Throws(string skeleton)
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
    }

    [Fact]
    public void Parse_Currency_Invalid_ReportsUntrackedPosition()
    {
        // Number-style validation errors carry the documented -1 (offset is not tracked at this point).
        MessageFormatException ex = Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse("::currency/123"));
        Assert.Equal(-1, ex.Position);
    }

    [Theory]
    [InlineData("::.0a")]                   // malformed fraction stem
    [InlineData("::scientific")]            // unsupported stem
    [InlineData("::percent compact-short")] // post-loop compact+percent validation error
    public void Parse_OtherInvalidFamilies_ReportUntrackedPosition(string skeleton)
    {
        MessageFormatException ex = Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
        Assert.Equal(-1, ex.Position);
    }

    [Fact]
    public void Parse_EmptySkeleton_MatchesDefaultSpec()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::");

        Assert.Equal(NumberFormatSpec.Default, spec);
        Assert.Equal(NumberUnit.Decimal, spec.Unit);
        Assert.Null(spec.CurrencyCode);
        Assert.Null(spec.MinFractionDigits);
        Assert.Null(spec.MaxFractionDigits);
        Assert.True(spec.Grouping);
        Assert.Equal(NumberNotation.Standard, spec.Notation);
    }

    [Fact]
    public void Resolve_EmptyStyle_ReturnsDefaultSpec()
    {
        Assert.Equal(NumberFormatSpec.Default, NumberFormatting.Resolve(""));
    }

    [Theory]
    [InlineData("::currency/USD", (int)CurrencyWidth.Short)]
    [InlineData("::currency/USD unit-width-short", (int)CurrencyWidth.Short)]
    [InlineData("::currency/USD unit-width-narrow", (int)CurrencyWidth.Narrow)]
    [InlineData("::currency/USD unit-width-iso-code", (int)CurrencyWidth.IsoCode)]
    [InlineData("::currency/USD unit-width-full-name", (int)CurrencyWidth.FullName)]
    public void Parse_CurrencyWidth_SetsWidth(string skeleton, int expected)
    {
        Assert.Equal(expected, (int)NumberSkeleton.Parse(skeleton).Width);
    }

    [Fact]
    public void Parse_WidthOrderIndependent_SetsWidth()
    {
        Assert.Equal(CurrencyWidth.IsoCode, NumberSkeleton.Parse("::unit-width-iso-code currency/USD").Width);
    }

    [Theory]
    [InlineData("::percent unit-width-full-name")]
    [InlineData("::unit-width-narrow")]
    [InlineData("::.00 unit-width-narrow")]
    public void Parse_WidthWithoutCurrency_Throws(string skeleton)
    {
        MessageFormatException ex = Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton));
        Assert.Equal(-1, ex.Position);
        // Keeps this body distinct from sibling throw-tests so SonarAnalyzer S4144 (identical method bodies) stays quiet in the test-project Release build.
        Assert.NotEmpty(ex.Message);
    }

    [Theory]
    [InlineData("::compact-short currency/USD unit-width-iso-code")]
    [InlineData("::currency/USD unit-width-full-name compact-long")]
    public void Parse_WidthWithCompact_Throws(string skeleton)
    {
        Assert.Equal(-1, Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse(skeleton)).Position);
    }

    [Fact]
    public void Parse_UnknownWidth_Throws()
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse("::currency/USD unit-width-bogus"));
    }
}
