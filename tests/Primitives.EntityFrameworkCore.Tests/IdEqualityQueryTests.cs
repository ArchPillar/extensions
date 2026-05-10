using ArchPillar.Extensions.Identifiers;
using ArchPillar.Extensions.Identifiers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Primitives.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdEqualityQueryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    private Id<UserTag> _userId1;
    private Id<UserTag> _userId2;
    private Id<OrderTag> _orderId;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();

        _userId1 = Id<UserTag>.New();
        _userId2 = Id<UserTag>.New();
        _orderId = Id<OrderTag>.New();

        _db.Users.AddRange(
            new UserEntity { Id = _userId1, Name = "Alice", LatestOrderId = _orderId },
            new UserEntity { Id = _userId2, Name = "Bob", LatestOrderId = null });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Where_IdEqualsParameter_ReturnsMatchAsync()
    {
        Id<UserTag> id = _userId1;
        UserEntity? user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id);
        Assert.NotNull(user);
        Assert.Equal("Alice", user!.Name);
    }

    [Fact]
    public async Task Where_IdEqualsDefault_ReturnsNothingAsync()
    {
        var count = await _db.Users.CountAsync(u => u.Id == default);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Where_OptionalFkIsNull_FiltersCorrectlyAsync()
    {
        List<UserEntity> users = await _db.Users
            .Where(u => u.LatestOrderId == null)
            .ToListAsync();
        Assert.Single(users);
        Assert.Equal("Bob", users[0].Name);
    }

    [Fact]
    public async Task Where_OptionalFkIsNotNull_FiltersCorrectlyAsync()
    {
        List<UserEntity> users = await _db.Users
            .Where(u => u.LatestOrderId != null)
            .ToListAsync();
        Assert.Single(users);
        Assert.Equal("Alice", users[0].Name);
    }

    [Fact]
    public async Task Where_OptionalFkEqualsValue_FiltersCorrectlyAsync()
    {
        Id<OrderTag> orderId = _orderId;
        List<UserEntity> users = await _db.Users
            .Where(u => u.LatestOrderId == orderId)
            .ToListAsync();
        Assert.Single(users);
        Assert.Equal("Alice", users[0].Name);
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
