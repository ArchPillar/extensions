namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

/// <summary>
/// Manages an isolated PostgreSQL database for a single test class. Uses
/// the shared <see cref="PostgresFixture"/> container (one per assembly)
/// and creates/drops a unique database within it.
/// </summary>
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly PostgresFixture _fixture;

    internal string ConnectionString { get; private set; } = "";

    private PostgresTestDatabase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    internal static async Task<PostgresTestDatabase> CreateAsync(PostgresFixture fixture)
    {
        var connectionString = await fixture.CreateDatabaseAsync();

        return new PostgresTestDatabase(fixture)
        {
            ConnectionString = connectionString,
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            await _fixture.DropDatabaseAsync(ConnectionString);
        }
    }
}
