using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// Builds a <see cref="TestDbContext"/> wired up with both the
/// <see cref="NpgsqlDataSourceBuilder"/> wire converters and the
/// <see cref="Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"/> EF
/// integration. The resulting data source lives for the lifetime of the
/// returned context.
/// </summary>
internal static class TestContextFactory
{
    public static (NpgsqlDataSource DataSource, TestDbContext Context) Create(string connectionString)
    {
        NpgsqlDataSourceBuilder dataSourceBuilder = new(connectionString);
        dataSourceBuilder.UseArchPillarNpgsqlImprovements();
        NpgsqlDataSource dataSource = dataSourceBuilder.Build();

        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(dataSource)
            .UseArchPillarNpgsqlImprovements()
            .Options;

        return (dataSource, new TestDbContext(options));
    }
}
