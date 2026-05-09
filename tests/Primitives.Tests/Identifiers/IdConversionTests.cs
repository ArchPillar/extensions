using ArchPillar.Extensions.Identifiers;

namespace ArchPillar.Extensions.Primitives.Tests.Identifiers;

public sealed class IdConversionTests
{
    private sealed class User;

    [Fact]
    public void ImplicitToGuid_ReturnsUnderlyingValue()
    {
        var guid = Guid.NewGuid();
        Id<User> id = new(guid);
        Guid result = id;
        Assert.Equal(guid, result);
    }

    [Fact]
    public void ExplicitFromGuid_WrapsValue()
    {
        var guid = Guid.NewGuid();
        var id = (Id<User>)guid;
        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void RoundTrip_GuidToIdToGuid_PreservesValue()
    {
        var guid = Guid.NewGuid();
        var id = (Id<User>)guid;
        Guid result = id;
        Assert.Equal(guid, result);
    }
}
