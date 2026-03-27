using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every nested-mapper call in-place with
/// the inlined body of the nested mapper's expression tree.
/// <list type="bullet">
/// <item>
/// <c>Mapper&lt;X,Y&gt;.Map(srcExpr)</c> and <c>EnumMapper&lt;X,Y&gt;.Map(srcExpr)</c>
/// are replaced with the nested mapper's expression body, parameter-substituted with
/// <c>srcExpr</c>. Reference-type source expressions are wrapped in a null guard.
/// </item>
/// <item>
/// <c>srcColl.Project(mapper)</c> is replaced with
/// <c>Enumerable.Select(srcColl, nestedLambda)</c>.
/// The materialization call (<c>.ToList()</c>, <c>.ToHashSet()</c>, etc.) already
/// present in the user's expression is preserved as-is.
/// </item>
/// </list>
/// <para>
/// Unlike the previous two-phase detect-then-stitch approach, this visitor handles
/// arbitrary expression shapes: multiple mapper calls per property, mapper calls
/// inside <c>ToDictionary</c> value selectors, ternary expressions, and so on.
/// </para>
/// </summary>
internal sealed class NestedMapperInliner(IncludeSet includes, int depth = 0) : ExpressionVisitor
{
    internal const int MaxNestingDepth = 32;

    private static readonly MethodInfo EnumerableSelectMethod =
        typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "Select" && m.GetParameters().Length == 2);

    /// <summary>
    /// For inline <c>new TypeX { Prop = ... }</c> initializers, each member binding
    /// is visited with a sub-inliner scoped to the nested <see cref="IncludeSet"/>
    /// for that destination property name.  This lets include paths follow the
    /// full property chain — e.g. <c>"Pack.Primary.Tag"</c> reaches the <c>Tag</c>
    /// optional inside the <c>Primary</c> binding of an inline <c>new PackDest</c>
    /// without affecting the sibling <c>Secondary</c> binding.
    /// </summary>
    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        ValidateInlineIncludes(node);

        var newExpr    = (NewExpression)Visit(node.NewExpression)!;
        var newBindings = new List<MemberBinding>(node.Bindings.Count);

        foreach (MemberBinding binding in node.Bindings)
        {
            if (binding is MemberAssignment assignment)
            {
                var memberName    = assignment.Member.Name;
                IncludeSet memberIncludes = includes.IncludeAll
                    ? IncludeSet.All
                    : includes.Nested.GetValueOrDefault(memberName, IncludeSet.Empty);
                Expression visited = new NestedMapperInliner(memberIncludes, depth).Visit(assignment.Expression)!;
                newBindings.Add(Expression.Bind(assignment.Member, visited));
            }
            else
            {
                newBindings.Add(VisitMemberBinding(binding));
            }
        }

        return node.Update(newExpr, newBindings);
    }

    /// <summary>
    /// Validates that every name in the current <see cref="IncludeSet"/> corresponds
    /// to a member binding in the inline <see cref="MemberInitExpression"/>. Throws
    /// <see cref="InvalidOperationException"/> for unrecognised names, catching typos
    /// in deep include paths that traverse inline object initializers.
    /// </summary>
    private void ValidateInlineIncludes(MemberInitExpression node)
    {
        if (includes.IncludeAll)
        {
            return;
        }

        var memberNames = new HashSet<string>(
            node.Bindings
                .OfType<MemberAssignment>()
                .Select(b => b.Member.Name));

        foreach (var name in includes.Names)
        {
            if (!memberNames.Contains(name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }

        foreach (var name in includes.Nested.Keys)
        {
            if (!memberNames.Contains(name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Mapper<X,Y>.Map(srcExpr) or EnumMapper<X,Y>.Map(srcExpr)
        if (IsScalarMapCall(node))
        {
            IMapper nestedMapper = CompileMapperAccessor(node.Object!);
            LambdaExpression nestedLambda = nestedMapper.GetRawExpression(includes, depth + 1);
            Expression srcExpr = Visit(node.Arguments[0])!;

            // Nullable value type source (e.g. TSource?) — unwrap via .Value,
            // inline the core expression, then wrap with a HasValue guard.
            Type? nullableUnderlying = Nullable.GetUnderlyingType(srcExpr.Type);

            if (nullableUnderlying != null && nullableUnderlying == nestedLambda.Parameters[0].Type)
            {
                Expression valueExpr = Expression.Property(srcExpr, "Value");
                Expression inlined = new ParameterReplacer(nestedLambda.Parameters[0], valueExpr)
                                         .Visit(nestedLambda.Body)!;

                // 2-arg overload: Map(TSource?, TDest defaultValue) — use the
                // second argument as the fallback when source is null.
                if (node.Arguments.Count == 2)
                {
                    Expression defaultExpr = Visit(node.Arguments[1])!;
                    return Expression.Condition(
                        Expression.Property(srcExpr, "HasValue"),
                        inlined,
                        defaultExpr);
                }

                // 1-arg overload: Map(TSource?) → TDest? — null in, null out.
                Type nullableReturnType = typeof(Nullable<>).MakeGenericType(inlined.Type);
                return Expression.Condition(
                    Expression.Property(srcExpr, "HasValue"),
                    Expression.Convert(inlined, nullableReturnType),
                    Expression.Default(nullableReturnType));
            }

            Expression inlinedBody = new ParameterReplacer(nestedLambda.Parameters[0], srcExpr)
                                         .Visit(nestedLambda.Body)!;

            if (!srcExpr.Type.IsValueType)
            {
                return Expression.Condition(
                    Expression.Equal(srcExpr, Expression.Default(srcExpr.Type)),
                    Expression.Default(inlinedBody.Type),
                    inlinedBody);
            }

            return inlinedBody;
        }

        // srcColl.Project(mapper) — IEnumerable overload (no options)
        if (IsProjectCall(node))
        {
            IMapper nestedMapper = CompileMapperAccessor(node.Arguments[1]);
            LambdaExpression nestedLambda = nestedMapper.GetRawExpression(includes, depth + 1);
            Expression srcExpr = Visit(node.Arguments[0])!;
            Type srcType  = nestedLambda.Parameters[0].Type;
            Type destType = nestedLambda.ReturnType;

            return Expression.Call(
                EnumerableSelectMethod.MakeGenericMethod(srcType, destType),
                srcExpr,
                nestedLambda);
        }

        return base.VisitMethodCall(node);
    }

    /// <summary>
    /// Compiles the expression referencing the mapper instance into an
    /// <see cref="IMapper"/> by wrapping it in a parameterless lambda and
    /// invoking it immediately. Handles any closure depth (direct field,
    /// property chain, etc.) without evaluating the mapper until called.
    /// </summary>
    private static IMapper CompileMapperAccessor(Expression expression)
    {
        var lambda = Expression.Lambda<Func<IMapper>>(
            Expression.Convert(expression, typeof(IMapper)));
        return lambda.Compile().Invoke();
    }

    private static bool IsScalarMapCall(MethodCallExpression node)
        => node.Object != null
        && node.Method.Name == "Map"
        && node.Arguments.Count >= 1
        && node.Arguments.Count <= 2
        && (IsClosedGenericOf(node.Object.Type, typeof(Mapper<,>))
         || IsClosedGenericOf(node.Object.Type, typeof(EnumMapper<,>)));

    // Only the 2-argument IEnumerable.Project(mapper) overload can be inlined.
    // The 3-argument overload (mapper, options) carries runtime MapOptions
    // (includes + variable bindings) that cannot be statically baked into an
    // expression tree, so it is intentionally excluded here.
    private static bool IsProjectCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperExtensions)
        && node.Method.Name == "Project"
        && node.Arguments.Count == 2;

    private static bool IsClosedGenericOf(Type type, Type genericTypeDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
}
