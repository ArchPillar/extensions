using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every occurrence of a specific
/// <see cref="ParameterExpression"/> with a given replacement expression.
///
/// Used when inlining a nested mapper: the nested mapper's source parameter
/// is replaced with the actual member-access expression from the parent mapper
/// (for example, <c>source.Customer</c>).
/// </summary>
internal sealed class ParameterReplacer(ParameterExpression target, Expression replacement)
    : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => node == target ? replacement : base.VisitParameter(node);
}
