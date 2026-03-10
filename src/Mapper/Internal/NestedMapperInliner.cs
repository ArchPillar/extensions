using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces nested mapper and enum mapper call expressions
/// with their fully inlined expression trees.
///
/// Handles three call patterns detected in parent mapper expressions:
/// <list type="bullet">
///   <item><see cref="Mapper{TSource,TDest}.Map(TSource)"/> — the expression-safe
///   single-argument overload; replaced with the nested mapper's required-only
///   expression body with its source parameter substituted.</item>
///   <item><see cref="MapperExtensions.Project{TSource,TDest}(IEnumerable{TSource}, Mapper{TSource,TDest})"/>
///   — replaced with <c>Enumerable.Select(source, nestedExpression)</c>.</item>
///   <item><see cref="EnumMapper{TSource,TDest}.Map(TSource)"/> — replaced with the
///   enum mapper's conditional expression tree with its source parameter substituted.</item>
/// </list>
///
/// Mapper and enum mapper instances are extracted from the expression tree by reading
/// the target object of each call as a <see cref="ConstantExpression"/> or as a
/// <see cref="MemberExpression"/> over a <see cref="ConstantExpression"/> (the common
/// captured-closure pattern).
/// </summary>
internal sealed class NestedMapperInliner : ExpressionVisitor
{
    private static readonly MethodInfo EnumerableSelectMethod =
        typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "Select" && m.GetParameters().Length == 2);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // EnumMapper<X,Y>.Map(arg)
        if (IsEnumMapperMapCall(node))
        {
            var enumMapper = (IEnumMapper)ExtractInstance(node.Object!);
            var lambda     = enumMapper.GetExpression();
            var argument   = Visit(node.Arguments[0]);
            return new ParameterReplacer(lambda.Parameters[0], argument).Visit(lambda.Body);
        }

        // Mapper<X,Y>.Map(arg) — expression-safe single-argument overload
        if (IsMapperMapCall(node))
        {
            var mapper   = (IMapper)ExtractInstance(node.Object!);
            var lambda   = mapper.GetBaseExpression();
            var argument = Visit(node.Arguments[0]);
            return new ParameterReplacer(lambda.Parameters[0], argument).Visit(lambda.Body);
        }

        // MapperExtensions.Project(source, mapper) — IEnumerable overload (no options)
        if (IsProjectCall(node))
        {
            var mapper       = (IMapper)ExtractInstance(node.Arguments[1]);
            var lambda       = mapper.GetBaseExpression();
            var source       = Visit(node.Arguments[0]);
            var sourceType   = lambda.Parameters[0].Type;
            var destType     = lambda.ReturnType;
            var selectMethod = EnumerableSelectMethod.MakeGenericMethod(sourceType, destType);
            return Expression.Call(selectMethod, source, lambda);
        }

        return base.VisitMethodCall(node);
    }

    private static bool IsEnumMapperMapCall(MethodCallExpression node)
        => node.Object != null
        && node.Method.Name == "Map"
        && node.Arguments.Count == 1
        && IsClosedGenericOf(node.Object.Type, typeof(EnumMapper<,>));

    private static bool IsMapperMapCall(MethodCallExpression node)
        => node.Object != null
        && node.Method.Name == "Map"
        && node.Arguments.Count == 1
        && IsClosedGenericOf(node.Object.Type, typeof(Mapper<,>));

    private static bool IsProjectCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperExtensions)
        && node.Method.Name == "Project"
        && node.Arguments.Count == 2;

    /// <summary>
    /// Extracts the runtime instance from an expression that is either a direct
    /// <see cref="ConstantExpression"/> or a property/field access on one (closure pattern).
    /// </summary>
    private static object ExtractInstance(Expression expression)
    {
        if (expression is ConstantExpression constant)
            return constant.Value!;

        if (expression is MemberExpression { Expression: ConstantExpression target } member)
        {
            return member.Member switch
            {
                PropertyInfo property => property.GetValue(target.Value)!,
                FieldInfo    field    => field.GetValue(target.Value)!,
                _ => throw new InvalidOperationException(
                    $"Cannot extract mapper instance from member '{member.Member.Name}'."),
            };
        }

        throw new InvalidOperationException(
            $"Cannot extract mapper instance from expression of kind '{expression.NodeType}'.");
    }

    private static bool IsClosedGenericOf(Type type, Type genericTypeDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
}
