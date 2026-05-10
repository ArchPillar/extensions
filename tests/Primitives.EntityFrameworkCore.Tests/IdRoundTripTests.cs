using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

[Collection("PostgreSQL")]
public sealed class IdRoundTripTests(PostgresFixture fixture) : IAsyncLifetime
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
    public async Task Insert_And_Find_ReturnsCorrectEntityAsync()
    {
        var id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = id, Name = "Alice" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(id);

        Assert.NotNull(user);
        Assert.Equal(id, user!.Id);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task Insert_NullOptionalFk_StoresNullAsync()
    {
        var id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = id, Name = "Bob", LatestOrderId = null });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(id);

        Assert.NotNull(user);
        Assert.Null(user!.LatestOrderId);
    }

    [Fact]
    public async Task Insert_NonNullOptionalFk_StoresValueAsync()
    {
        var userId = Id<UserTag>.New();
        var orderId = Id<OrderTag>.New();
        _db.Users.Add(new UserEntity { Id = userId, Name = "Carol", LatestOrderId = orderId });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.FindAsync(userId);

        Assert.NotNull(user);
        Assert.Equal(orderId, user!.LatestOrderId);
    }

    [Fact]
    public async Task IdColumn_IsUuidTypeAsync()
    {
        var tableName = _db.Model.FindEntityType(typeof(UserEntity))!.GetTableName()!;

        await using var conn = new NpgsqlConnection(_postgres.ConnectionString);
        await conn.OpenAsync();
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = @t AND lower(column_name) = 'id'";
        cmd.Parameters.AddWithValue("t", tableName);
        var dataType = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("uuid", dataType);
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
