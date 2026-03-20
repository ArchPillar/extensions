using ArchPillar.Extensions.Mapper.EntityFramework.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArchPillar.Extensions.Mapper.EntityFramework;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> that register
/// the ArchPillar Mapper EF Core integration.
/// <para>
/// When enabled, any <c>EnumMapper&lt;TSource, TDest&gt;.Map(source)</c> call
/// in a LINQ query is translated into a flat SQL <c>CASE</c> expression
/// instead of falling back to the nested conditional chain.
/// </para>
/// </summary>
public static class MapperDbContextOptionsExtensions
{
    /// <summary>
    /// Registers the ArchPillar Mapper EF Core integration, enabling direct
    /// <c>EnumMapper&lt;,&gt;.Map()</c> calls in LINQ queries.
    /// <para>
    /// Enum mapper instances are discovered dynamically when the query
    /// interceptor encounters <c>Map()</c> calls. Their mapping tables are
    /// cached for the lifetime of the <see cref="DbContextOptions"/>.
    /// </para>
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static DbContextOptionsBuilder UseArchPillarMapper(
        this DbContextOptionsBuilder builder)
    {
        // Prevent double registration.
        ArchPillarMapperOptionsExtension? existing =
            builder.Options.FindExtension<ArchPillarMapperOptionsExtension>();

        if (existing is not null)
        {
            return builder;
        }

        var store = new EnumMappingStore();

        // Register the query interceptor (rewrites Map() → MapEnum()).
        builder.AddInterceptors(new EnumMapperQueryInterceptor(store));

        // Register the options extension (adds translator plugin to DI).
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new ArchPillarMapperOptionsExtension(store));

        return builder;
    }
}
