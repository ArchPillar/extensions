using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;

internal sealed class JsonbBuildObjectMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public JsonbBuildObjectMethodCallTranslatorPlugin(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource)
    {
        Translators =
        [
            new JsonbBuildObjectTranslator(sqlExpressionFactory, typeMappingSource),
        ];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
