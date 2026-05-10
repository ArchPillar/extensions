using ArchPillar.Extensions.Models.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="DbContextOptionsBuilder"/> that register
/// ArchPillar Primitives EF Core integration.
/// </summary>
public static class PrimitivesDbContextOptionsExtensions
{
    /// <summary>
    /// Registers the ArchPillar Primitives EF Core integration. When enabled,
    /// every property whose CLR type implements <c>IId</c> automatically receives
    /// a <c>ValueConverter&lt;Id&lt;T&gt;, Guid&gt;</c> and
    /// <c>ValueComparer&lt;Id&lt;T&gt;&gt;</c>, and <c>Id&lt;T&gt;</c> becomes a
    /// first-class relational type in LINQ query translation.
    /// </summary>
    public static DbContextOptionsBuilder UseArchPillarTypedIds(
        this DbContextOptionsBuilder builder)
    {
        if (builder.Options.FindExtension<ArchPillarPrimitivesOptionsExtension>() is not null)
        {
            return builder;
        }

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new ArchPillarPrimitivesOptionsExtension());

        return builder;
    }

    /// <summary>
    /// Generic overload of <see cref="UseArchPillarTypedIds(DbContextOptionsBuilder)"/>
    /// that preserves the <typeparamref name="TContext"/> type parameter so it can be
    /// chained alongside other typed builder extensions like <c>UseNpgsql&lt;TContext&gt;</c>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseArchPillarTypedIds<TContext>(
        this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
    {
        UseArchPillarTypedIds((DbContextOptionsBuilder)builder);
        return builder;
    }
}
