
namespace ArchPillar.Extensions.Models.Tests;

public sealed class IdComparisonTests
{
    private sealed class User;

    [Fact]
    public void CompareTo_SameValue_ReturnsZero()
    {
        var guid = Guid.NewGuid();
        Id<User> a = new(guid);
        Id<User> b = new(guid);
        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void CompareTo_MatchesUnderlyingGuidOrder()
    {
        var low  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Id<User> a = new(low);
        Id<User> b = new(high);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void SortedList_OrderMatchesGuidOrder()
    {
        Guid[] guids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var sortedGuids = guids.OrderBy(g => g).ToList();
        var ids = guids.Select(g => new Id<User>(g)).OrderBy(id => id).ToList();
        for (var i = 0; i < sortedGuids.Count; i++)
        {
            Assert.Equal(sortedGuids[i], ids[i].Value);
        }
    }
}
