using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

/// <summary>
/// Translates <c>EnumMappingFunctions.MapEnum&lt;TSource, TDest&gt;(source)</c>
/// into a flat SQL <c>CASE operand WHEN … THEN … END</c> expression with one
/// <see cref="CaseWhenClause"/> per enum value.
/// </summary>
internal sealed class EnumMapperMethodCallTranslator(
    EnumMappingStore store,
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // Only handle our marker method.
        if (method.DeclaringType != typeof(EnumMappingFunctions)
            || method.Name != nameof(EnumMappingFunctions.MapEnum))
        {
            return null;
        }

        Type[] typeArgs = method.GetGenericArguments();
        Type sourceType = typeArgs[0];
        Type destType = typeArgs[1];

        IReadOnlyList<(int SourceValue, int DestValue)>? mappings =
            store.GetMappings(sourceType, destType);

        if (mappings is null || mappings.Count == 0)
        {
            return null;
        }

        SqlExpression source = arguments[0];

        // Find the type mapping for the destination enum so the CASE result
        // has the correct SQL type.  Fall back to the source mapping (both
        // are typically int-backed enums).
        RelationalTypeMapping? destMapping =
            typeMappingSource.FindMapping(destType)
            ?? typeMappingSource.FindMapping(Enum.GetUnderlyingType(destType))
            ?? source.TypeMapping;

        // Build flat CASE:
        //   CASE source
        //     WHEN srcVal0 THEN destVal0
        //     WHEN srcVal1 THEN destVal1
        //     …
        //   END
        var whenClauses = new List<CaseWhenClause>(mappings.Count);

        foreach (var (sourceValue, destValue) in mappings)
        {
            SqlExpression test = sqlExpressionFactory.Constant(
                sourceValue, source.TypeMapping);

            SqlExpression result = sqlExpressionFactory.Constant(
                destValue, destMapping);

            whenClauses.Add(new CaseWhenClause(test, result));
        }

        return sqlExpressionFactory.Case(source, whenClauses, elseResult: null);
    }
}
