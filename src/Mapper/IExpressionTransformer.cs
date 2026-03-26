using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Transforms an expression tree during mapper compilation. Implementations
/// typically subclass <see cref="ExpressionVisitor"/> to rewrite specific
/// patterns (e.g. replacing custom implicit conversions with EF Core-translatable
/// equivalents).
/// <para>
/// Transformers run after nested mapper inlining and before variable
/// substitution, in the order: global → per-context → per-mapper.
/// </para>
/// </summary>
public interface IExpressionTransformer
{
    /// <summary>
    /// Transforms the given expression tree, returning a rewritten tree or the
    /// original if no changes are needed.
    /// </summary>
    Expression Transform(Expression expression);
}
