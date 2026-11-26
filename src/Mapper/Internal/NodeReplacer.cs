using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Replaces one specific node (matched by reference) with another expression.
/// </summary>
internal sealed class NodeReplacer(Expression target, Expression replacement) : ExpressionVisitor
{
    public override Expression? Visit(Expression? node)
        => node == target ? replacement : base.Visit(node);
}
