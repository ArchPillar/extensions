using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class ComparisonStreamTests
{
    [Fact]
    public void Matched_IdenticalContentWrittenInOneCall_IsTrue()
    {
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("hello"u8);

        Assert.True(stream.Matched);
    }

    [Fact]
    public void Matched_IdenticalContentSplitAcrossWrites_IsTrue()
    {
        // A format is free to write in as many chunks as it likes; the comparison spans them.
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("he"u8);
        stream.Write("l"u8);
        stream.Write("lo"u8);

        Assert.True(stream.Matched);
    }

    [Fact]
    public void Matched_ContentDiffersMidway_IsFalse()
    {
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("he"u8);
        stream.Write("XX"u8);
        stream.Write("o"u8);

        Assert.False(stream.Matched);
    }

    [Fact]
    public void Matched_ShorterContent_IsFalse()
    {
        // A prefix matches byte for byte but leaves the expected buffer unfilled, so it is not the same output.
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("hell"u8);

        Assert.False(stream.Matched);
    }

    [Fact]
    public void Matched_LongerContent_IsFalse()
    {
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("hello!"u8);

        Assert.False(stream.Matched);
    }

    [Fact]
    public void Matched_WritesContinuingPastDivergence_StaysFalse()
    {
        // The format cannot be told to stop, so trailing writes must neither throw nor let it match again.
        using var stream = new ComparisonStream("hello"u8.ToArray());

        stream.Write("XXXXX"u8);
        stream.Write("hello"u8);

        Assert.False(stream.Matched);
    }

    [Fact]
    public async Task Matched_AsyncWrites_CompareTheSameWayAsync()
    {
        using var stream = new ComparisonStream("hello"u8.ToArray());

        await stream.WriteAsync("hel"u8.ToArray());
        await stream.WriteAsync("lo"u8.ToArray());

        Assert.True(stream.Matched);
    }

    [Fact]
    public void Matched_NothingWrittenAgainstEmptyExpectation_IsTrue()
    {
        using var stream = new ComparisonStream(Array.Empty<byte>());

        Assert.True(stream.Matched);
    }
}
