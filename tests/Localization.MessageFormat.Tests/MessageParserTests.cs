namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class MessageParserTests
{
    [Fact]
    public void Parse_PlainText_ProducesSingleLiteral()
    {
        Message message = MessageParser.Parse("Hello, world");

        MessagePart part = Assert.Single(message.Parts);
        LiteralPart literal = Assert.IsType<LiteralPart>(part);
        Assert.Equal("Hello, world", literal.Text);
    }

    [Fact]
    public void Parse_QuotedBrace_IsLiteralText()
    {
        Message message = MessageParser.Parse("'{'not an arg'}'");

        LiteralPart literal = Assert.IsType<LiteralPart>(Assert.Single(message.Parts));
        Assert.Equal("{not an arg}", literal.Text);
    }

    [Fact]
    public void Parse_DoubledApostrophe_IsLiteralApostrophe()
    {
        Message message = MessageParser.Parse("it''s");

        LiteralPart literal = Assert.IsType<LiteralPart>(Assert.Single(message.Parts));
        Assert.Equal("it's", literal.Text);
    }

    [Fact]
    public void Parse_LoneApostrophe_IsLiteralApostrophe()
    {
        Message message = MessageParser.Parse("o'clock");

        LiteralPart literal = Assert.IsType<LiteralPart>(Assert.Single(message.Parts));
        Assert.Equal("o'clock", literal.Text);
    }

    [Fact]
    public void Parse_SimpleArgument_ProducesArgumentPart()
    {
        Message message = MessageParser.Parse("Hi {name}!");

        Assert.Collection(
            message.Parts,
            p => Assert.Equal("Hi ", Assert.IsType<LiteralPart>(p).Text),
            p =>
            {
                ArgumentPart argument = Assert.IsType<ArgumentPart>(p);
                Assert.Equal("name", argument.Name);
                Assert.Null(argument.Type);
                Assert.Null(argument.Style);
            },
            p => Assert.Equal("!", Assert.IsType<LiteralPart>(p).Text));
    }

    [Fact]
    public void Parse_TypedArgumentWithStyle_CapturesTypeAndStyle()
    {
        Message message = MessageParser.Parse("{when, date, long}");

        ArgumentPart argument = Assert.IsType<ArgumentPart>(Assert.Single(message.Parts));
        Assert.Equal("when", argument.Name);
        Assert.Equal("date", argument.Type);
        Assert.Equal("long", argument.Style);
    }

    [Fact]
    public void Parse_TypedArgumentWithoutStyle_CapturesType()
    {
        Message message = MessageParser.Parse("{amount, number}");

        ArgumentPart argument = Assert.IsType<ArgumentPart>(Assert.Single(message.Parts));
        Assert.Equal("amount", argument.Name);
        Assert.Equal("number", argument.Type);
        Assert.Null(argument.Style);
    }

    [Fact]
    public void Parse_Plural_ProducesBranchesWithSelectorsAndPound()
    {
        Message message = MessageParser.Parse("{count, plural, =0 {none} one {# item} other {# items}}");

        PluralPart plural = Assert.IsType<PluralPart>(Assert.Single(message.Parts));
        Assert.Equal("count", plural.ArgumentName);
        Assert.False(plural.Ordinal);
        Assert.Equal(0, plural.Offset);

        Assert.Equal("none", LiteralOf(plural.Branches[new PluralSelector(0, null)]));

        Message one = plural.Branches[new PluralSelector(null, PluralCategory.One)];
        Assert.Collection(
            one.Parts,
            p => Assert.IsType<PoundPart>(p),
            p => Assert.Equal(" item", Assert.IsType<LiteralPart>(p).Text));

        Assert.True(plural.Branches.ContainsKey(new PluralSelector(null, PluralCategory.Other)));
    }

    [Fact]
    public void Parse_SelectOrdinalWithOffset_SetsOrdinalAndOffset()
    {
        Message message = MessageParser.Parse("{n, selectordinal, offset:1 one {#st} other {#th}}");

        PluralPart plural = Assert.IsType<PluralPart>(Assert.Single(message.Parts));
        Assert.True(plural.Ordinal);
        Assert.Equal(1, plural.Offset);
    }

    [Fact]
    public void Parse_Select_ProducesStringKeyedBranches()
    {
        Message message = MessageParser.Parse("{gender, select, male {he} female {she} other {they}}");

        SelectPart select = Assert.IsType<SelectPart>(Assert.Single(message.Parts));
        Assert.Equal("gender", select.ArgumentName);
        Assert.Equal("he", LiteralOf(select.Branches["male"]));
        Assert.Equal("she", LiteralOf(select.Branches["female"]));
        Assert.Equal("they", LiteralOf(select.Branches["other"]));
    }

    [Fact]
    public void Parse_NestedPluralInsideSelect_ParsesRecursively()
    {
        Message message = MessageParser.Parse(
            "{gender, select, female {{count, plural, one {she has # cat} other {she has # cats}}} other {x}}");

        SelectPart select = Assert.IsType<SelectPart>(Assert.Single(message.Parts));
        Message female = select.Branches["female"];
        PluralPart nested = Assert.IsType<PluralPart>(Assert.Single(female.Parts));
        Assert.Equal("count", nested.ArgumentName);
    }

    [Fact]
    public void ExtractPlaceholders_ReturnsEveryArgumentName_InFirstSeenOrder()
    {
        Message message = MessageParser.Parse("{greeting}, {name}! {count, plural, other {#}}");

        Assert.Equal(new[] { "greeting", "name", "count" }, MessageParser.ExtractPlaceholders(message));
    }

    [Fact]
    public void ExtractPlaceholders_IncludesNamesUsedOnlyInNestedBranches()
    {
        Message message = MessageParser.Parse("{gender, select, other {{count, plural, other {hi}}}}");

        Assert.Equal(new[] { "gender", "count" }, MessageParser.ExtractPlaceholders(message));
    }

    [Fact]
    public void TryParse_InvalidSyntax_ReturnsErrorWithPosition()
    {
        var ok = MessageParser.TryParse("{name", out Message? message, out MessageFormatError? error);

        Assert.False(ok);
        Assert.Null(message);
        Assert.NotNull(error);
        Assert.Equal(5, error!.Position);
    }

    [Fact]
    public void Parse_StrayClosingBrace_Throws()
    {
        MessageFormatException exception =
            Assert.Throws<MessageFormatException>(() => MessageParser.Parse("a } b"));

        Assert.Equal(2, exception.Position);
    }

    [Fact]
    public void Parse_InvalidPluralCategory_Throws()
    {
        Assert.Throws<MessageFormatException>(() => MessageParser.Parse("{n, plural, banana {x} other {y}}"));
    }

    private static string LiteralOf(Message message) =>
        Assert.IsType<LiteralPart>(Assert.Single(message.Parts)).Text;
}
