using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Replaces <c>(<typeparamref name="TTarget"/>)operand</c> cast expressions
/// (where the operand is of type <typeparamref name="TSource"/>) with a custom
/// expression returned by <see cref="Replacement"/>.
/// <para>
/// This is useful when a domain type defines an implicit or explicit conversion
/// operator that EF Core cannot translate. Subclasses replace the cast with an
/// equivalent member access or computation that EF Core understands.
/// </para>
/// <example>
/// Replace <c>(decimal)money</c> with <c>money.Amount</c>:
/// <code>
/// public sealed class MoneyToAmountTransformer : CastTransformer&lt;Money, decimal&gt;
/// {
///     protected override Expression Replacement(Expression operand)
///         =&gt; Expression.Property(operand, nameof(Money.Amount));
/// }
/// </code>
/// </example>
/// </summary>
/// <typeparam name="TSource">
/// The type being cast from (the operand type). The match uses
/// <see cref="Type.IsAssignableTo"/>, so specifying a base class or
/// interface matches all derived/implementing types.
/// </typeparam>
/// <typeparam name="TTarget">The type being cast to (the conversion target).</typeparam>
public abstract class CastTransformer<TSource, TTarget> : ExpressionVisitor, IExpressionTransformer
{
    /// <inheritdoc />
    public Expression Transform(Expression expression)
    {
        return Visit(expression);
    }

    /// <summary>
    /// Returns the expression to substitute for the matched cast.
    /// The returned expression must be of type <typeparamref name="TTarget"/>.
    /// </summary>
    /// <param name="operand">
    /// The already-visited cast operand (the expression being cast from
    /// a type assignable to <typeparamref name="TSource"/> to
    /// <typeparamref name="TTarget"/>).
    /// </param>
    protected abstract Expression Replacement(Expression operand);

    /// <inheritdoc />
    protected sealed override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert
            && node.Type == typeof(TTarget)
            && node.Operand.Type.IsAssignableTo(typeof(TSource)))
        {
            return Replacement(Visit(node.Operand)!);
        }

        return base.VisitUnary(node);
    }
}
