using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// Scans an expression tree for the first nested-mapper call pattern —
/// <c>Mapper&lt;X,Y&gt;.Map(srcExpr)</c>,
/// <c>EnumMapper&lt;X,Y&gt;.Map(srcExpr)</c>, or
/// <c>MapperExtensions.Project(sourceExpr, mapper)</c> — and records
/// a deferred accessor for the mapper instance plus the source-access
/// sub-expression.
///
/// The accessor is compiled from the expression tree so the mapper
/// instance does not need to exist at detection time — only when the
/// accessor is invoked (typically on first use of the parent mapper).
///
/// After <see cref="Detect"/> returns, inspect
/// <see cref="NestedMapperAccessor"/>, <see cref="SourceAccess"/>,
/// and <see cref="IsCollection"/>.
/// </summary>
internal sealed class NestedMapperDetector : ExpressionVisitor
{
    public Func<IMapper>? NestedMapperAccessor { get; private set; }
    public Expression?    SourceAccess         { get; private set; }
    public bool           IsCollection         { get; private set; }
    public bool           Found                => NestedMapperAccessor != null;

    public void Detect(Expression expression) => Visit(expression);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Mapper<X,Y>.Map(srcExpr) or EnumMapper<X,Y>.Map(srcExpr)
        if (!Found && IsMapperMapCall(node))
        {
            NestedMapperAccessor = CompileAccessor(node.Object!);
            SourceAccess = node.Arguments[0];
            IsCollection = false;
            return node; // stop recursing into this sub-tree
        }

        // MapperExtensions.Project(sourceExpr, mapper) — IEnumerable overload (no options)
        if (!Found && IsProjectCall(node))
        {
            NestedMapperAccessor = CompileAccessor(node.Arguments[1]);
            SourceAccess = node.Arguments[0];
            IsCollection = true;
            return node;
        }

        return base.VisitMethodCall(node);
    }

    private static bool IsMapperMapCall(MethodCallExpression node)
        => node.Object != null
        && node.Method.Name == "Map"
        && node.Arguments.Count == 1
        && (IsClosedGenericOf(node.Object.Type, typeof(Mapper<,>))
         || IsClosedGenericOf(node.Object.Type, typeof(EnumMapper<,>)));

    private static bool IsProjectCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperExtensions)
        && node.Method.Name == "Project"
        && node.Arguments.Count == 2;

    /// <summary>
    /// Compiles the expression that references the mapper into a deferred
    /// <c>Func&lt;IMapper&gt;</c>. This handles any closure depth
    /// (constant, single member access, deeper chains) and does not
    /// evaluate the mapper property until the returned delegate is invoked.
    /// </summary>
    private static Func<IMapper> CompileAccessor(Expression expression)
    {
        var lambda = Expression.Lambda<Func<IMapper>>(
            Expression.Convert(expression, typeof(IMapper)));
        return lambda.Compile();
    }

    private static bool IsClosedGenericOf(Type type, Type genericTypeDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
}
