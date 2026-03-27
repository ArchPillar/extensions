using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Manages a PostgreSQL database for integration tests. Tries Testcontainers
/// (Docker) first; falls back to the host-local PostgreSQL instance when the
/// <c>CLAUDE_CLOUD</c> environment variable is set and Docker is unavailable.
/// </summary>
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private const string LocalConnectionString =
        "Host=localhost;Port=5432;Username=app;Password=postgres;Database={0}";

    private PostgreSqlContainer? _container;
    private string _databaseName = "";

    internal string ConnectionString { get; private set; } = "";

    internal static async Task<PostgresTestDatabase> CreateAsync()
    {
        var instance = new PostgresTestDatabase();
        await instance.InitializeAsync();
        return instance;
    }

    private async Task InitializeAsync()
    {
        if (TryCreateContainer(out PostgreSqlContainer? container) && container != null)
        {
            _container = container;
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            return;
        }

        // Fallback: host-local PostgreSQL (cloud environments without Docker).
        _databaseName = $"mapper_test_{Guid.NewGuid():N}";
        ConnectionString = string.Format(LocalConnectionString, _databaseName);

        // Create the disposable test database.
        var adminConn = string.Format(LocalConnectionString, "postgres");
        DbContextOptions<PostgresTestDbContext> adminOptions = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(adminConn)
            .Options;

        await using var adminDb = new PostgresTestDbContext(adminOptions);
        await adminDb.Database.ExecuteSqlRawAsync(
            $"CREATE DATABASE \"{_databaseName}\"");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
            return;
        }

        // Drop the test database when using local PostgreSQL.
        if (!string.IsNullOrEmpty(_databaseName))
        {
            var adminConn = string.Format(LocalConnectionString, "postgres");
            DbContextOptions<PostgresTestDbContext> adminOptions = new DbContextOptionsBuilder<PostgresTestDbContext>()
                .UseNpgsql(adminConn)
                .Options;

            await using var adminDb = new PostgresTestDbContext(adminOptions);
            await adminDb.Database.ExecuteSqlRawAsync(
                $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        }
    }

    private static bool TryCreateContainer(out PostgreSqlContainer? container)
    {
        try
        {
            container = new PostgreSqlBuilder()
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilMessageIsLogged("database system is ready to accept connections"))
                .Build();

            // Validate that Docker is reachable before returning the container.
            // The Build() call above succeeds even if Docker is not available;
            // the actual check happens on StartAsync(). However, on environments
            // where CLAUDE_CLOUD is set we want to detect Docker absence early
            // so the fallback can kick in without paying the StartAsync timeout.
            if (Environment.GetEnvironmentVariable("CLAUDE_CLOUD") == "true")
            {
                // Attempt a quick Docker ping. If it throws, Docker is unavailable.
                try
                {
                    container.StartAsync().Wait(TimeSpan.FromSeconds(30));
                    return true;
                }
                catch
                {
                    container = null;
                    return false;
                }
            }

            return true;
        }
        catch
        {
            container = null;
            return false;
        }
    }
}
