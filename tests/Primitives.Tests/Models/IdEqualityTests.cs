
namespace ArchPillar.Extensions.Models.Tests;

public sealed class IdEqualityTests
{
    private sealed class User;
    private sealed class Order;

    [Fact]
    public void Equal_SameGuid_IsTrue()
    {
        var guid = Guid.NewGuid();
        Id<User> a = new(guid);
        Id<User> b = new(guid);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equal_DifferentGuid_IsFalse()
    {
        Id<User> a = new(Guid.NewGuid());
        Id<User> b = new(Guid.NewGuid());
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equal_BoxedObject_MatchesByValue()
    {
        var guid = Guid.NewGuid();
        Id<User> a = new(guid);
        object b = new Id<User>(guid);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equal_BoxedDifferentType_IsFalse()
    {
        var guid = Guid.NewGuid();
        Id<User> a = new(guid);
        Assert.False(a.Equals(guid));
    }

    [Fact]
    public void GetHashCode_EqualIds_SameHash()
    {
        var guid = Guid.NewGuid();
        Id<User> a = new(guid);
        Id<User> b = new(guid);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void UsedAsDictionaryKey_RetrievesValue()
    {
        var key = Id<User>.New();
        var dict = new Dictionary<Id<User>, string> { [key] = "found" };
        Assert.Equal("found", dict[key]);
    }

    [Fact]
    public void Default_EqualsEmpty()
    {
        Assert.Equal(default, Id<User>.Empty);
        Assert.True(default == Id<User>.Empty);
    }
}
