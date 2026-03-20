using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

/// <summary>
/// Registers the <see cref="EnumMapperMethodCallTranslator"/> with EF Core's
/// query translation pipeline.
/// </summary>
internal sealed class EnumMapperTranslatorPlugin(
    EnumMappingStore store,
    IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Create(ISqlExpressionFactory sqlExpressionFactory)
    {
        yield return new EnumMapperMethodCallTranslator(store, sqlExpressionFactory, typeMappingSource);
    }
}
