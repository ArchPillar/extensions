namespace ArchPillar.Extensions.Localization.MessageFormat.Tests;

public sealed class MessageSyntaxTests
{
    [Fact]
    public void ExtractPlaceholders_ReturnsEveryArgumentName_InFirstSeenOrder()
    {
        IReadOnlyCollection<string> names =
            MessageSyntax.ExtractPlaceholders("{greeting}, {name}! {count, plural, other {#}}");

        Assert.Equal(new[] { "greeting", "name", "count" }, names);
    }

    [Fact]
    public void TryValidate_WellFormedMessage_ReturnsTrueWithNoError()
    {
        var valid = MessageSyntax.TryValidate(
            "{count, plural, one {# item} other {# items}}", out MessageFormatError? error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_MalformedMessage_ReturnsFalseWithPositionedError()
    {
        var valid = MessageSyntax.TryValidate("{name", out MessageFormatError? error);

        Assert.False(valid);
        Assert.NotNull(error);
        Assert.Equal(5, error.Position);
    }

    [Fact]
    public void InsertMissingOtherBranches_Plural_AppendsEmptyOtherBeforeClose()
    {
        var fixedText = MessageSyntax.InsertMissingOtherBranches("{count, plural, one {# message}}");

        Assert.Equal("{count, plural, one {# message} other {}}", fixedText);
        Assert.Empty(MessageSyntax.FindConstructsMissingOther(fixedText));
    }

    [Fact]
    public void InsertMissingOtherBranches_Select_AppendsEmptyOtherBeforeClose()
    {
        var fixedText = MessageSyntax.InsertMissingOtherBranches("{gender, select, male {He}}");

        Assert.Equal("{gender, select, male {He} other {}}", fixedText);
    }

    [Fact]
    public void InsertMissingOtherBranches_NestedConstructs_FixesEachLevel()
    {
        var fixedText = MessageSyntax.InsertMissingOtherBranches("{n, plural, one {{g, select, male {he}}}}");

        Assert.Equal("{n, plural, one {{g, select, male {he} other {}}} other {}}", fixedText);
        Assert.Empty(MessageSyntax.FindConstructsMissingOther(fixedText));
    }

    [Fact]
    public void InsertMissingOtherBranches_AlreadyComplete_ReturnsUnchanged()
    {
        const string Complete = "{count, plural, one {# item} other {# items}}";

        Assert.Equal(Complete, MessageSyntax.InsertMissingOtherBranches(Complete));
    }

    [Fact]
    public void InsertMissingOtherBranches_QuotedBraces_AreNotTreatedAsConstructs()
    {
        const string Quoted = "It's '{not a placeholder}' here";

        Assert.Equal(Quoted, MessageSyntax.InsertMissingOtherBranches(Quoted));
    }

    [Fact]
    public void InsertMissingOtherBranches_InvalidSyntax_ReturnsUnchanged()
    {
        const string Invalid = "{count, plural, one {x}";

        Assert.Equal(Invalid, MessageSyntax.InsertMissingOtherBranches(Invalid));
    }

    [Fact]
    public void InsertMissingOtherBranches_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => MessageSyntax.InsertMissingOtherBranches(null!));

    [Fact]
    public void RecognizeCardinalPlural_SimplePlural_ReturnsArgumentAndBranchBodies()
    {
        CardinalPlural? shape = MessageSyntax.RecognizeCardinalPlural("{count, plural, one {# item} other {# items}}");

        Assert.NotNull(shape);
        Assert.Equal("count", shape.ArgumentName);
        Assert.Equal("# item", shape.Branches[PluralCategory.One]);
        Assert.Equal("# items", shape.Branches[PluralCategory.Other]);
    }

    [Theory]
    [InlineData("{count, selectordinal, one {#st} other {#th}}")] // ordinal, not cardinal
    [InlineData("{count, plural, offset:1 one {#} other {#}}")]   // offset
    [InlineData("{count, plural, =0 {none} other {#}}")]          // explicit =N selector
    [InlineData("You have {count, plural, one {#} other {#}}")]   // surrounding text
    [InlineData("not a plural at all")]
    [InlineData("{count, plural, one {#}")]                       // invalid syntax
    public void RecognizeCardinalPlural_NonRepresentableShape_ReturnsNull(string text) =>
        Assert.Null(MessageSyntax.RecognizeCardinalPlural(text));

    [Fact]
    public void RecognizeCardinalPlural_BranchWithQuotedSyntax_ReemitsEquivalentBody()
    {
        // A branch body with a literal brace must serialize back to ICU that re-parses to the same literal.
        CardinalPlural? shape = MessageSyntax.RecognizeCardinalPlural("{n, plural, one {a '{' b} other {#}}");

        Assert.NotNull(shape);
        var body = shape.Branches[PluralCategory.One];
        Assert.Equal("{n, plural, one {a '{' b}}", MessageSyntax.BuildCardinalPlural("n", [(PluralCategory.One, body)]));
    }
}
