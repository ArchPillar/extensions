using ArchPillar.Extensions.Identifiers;
using ArchPillar.Extensions.Identifiers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Primitives.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdConstantTests(PostgresFixture fixture) : IAsyncLifetime
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
    public async Task Where_IdEqualsExplicitCastFromGuid_ReturnsMatchAsync()
    {
        Guid guid = _id;
        var id = (Id<UserTag>)guid;
        UserEntity? user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id);
        Assert.NotNull(user);
        Assert.Equal("Alice", user!.Name);
    }

    [Fact]
    public async Task Where_IdEqualsParsedConstant_ReturnsMatchAsync()
    {
        var id = Id<UserTag>.Parse(_id.ToString(), null);
        UserEntity? user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Where_IdEqualsNewId_NeverMatchesAsync()
    {
        // Id<T>.New() is evaluated client-side and becomes a parameter —
        // it will never match a persisted entity.
        var freshId = Id<UserTag>.New();
        var count = await _db.Users.CountAsync(u => u.Id == freshId);
        Assert.Equal(0, count);
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
