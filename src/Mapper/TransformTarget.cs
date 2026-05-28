namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Selects which compilation path(s) an <see cref="IExpressionTransformer"/>
/// applies to. A single mapper definition is compiled into two forms — an
/// in-memory delegate (<c>Map</c>) and a LINQ projection (<c>ToExpression</c>) —
/// each represented by a flag. A transformer's <see cref="IExpressionTransformer.Target"/>
/// is the set of paths it runs on; combine flags to target more than one.
/// <para>
/// Path targeting exists for cases where the same logical operation needs a
/// different <em>encoding</em> per execution engine — for example, a domain
/// method that runs as real CLR code in memory but maps to a SQL function in the
/// database. The transformed result must remain semantically equivalent across
/// paths: targeting changes only the encoding, never the observable result.
/// </para>
/// </summary>
[Flags]
public enum TransformTarget
{
    /// <summary>
    /// No path. A transformer with this target never runs.
    /// </summary>
    None = 0,

    /// <summary>
    /// The LINQ projection produced by <c>ToExpression</c> (the EF Core /
    /// <see cref="System.Linq.IQueryable"/> path).
    /// </summary>
    Expression = 1,

    /// <summary>
    /// The in-memory delegate produced for <c>Map</c>, executed as CLR code.
    /// </summary>
    InMemory = 2,

    /// <summary>
    /// Both the LINQ projection and the in-memory delegate. This is the default
    /// for a transformer that does not override its target.
    /// </summary>
    Both = Expression | InMemory,
}
