
namespace ArchPillar.Extensions.Models.Tests;

public sealed class IdFormattingTests
{
    private sealed class User;

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    [InlineData("X")]
    [InlineData(null)]
    public void ToString_Format_MatchesGuid(string? format)
    {
        var guid = Guid.NewGuid();
        Id<User> id = new(guid);
        Assert.Equal(guid.ToString(format), id.ToString(format, null));
    }

    [Fact]
    public void TryFormat_WritesToSpan()
    {
        var guid = Guid.NewGuid();
        Id<User> id = new(guid);
        Span<char> buffer = stackalloc char[36];
        var formatted = id.TryFormat(buffer, out var written, "D".AsSpan(), null);
        Assert.True(formatted);
        Assert.Equal(36, written);
        Assert.Equal(guid.ToString("D"), new string(buffer));
    }

    [Fact]
    public void Parse_String_RoundTrip()
    {
        var id = Id<User>.New();
        var parsed = Id<User>.Parse(id.ToString(), null);
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryParse_ValidString_ReturnsTrue()
    {
        var id = Id<User>.New();
        var success = Id<User>.TryParse(id.ToString(), null, out Id<User> parsed);
        Assert.True(success);
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        var success = Id<User>.TryParse("not-a-guid", null, out Id<User> result);
        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_Span_RoundTrip()
    {
        var id = Id<User>.New();
        ReadOnlySpan<char> span = id.ToString().AsSpan();
        var parsed = Id<User>.Parse(span, null);
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryParse_Span_ValidString_ReturnsTrue()
    {
        var id = Id<User>.New();
        ReadOnlySpan<char> span = id.ToString().AsSpan();
        var success = Id<User>.TryParse(span, null, out Id<User> parsed);
        Assert.True(success);
        Assert.Equal(id, parsed);
    }
}
