using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

internal sealed class EnumMapperTranslatorPlugin(
    EnumMappingStore store,
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } =
        [new EnumMapperMethodCallTranslator(store, sqlExpressionFactory, typeMappingSource)];
}
