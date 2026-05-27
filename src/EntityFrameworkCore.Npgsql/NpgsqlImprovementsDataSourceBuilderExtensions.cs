using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;
using Npgsql;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// Extension methods on <see cref="NpgsqlDataSourceBuilder"/> that install the
/// ArchPillar ADO-wire converters.
/// </summary>
public static class NpgsqlImprovementsDataSourceBuilderExtensions
{
    /// <summary>
    /// Installs the ArchPillar Npgsql wire converters on this data source builder:
    /// <list type="bullet">
    /// <item><description><see cref="System.DateTime"/> ↔ <c>timestamptz</c> with UTC enforcement
    /// (rejects <see cref="System.DateTimeKind.Unspecified"/>, converts <see cref="System.DateTimeKind.Local"/> to UTC).</description></item>
    /// <item><description><see cref="System.DateTimeOffset"/> ↔ <c>timestamptz</c>, always stored as UTC regardless of input offset.</description></item>
    /// <item><description>Any CLR <c>enum</c> ↔ <c>int4</c>, including arrays.</description></item>
    /// </list>
    /// Pair with <c>UseArchPillarNpgsqlImprovements</c> on the EF
    /// <c>DbContextOptionsBuilder</c> for the EF-side fixes (Guid literal cast, jsonb helpers).
    /// </summary>
    public static NpgsqlDataSourceBuilder UseArchPillarNpgsqlImprovements(
        this NpgsqlDataSourceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTypeInfoResolverFactory(new ArchPillarTypeInfoResolverFactory());
        return builder;
    }
}
