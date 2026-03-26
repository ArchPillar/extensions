using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Replaces calls to a specific method with a custom expression returned by
/// <see cref="Replacement"/>. Subclasses identify the target method via
/// <see cref="Method"/>.
/// <para>
/// This is useful when a domain type exposes a method that EF Core cannot
/// translate. Subclasses replace the method call with an equivalent expression
/// tree that EF Core understands.
/// </para>
/// <example>
/// Replace <c>money.IsPositive()</c> with <c>money.Amount &gt; 0</c>:
/// <code>
/// public sealed class IsPositiveTransformer : MethodCallTransformer
/// {
///     protected override MethodInfo Method { get; }
///         = typeof(Money).GetMethod(nameof(Money.IsPositive))!;
///
///     protected override Expression Replacement(
///         Expression? instance, IReadOnlyList&lt;Expression&gt; arguments)
///         =&gt; Expression.GreaterThan(
///             Expression.Property(instance!, nameof(Money.Amount)),
///             Expression.Constant(0m));
/// }
/// </code>
/// </example>
/// </summary>
public abstract class MethodCallTransformer : ExpressionVisitor, IExpressionTransformer
{
    /// <inheritdoc />
    public Expression Transform(Expression expression)
    {
        return Visit(expression);
    }

    /// <summary>
    /// The method to match. For generic methods, return the open generic
    /// definition — closed instantiations are matched automatically.
    /// </summary>
    protected abstract MethodInfo Method { get; }

    /// <summary>
    /// Returns the expression to substitute for the matched method call.
    /// </summary>
    /// <param name="instance">
    /// The already-visited instance expression, or <see langword="null"/>
    /// for static method calls.
    /// </param>
    /// <param name="arguments">The already-visited argument expressions.</param>
    protected abstract Expression Replacement(
        Expression? instance, IReadOnlyList<Expression> arguments);

    /// <inheritdoc />
    protected sealed override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (IsMatch(node))
        {
            Expression? instance = node.Object is not null ? Visit(node.Object) : null;
            IReadOnlyList<Expression> arguments = VisitArguments(node.Arguments);
            return Replacement(instance, arguments);
        }

        return base.VisitMethodCall(node);
    }

    private bool IsMatch(MethodCallExpression node)
    {
        MethodInfo target = Method;
        MethodInfo candidate = node.Method;

        if (candidate == target)
        {
            return true;
        }

        if (candidate.IsGenericMethod && target.IsGenericMethodDefinition)
        {
            return candidate.GetGenericMethodDefinition() == target;
        }

        return false;
    }

    private IReadOnlyList<Expression> VisitArguments(
        IReadOnlyList<Expression> arguments)
    {
        var visited = new Expression[arguments.Count];

        for (var i = 0; i < arguments.Count; i++)
        {
            visited[i] = Visit(arguments[i])!;
        }

        return visited;
    }
}
