using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

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
        if (method.DeclaringType != typeof(EnumMappingFunctions))
        {
            return null;
        }

        var isNullable            = method.Name == nameof(EnumMappingFunctions.MapEnumNullable);
        var isNullableWithDefault = method.Name == nameof(EnumMappingFunctions.MapEnumNullableWithDefault);

        if (method.Name != nameof(EnumMappingFunctions.MapEnum) && !isNullable && !isNullableWithDefault)
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

        foreach ((var sourceValue, var destValue) in mappings)
        {
            SqlExpression test = sqlExpressionFactory.Constant(
                sourceValue, source.TypeMapping);

            SqlExpression result = sqlExpressionFactory.Constant(
                destValue, destMapping);

            whenClauses.Add(new CaseWhenClause(test, result));
        }

        SqlExpression caseExpression = sqlExpressionFactory.Case(source, whenClauses, elseResult: null);

        // Non-nullable: return the bare CASE expression.
        if (!isNullable && !isNullableWithDefault)
        {
            return caseExpression;
        }

        // Nullable variants: wrap in CASE WHEN source IS NOT NULL THEN … ELSE … END
        // For nullable-to-nullable, the ELSE is NULL (no else clause = implicit NULL).
        // For nullable-with-default, the ELSE is the user-supplied default value.
        SqlExpression? elseResult = isNullableWithDefault
            ? arguments[1]
            : null;

        return sqlExpressionFactory.Case(
            [new CaseWhenClause(
                sqlExpressionFactory.IsNotNull(source),
                caseExpression)],
            elseResult);
    }
}
