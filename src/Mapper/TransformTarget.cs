namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Selects which compilation path(s) an <see cref="IExpressionTransformer"/>
/// applies to. A single mapper definition is compiled into two forms — an
/// in-memory delegate (<c>Map</c>) and a LINQ projection (<c>ToExpression</c>) —
/// and a transformer may target one or both.
/// <para>
/// Path targeting exists for cases where the same logical operation needs a
/// different <em>encoding</em> per execution engine — for example, a domain
/// method that runs as real CLR code in memory but maps to a SQL function in the
/// database. The transformed result must remain semantically equivalent across
/// paths: targeting changes only the encoding, never the observable result.
/// </para>
/// </summary>
public enum TransformTarget
{
    /// <summary>
    /// Applies to both the in-memory delegate and the LINQ projection.
    /// This is the default.
    /// </summary>
    Both,

    /// <summary>
    /// Applies only to the LINQ projection produced by <c>ToExpression</c>
    /// (the EF Core / <see cref="System.Linq.IQueryable"/> path). The transformer
    /// is skipped when compiling the in-memory delegate, leaving the original
    /// expression to execute as real CLR code.
    /// </summary>
    ExpressionOnly,

    /// <summary>
    /// Applies only to the in-memory delegate produced for <c>Map</c>.
    /// The transformer is skipped when building the LINQ projection.
    /// </summary>
    InMemoryOnly,
}
