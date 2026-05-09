using ArchPillar.Extensions.Identifiers;

namespace ArchPillar.Extensions.Primitives.Tests.Identifiers;

public sealed class IdFactoryTests
{
    private sealed class User;

    [Fact]
    public void New_ReturnsDifferentValues()
    {
        var a = Id<User>.New();
        var b = Id<User>.New();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void New_ValueIsNotEmpty()
    {
        var id = Id<User>.New();
        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Default_ValueIsEmpty()
    {
        Id<User> id = default;
        Assert.Equal(Guid.Empty, id.Value);
    }

    [Fact]
    public void Empty_ValueIsEmpty()
    {
        Assert.Equal(Guid.Empty, Id<User>.Empty.Value);
    }

    [Fact]
    public void Empty_EqualsDefault()
    {
        Assert.Equal(default, Id<User>.Empty);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void New_OnNet9OrLater_CreatesVersion7Guid()
    {
        var id = Id<User>.New();
        // "D" format: xxxxxxxx-xxxx-Vxxx-xxxx-xxxxxxxxxxxx
        // Position 14 (0-indexed) is the version nibble.
        Assert.Equal('7', id.Value.ToString("D")[14]);
    }

    [Fact]
    public void New_OnNet9OrLater_SuccessiveIdsAreSortable()
    {
        var a = Id<User>.New();
        System.Threading.Thread.Sleep(2);
        var b = Id<User>.New();
        Assert.True(a.CompareTo(b) < 0, "v7 GUIDs should sort in creation order");
    }
#endif
}
