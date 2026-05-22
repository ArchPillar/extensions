using ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> that register
/// the ArchPillar Mapper EF Core integration.
/// <para>
/// When enabled:
/// </para>
/// <list type="bullet">
/// <item>Any <c>EnumMapper&lt;TSource, TDest&gt;.Map(source)</c> call in a LINQ query is translated into a flat SQL <c>CASE</c> expression instead of falling back to the nested conditional chain.</item>
/// <item>Any direct <c>Mapper&lt;TSource, TDest&gt;.Map(source)</c> or <c>source.Project(mapper)</c> call inside a query projection is inlined into the mapper's projection expression, so a hand-written <c>Select</c> can produce a single property with a mapper while the whole query is still translated server-side.</item>
/// </list>
/// </summary>
public static class MapperDbContextOptionsExtensions
{
    /// <summary>
    /// Registers the ArchPillar Mapper EF Core integration, enabling direct
    /// <c>EnumMapper&lt;,&gt;.Map()</c> calls as well as inlining of regular
    /// <c>Mapper&lt;,&gt;.Map()</c> / <c>Project()</c> calls in LINQ queries.
    /// <para>
    /// All <see cref="EnumMapper{TSource,TDest}"/> properties discovered on
    /// the supplied <paramref name="contexts"/> are pre-registered so their
    /// mapping tables are available at query translation time.
    /// </para>
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="contexts">
    /// One or more <see cref="MapperContext"/> instances whose
    /// <see cref="EnumMapper{TSource,TDest}"/> properties will be registered
    /// for flat SQL CASE translation.
    /// </param>
    /// <returns>The same builder instance for chaining.</returns>
    public static DbContextOptionsBuilder UseArchPillarMapper(
        this DbContextOptionsBuilder builder,
        params MapperContext[] contexts)
    {
        ArchPillarMapperOptionsExtension? existing =
            builder.Options.FindExtension<ArchPillarMapperOptionsExtension>();

        if (existing is not null)
        {
            return builder;
        }

        var store = new EnumMappingStore();

        foreach (MapperContext context in contexts)
        {
            store.RegisterFromContext(context);
        }

        builder.AddInterceptors(new EnumMapperQueryInterceptor(), new MapperInliningInterceptor());

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new ArchPillarMapperOptionsExtension(store));

        return builder;
    }
}
