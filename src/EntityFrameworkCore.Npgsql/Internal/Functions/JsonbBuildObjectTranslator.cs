using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;

/// <summary>
/// Translates the <c>JsonbBuildObjectDbFunctions</c> markers into the PostgreSQL
/// <c>jsonb_build_object(...)</c> SQL function. Handles both the fixed-arity
/// <c>JsonbBuildObject(...)</c> overloads and the fluent
/// <c>JsonbObject().Add(...).Build()</c> builder chain. The chain is folded one node at a
/// time as EF translates the expression tree bottom-up: the seed becomes an empty
/// <c>jsonb_build_object()</c>, each <c>Add</c> appends its key/value to the accumulated
/// function, and <c>Build</c> yields the result as text.
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
        if (method.DeclaringType != typeof(JsonbBuildObjectDbFunctions))
        {
            return null;
        }

        return method.Name switch
        {
            // Seed carries [DbFunctions, key, value]; flat overloads carry [DbFunctions, key, value, …].
            nameof(JsonbBuildObjectDbFunctions.JsonbObject) => TranslateFlat(arguments),
            nameof(JsonbBuildObjectDbFunctions.Add) => TranslateAdd(arguments),
            nameof(JsonbBuildObjectDbFunctions.Build) => TranslateBuild(arguments),
            nameof(JsonbBuildObjectDbFunctions.JsonbBuildObject) => TranslateFlat(arguments),
            _ => null,
        };
    }

    private SqlExpression? TranslateAdd(IReadOnlyList<SqlExpression> arguments)
    {
        // arguments: [accumulated jsonb_build_object(...), key, value]
        if (arguments.Count != 3 || arguments[0] is not SqlFunctionExpression { Name: FunctionName } current)
        {
            return null;
        }

        IReadOnlyList<SqlExpression> existing = current.Arguments ?? [];
        var next = new SqlExpression[existing.Count + 2];
        for (var i = 0; i < existing.Count; i++)
        {
            next[i] = existing[i];
        }

        next[existing.Count] = PrepareArgument(arguments[1]);
        next[existing.Count + 1] = PrepareArgument(arguments[2]);

        return CreateFunction(next);
    }

    private static SqlExpression? TranslateBuild(IReadOnlyList<SqlExpression> arguments)
    {
        // arguments: [accumulated jsonb_build_object(...)]
        if (arguments.Count != 1 || arguments[0] is not SqlFunctionExpression { Name: FunctionName } current)
        {
            return null;
        }

        return current;
    }

    private SqlExpression? TranslateFlat(IReadOnlyList<SqlExpression> arguments)
    {
        var start = (arguments.Count > 0 && IsDbFunctionsInstance(arguments[0])) ? 1 : 0;
        var count = arguments.Count - start;

        if (count == 0 || (count % 2) != 0)
        {
            return null;
        }

        var mapped = new SqlExpression[count];
        for (var i = 0; i < count; i++)
        {
            mapped[i] = PrepareArgument(arguments[start + i]);
        }

        return CreateFunction(mapped);
    }

    private SqlExpression CreateFunction(IReadOnlyList<SqlExpression> mapped)
        // jsonb_build_object returns a non-null jsonb even when values are null, so no
        // argument propagates nullability to the result.
        => _sql.Function(
            FunctionName,
            mapped,
            nullable: true,
            argumentsPropagateNullability: new bool[mapped.Count],
            typeof(string),
            _jsonbMapping);

    private static bool IsDbFunctionsInstance(SqlExpression expression)
        => expression.Type == typeof(DbFunctions);

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
