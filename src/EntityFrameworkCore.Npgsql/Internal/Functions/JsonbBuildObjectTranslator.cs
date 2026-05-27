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
/// Translates calls to <see cref="JsonbBuildObjectDbFunctions.JsonbBuildObject(Microsoft.EntityFrameworkCore.DbFunctions, object?[])"/>
/// into the PostgreSQL <c>jsonb_build_object(...)</c> SQL function.
/// </summary>
internal sealed class JsonbBuildObjectTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo _method = typeof(JsonbBuildObjectDbFunctions)
        .GetMethod(nameof(JsonbBuildObjectDbFunctions.JsonbBuildObject))!;

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
        if (method != _method)
        {
            return null;
        }

        IReadOnlyList<SqlExpression>? items = ExtractArrayItems(arguments);
        if (items is null)
        {
            return null;
        }

        if ((items.Count % 2) != 0)
        {
            return null;
        }

        var mapped = new SqlExpression[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            mapped[i] = PrepareArgument(items[i]);
        }

        var nullability = new bool[mapped.Length];
        for (var i = 0; i < nullability.Length; i++)
        {
            nullability[i] = false;
        }

        return _sql.Function(
            "jsonb_build_object",
            mapped,
            nullable: true,
            argumentsPropagateNullability: nullability,
            typeof(string),
            _jsonbMapping);
    }

    private static IReadOnlyList<SqlExpression>? ExtractArrayItems(
        IReadOnlyList<SqlExpression> arguments)
    {
        var start = (arguments.Count > 0 && IsDbFunctionsInstance(arguments[0])) ? 1 : 0;

        if (arguments.Count - start == 1 && arguments[start] is PgNewArrayExpression array)
        {
            return array.Expressions;
        }

        var rest = new List<SqlExpression>(arguments.Count - start);
        for (var i = start; i < arguments.Count; i++)
        {
            rest.Add(arguments[i]);
        }

        return rest;
    }

    private static bool IsDbFunctionsInstance(SqlExpression expression)
        => expression.Type == typeof(Microsoft.EntityFrameworkCore.DbFunctions);

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
