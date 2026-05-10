using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Internal;

internal sealed class IdRelationalTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    private readonly Lazy<IRelationalTypeMappingSource> _source;

    public IdRelationalTypeMappingSourcePlugin(IServiceProvider serviceProvider)
    {
        _source = new(() => serviceProvider.GetRequiredService<IRelationalTypeMappingSource>());
    }

    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        Type? clrType = mappingInfo.ClrType;
        if (clrType is null)
        {
            return null;
        }

        Type? idType = IdConvention.GetIdType(clrType);
        if (idType is null)
        {
            return null;
        }

        RelationalTypeMapping? guidMapping = _source.Value.FindMapping(typeof(Guid));
        if (guidMapping is null)
        {
            return null;
        }

        Type typeArg = idType.GetGenericArguments()[0];

        var converter = (ValueConverter)Activator.CreateInstance(
            typeof(IdValueConverter<>).MakeGenericType(typeArg))!;
        var comparer = (ValueComparer)Activator.CreateInstance(
            typeof(IdValueComparer<>).MakeGenericType(typeArg))!;

        return guidMapping.Clone(
            clrType: clrType,
            converter: converter,
            comparer: comparer);
    }
}
