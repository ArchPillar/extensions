using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.TypeMappings;

/// <summary>
/// Replaces the default <see cref="Guid"/> type mapping with one that emits
/// <c>'…'::uuid</c> literals so that uuid constants projected as a read column
/// come back as <see cref="Guid"/> instead of <see cref="string"/>.
/// </summary>
internal sealed class GuidUuidTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        // EF Core unwraps Nullable<T> before consulting type-mapping plugins (the mapping
        // for Guid? and Guid is the same instance), but unwrap defensively so the match
        // holds regardless of how this plugin is reached.
        Type? clrType = mappingInfo.ClrType;
        if (clrType is not null)
        {
            clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        }

        var storeType = mappingInfo.StoreTypeNameBase;

        var isGuid = clrType == typeof(Guid);
        var isUuidStore = string.Equals(storeType, "uuid", StringComparison.OrdinalIgnoreCase);

        if (!isGuid && !isUuidStore)
        {
            return null;
        }

        if (clrType is not null && clrType != typeof(Guid))
        {
            return null;
        }

        return GuidUuidMapping.Default;
    }
}
