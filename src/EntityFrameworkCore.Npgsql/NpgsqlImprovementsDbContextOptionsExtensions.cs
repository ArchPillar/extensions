using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// Extension methods on <see cref="DbContextOptionsBuilder"/> that register
/// the ArchPillar EF Core integration for Npgsql.
/// </summary>
public static class NpgsqlImprovementsDbContextOptionsExtensions
{
    /// <summary>
    /// Registers the ArchPillar Npgsql EF Core integration. When enabled, the
    /// context gets the <c>'…'::uuid</c> Guid literal cast (fixes Guid projection),
    /// and the <c>EF.Functions.JsonbBuildObject(...)</c> translator.
    /// </summary>
    /// <remarks>
    /// This is the EF-side wiring. The ADO-wire converters (date/time/enum) are
    /// installed at the <c>NpgsqlDataSourceBuilder</c> layer via
    /// <see cref="NpgsqlImprovementsDataSourceBuilderExtensions.UseArchPillarNpgsqlImprovements(global::Npgsql.NpgsqlDataSourceBuilder)"/>.
    /// </remarks>
    public static DbContextOptionsBuilder UseArchPillarNpgsqlImprovements(
        this DbContextOptionsBuilder builder)
    {
        if (builder.Options.FindExtension<NpgsqlImprovementsOptionsExtension>() is not null)
        {
            return builder;
        }

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new NpgsqlImprovementsOptionsExtension());

        return builder;
    }

    /// <summary>
    /// Generic overload of <see cref="UseArchPillarNpgsqlImprovements(DbContextOptionsBuilder)"/>
    /// that preserves the <typeparamref name="TContext"/> type parameter so it can be
    /// chained alongside other typed builder extensions like <c>UseNpgsql&lt;TContext&gt;</c>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseArchPillarNpgsqlImprovements<TContext>(
        this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
    {
        UseArchPillarNpgsqlImprovements((DbContextOptionsBuilder)builder);
        return builder;
    }
}
