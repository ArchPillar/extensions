using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdChangeTrackingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdTestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        _db = BuildContext(_postgres.ConnectionString);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Update_TrackedEntity_DetectsNameChangeAsync()
    {
        var id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = id, Name = "Before" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(id);
        user!.Name = "After";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? updated = await _db.Users.FindAsync(id);
        Assert.Equal("After", updated!.Name);
    }

    [Fact]
    public async Task TrackedEntity_IdProperty_ValueComparerPreventsFalseModifiedAsync()
    {
        var id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = id, Name = "Test" });
        await _db.SaveChangesAsync();

        // Load the entity — EF takes a snapshot of Id using ValueComparer.
        UserEntity? user = await _db.Users.FindAsync(id);

        // Re-assign the same value; the ValueComparer must treat this as unchanged.
        user!.Id = id;

        EntityEntry<UserEntity> entry = _db.Entry(user);
        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    [Fact]
    public async Task OptionalFkChange_FromNullToValue_IsDetectedAsync()
    {
        var userId = Id<UserTag>.New();
        var orderId = Id<OrderTag>.New();

        _db.Users.Add(new UserEntity { Id = userId, Name = "X", LatestOrderId = null });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(userId);
        user!.LatestOrderId = orderId;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? updated = await _db.Users.FindAsync(userId);
        Assert.Equal(orderId, updated!.LatestOrderId);
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
