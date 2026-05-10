using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdProjectionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    private Id<UserTag> _id;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();

        _id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = _id, Name = "Alice" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SelectScalarId_MaterializesCorrectlyAsync()
    {
        Id<UserTag> projectedId = await _db.Users
            .Where(u => u.Id == _id)
            .Select(u => u.Id)
            .SingleAsync();

        Assert.Equal(_id, projectedId);
    }

    [Fact]
    public async Task SelectAnonymousWithId_MaterializesCorrectlyAsync()
    {
        var result = await _db.Users
            .Where(u => u.Id == _id)
            .Select(u => new { u.Id, u.Name })
            .SingleAsync();

        Assert.Equal(_id, result.Id);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public async Task SelectAll_MaterializesAllIdPropertiesAsync()
    {
        var userId = Id<UserTag>.New();
        var orderId = Id<OrderTag>.New();
        _db.Users.Add(new UserEntity { Id = userId, Name = "Bob", LatestOrderId = orderId });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(userId);

        Assert.NotNull(user);
        Assert.Equal(userId, user!.Id);
        Assert.Equal(orderId, user.LatestOrderId);
    }

    private static IdTestDbContext BuildContext(string connectionString)
    {
        DbContextOptions<IdTestDbContext> options = new DbContextOptionsBuilder<IdTestDbContext>()
            .UseNpgsql(connectionString)
            .UseArchPillarTypedIds()
            .Options;
        return new IdTestDbContext(options);
    }
}
