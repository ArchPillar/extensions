using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;

/// <summary>
/// Translates the flat <see cref="JsonbDbFunctions.JsonbBuildObjectCore(object?[])"/> marker
/// (produced from <c>EF.Functions.ToJsonb(shape)</c> by <see cref="JsonbShapeInterceptor"/>)
/// into the PostgreSQL <c>jsonb_build_object(...)</c> SQL function.
/// </summary>
internal sealed class JsonbBuildObjectTranslator : IMethodCallTranslator
{
    private const string FunctionName = "jsonb_build_object";

    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _textMapping;
    private readonly RelationalTypeMapping _jsonbMapping;

    public JsonbBuildObjectTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _textMapping = typeMappingSource.FindMapping(typeof(string), "text")
            ?? typeMappingSource.FindMapping(typeof(string))!;
        _jsonbMapping = typeMappingSource.FindMapping(typeof(string), "jsonb")
            ?? _textMapping;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(JsonbDbFunctions)
            || method.Name != nameof(JsonbDbFunctions.JsonbBuildObjectCore))
        {
            return null;
        }

        // The single argument is the desugared object?[] of alternating keys/values, which EF
        // translates to a PgNewArrayExpression.
        if (arguments.Count != 1 || arguments[0] is not PgNewArrayExpression array)
        {
            return null;
        }

        IReadOnlyList<SqlExpression> items = array.Expressions;
        if (items.Count == 0 || (items.Count % 2) != 0)
        {
            return null;
        }

        var mapped = new SqlExpression[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            mapped[i] = PrepareArgument(items[i]);
        }

        // jsonb_build_object returns a non-null jsonb even when values are null, so no
        // argument propagates nullability to the result.
        return _sql.Function(
            FunctionName,
            mapped,
            nullable: true,
            argumentsPropagateNullability: new bool[mapped.Length],
            typeof(string),
            _jsonbMapping);
    }

    private SqlExpression PrepareArgument(SqlExpression argument)
    {
        SqlExpression stripped = StripBoxingConvert(argument);

        if (stripped.TypeMapping is not null)
        {
            return stripped;
        }

        return _sql.ApplyTypeMapping(stripped, _textMapping);
    }

    private static SqlExpression StripBoxingConvert(SqlExpression expression)
    {
        while (expression is SqlUnaryExpression
        {
            OperatorType: ExpressionType.Convert,
            Type: var t,
            Operand: { } inner,
        } && t == typeof(object))
        {
            expression = inner;
        }

        return expression;
    }
}
