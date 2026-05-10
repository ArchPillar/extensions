using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

[Collection("PostgreSQL")]
public sealed class IdContainsQueryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    private Id<UserTag> _id1;
    private Id<UserTag> _id2;
    private Id<UserTag> _id3;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();

        _id1 = Id<UserTag>.New();
        _id2 = Id<UserTag>.New();
        _id3 = Id<UserTag>.New();

        _db.Users.AddRange(
            new UserEntity { Id = _id1, Name = "A" },
            new UserEntity { Id = _id2, Name = "B" },
            new UserEntity { Id = _id3, Name = "C" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Contains_ListOfIds_ReturnsMatchingEntitiesAsync()
    {
        var ids = new List<Id<UserTag>> { _id1, _id3 };
        List<UserEntity> users = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.Name)
            .ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.Equal("A", users[0].Name);
        Assert.Equal("C", users[1].Name);
    }

    [Fact]
    public async Task Contains_IEnumerableOfIds_ReturnsMatchingEntitiesAsync()
    {
        IEnumerable<Id<UserTag>> ids = new[] { _id2 };
        List<UserEntity> users = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .ToListAsync();
        Assert.Single(users);
        Assert.Equal("B", users[0].Name);
    }

    [Fact]
    public async Task Contains_EmptyCollection_ReturnsNothingAsync()
    {
        var ids = new List<Id<UserTag>>();
        var count = await _db.Users.CountAsync(u => ids.Contains(u.Id));
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
