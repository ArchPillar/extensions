using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdGroupingQueryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    private Id<UserTag> _ownerId1;
    private Id<UserTag> _ownerId2;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();

        _ownerId1 = Id<UserTag>.New();
        _ownerId2 = Id<UserTag>.New();

        _db.Orders.AddRange(
            new OrderEntity { Id = Id<OrderTag>.New(), Title = "A1", OwnerId = _ownerId1 },
            new OrderEntity { Id = Id<OrderTag>.New(), Title = "A2", OwnerId = _ownerId1 },
            new OrderEntity { Id = Id<OrderTag>.New(), Title = "B1", OwnerId = _ownerId2 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GroupBy_OwnerId_CorrectGroupCountsAsync()
    {
        Id<UserTag> ownerId1 = _ownerId1;
        Id<UserTag> ownerId2 = _ownerId2;

        var groups = await _db.Orders
            .GroupBy(o => o.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToListAsync();

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.OwnerId == ownerId1 && g.Count == 2);
        Assert.Contains(groups, g => g.OwnerId == ownerId2 && g.Count == 1);
    }

    [Fact]
    public async Task OrderBy_Id_SortsCorrectlyAsync()
    {
        List<Id<OrderTag>> ids = await _db.Orders
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync();

        Assert.Equal(3, ids.Count);

        for (var i = 0; i < ids.Count - 1; i++)
        {
            Assert.True(ids[i].CompareTo(ids[i + 1]) <= 0);
        }
    }

    [Fact]
    public async Task Distinct_OnIdProjection_DeduplicatesOwnersAsync()
    {
        List<Id<UserTag>> ownerIds = await _db.Orders
            .Select(o => o.OwnerId)
            .Distinct()
            .ToListAsync();

        Assert.Equal(2, ownerIds.Count);
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
