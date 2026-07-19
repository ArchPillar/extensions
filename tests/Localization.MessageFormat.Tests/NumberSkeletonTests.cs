using ArchPillar.Extensions.Localization.MessageFormat.Internal;

namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class NumberSkeletonTests
{
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
    public void Parse_CombinedStems_MergeIntoOneSpec()
    {
        NumberFormatSpec spec = NumberSkeleton.Parse("::currency/USD .00");

        Assert.Equal(NumberUnit.Currency, spec.Unit);
        Assert.Equal("USD", spec.CurrencyCode);
        Assert.Equal(2, spec.MinFractionDigits);
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

    [Fact]
    public void Parse_CompactPercent_Throws()
    {
        Assert.Throws<MessageFormatException>(() => NumberSkeleton.Parse("::percent compact-short"));
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
}
