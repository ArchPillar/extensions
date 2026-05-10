using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

[Collection("PostgreSQL")]
public sealed class IdJoinQueryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();

        var userId = Id<UserTag>.New();
        var orderId = Id<OrderTag>.New();

        _db.Users.Add(new UserEntity { Id = userId, Name = "Alice" });
        _db.Orders.Add(new OrderEntity { Id = orderId, Title = "Order1", OwnerId = userId });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Join_UserToOrder_ByIdColumn_ReturnsMatchAsync()
    {
        var results = await _db.Users
            .Join(
                _db.Orders,
                u => u.Id,
                o => o.OwnerId,
                (u, o) => new { u.Name, o.Title })
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Order1", results[0].Title);
    }

    [Fact]
    public async Task SelectMany_CrossJoin_FiltersOnIdAsync()
    {
        var results = await (
            from u in _db.Users
            join o in _db.Orders on u.Id equals o.OwnerId
            select new { u.Name, o.Title }
        ).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
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
