using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// xUnit test collection that shares a single PostgreSQL container (or
/// host-local instance) across all test classes in the collection. Each
/// test class receives an isolated database via
/// <see cref="PostgresFixture.CreateDatabaseAsync"/>.
/// </summary>
[CollectionDefinition("PostgreSQL")]
public sealed class PostgresTestCollection : ICollectionFixture<PostgresFixture>;

/// <summary>
/// Assembly-level fixture that starts a single PostgreSQL Testcontainer
/// (or connects to the host-local instance in cloud environments) and
/// hands out isolated databases to each test class.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string LocalConnectionTemplate =
        "Host=localhost;Port=5432;Username=app;Password=postgres;Database={0}";

    private PostgreSqlContainer? _container;
    private string _baseConnectionString = "";

    public async Task InitializeAsync()
    {
        if (TryBuildContainer(out PostgreSqlContainer? container) && container != null)
        {
            _container = container;
            await _container.StartAsync();
            _baseConnectionString = _container.GetConnectionString();
            return;
        }

        // Fallback: host-local PostgreSQL (cloud environments without Docker).
        _baseConnectionString = string.Format(LocalConnectionTemplate, "postgres");
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a fresh, isolated database within the shared container and
    /// returns its connection string. The caller is responsible for dropping
    /// the database via <see cref="DropDatabaseAsync"/> when done.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"mapper_test_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();

        return ReplaceDatabaseName(_baseConnectionString, databaseName);
    }

    /// <summary>
    /// Drops a previously created test database.
    /// </summary>
    public async Task DropDatabaseAsync(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        var databaseName = builder.Database;

        if (string.IsNullOrEmpty(databaseName))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    private static string ReplaceDatabaseName(string connectionString, string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString) { Database = databaseName };
        return builder.ConnectionString;
    }

    private static bool TryBuildContainer(out PostgreSqlContainer? container)
    {
        try
        {
            container = new PostgreSqlBuilder()
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilMessageIsLogged("database system is ready to accept connections"))
                .Build();

            if (Environment.GetEnvironmentVariable("CLAUDE_CLOUD") == "true")
            {
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
