using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

[Collection("PostgreSQL")]
public sealed class HasIdConversionIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private IdManualDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);

        DbContextOptions<IdManualDbContext> options =
            new DbContextOptionsBuilder<IdManualDbContext>()
                .UseNpgsql(_postgres.ConnectionString)
                .Options;

        _db = new IdManualDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task ManualConversion_InsertAndQuery_WorksAsync()
    {
        var id = Id<UserTag>.New();
        _db.Users.Add(new UserEntity { Id = id, Name = "Manual" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UserEntity? user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id);

        Assert.NotNull(user);
        Assert.Equal("Manual", user!.Name);
    }
}
