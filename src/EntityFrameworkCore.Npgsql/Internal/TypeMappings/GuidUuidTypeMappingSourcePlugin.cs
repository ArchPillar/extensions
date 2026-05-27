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
        Type? clrType = mappingInfo.ClrType;
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
