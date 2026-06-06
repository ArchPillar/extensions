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
        Assert.Equal(5, error!.Position);
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
}
