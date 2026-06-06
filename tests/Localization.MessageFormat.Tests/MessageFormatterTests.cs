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
    public void Format_Plural_UsesTargetCulturePluralRules()
    {
        // 5 is "many" in Polish, "other" in English — same Template, different branch.
        const string Template = "{count, plural, one {# plik} few {# pliki} many {# plików} other {# pliku}}";

        Assert.Equal("5 plików", _formatter.Format(Template, _polish, ("count", 5)));
        Assert.Equal("2 pliki", _formatter.Format(Template, _polish, ("count", 2)));
        Assert.Equal("1 plik", _formatter.Format(Template, _polish, ("count", 1)));
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
}
