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
/// <para>
/// By default a transformer runs on both compilation paths (in-memory and LINQ
/// projection). Override <see cref="Target"/> to confine it to a single path —
/// see <see cref="TransformTarget"/>.
/// </para>
/// </summary>
public interface IExpressionTransformer
{
    /// <summary>
    /// Transforms the given expression tree, returning a rewritten tree or the
    /// original if no changes are needed.
    /// </summary>
    public Expression Transform(Expression expression);

    /// <summary>
    /// The compilation path(s) this transformer applies to. Defaults to
    /// <see cref="TransformTarget.Both"/>. Override to confine the rewrite to the
    /// LINQ projection (<see cref="TransformTarget.Expression"/>) or the
    /// in-memory delegate (<see cref="TransformTarget.InMemory"/>) — for example,
    /// to rewrite a domain method into a SQL-function call only on the EF Core
    /// path while the original method runs in memory. The two paths must stay
    /// semantically equivalent.
    /// </summary>
    public TransformTarget Target => TransformTarget.Both;
}
