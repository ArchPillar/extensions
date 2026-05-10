using System.Text.Json;
using ArchPillar.Extensions.Identifiers;

namespace ArchPillar.Extensions.Primitives.Tests.Identifiers;

public sealed class IdJsonTests
{
    private sealed class User;

    private sealed record UserDto(Id<User> Id, string Name);

    [Fact]
    public void Serialize_WritesGuidString()
    {
        var guid = Guid.NewGuid();
        Id<User> id = new(guid);
        var json = JsonSerializer.Serialize(id);
        Assert.Equal($"\"{guid:D}\"", json);
    }

    [Fact]
    public void Deserialize_ReadsGuidString()
    {
        var guid = Guid.NewGuid();
        var json = $"\"{guid:D}\"";
        Id<User> id = JsonSerializer.Deserialize<Id<User>>(json);
        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        var id = Id<User>.New();
        var json = JsonSerializer.Serialize(id);
        Id<User> deserialized = JsonSerializer.Deserialize<Id<User>>(json);
        Assert.Equal(id, deserialized);
    }

    [Fact]
    public void Serialize_InsideDto_WritesGuidForIdField()
    {
        var guid = Guid.NewGuid();
        var dto = new UserDto(new Id<User>(guid), "Alice");
        var json = JsonSerializer.Serialize(dto);
        using var doc = JsonDocument.Parse(json);
        Guid idValue = doc.RootElement.GetProperty("Id").GetGuid();
        Assert.Equal(guid, idValue);
    }

    [Fact]
    public void Deserialize_InsideDto_ReadsGuidForIdField()
    {
        var guid = Guid.NewGuid();
        var json = $"{{\"Id\":\"{guid:D}\",\"Name\":\"Alice\"}}";
        UserDto? dto = JsonSerializer.Deserialize<UserDto>(json);
        Assert.NotNull(dto);
        Assert.Equal(guid, dto!.Id.Value);
    }

    [Fact]
    public void Deserialize_InvalidGuid_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Id<User>>("\"not-a-guid\""));
    }
}
