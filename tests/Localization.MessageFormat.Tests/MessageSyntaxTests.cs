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
}
