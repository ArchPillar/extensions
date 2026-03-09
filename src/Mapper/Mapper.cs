using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// A compiled, reusable mapping between <typeparamref name="TSource"/> and
/// <typeparamref name="TDest"/>. Instances are created via
/// <see cref="MapperContext.CreateMapper{TSource,TDest}"/> and should be held
/// as properties on a <see cref="MapperContext"/> subclass.
///
/// The same configuration drives both in-memory object mapping (compiled
/// delegate) and LINQ expression projection (expression tree).
///
/// <b>Nesting inside parent mapper expressions:</b>
///
/// For a single nested object, call <see cref="Map(TSource)"/> (the
/// expression-safe, no-options overload) — the expression visitor detects
/// the call and inline this mapper's expression tree into the parent:
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Customer = CustomerMapper.Map(src.Customer),
/// });
/// </code>
///
/// For nested collections, use the <c>IEnumerable&lt;T&gt;.Project(mapper)</c>
/// extension — also detected and inlined by the visitor:
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Lines = src.Lines.Project(OrderLine).ToList(),
///     Tags  = src.Tags.Project(TagMapper).ToHashSet(),
/// });
/// </code>
///
/// For dictionaries, call <see cref="Map(TSource)"/> inline inside the
/// standard <c>ToDictionary</c> lambda:
/// <code>
///     LookupById = src.Items.ToDictionary(i => i.Id, i => ItemMapper.Map(i)),
/// </code>
/// </summary>
public sealed class Mapper<TSource, TDest>
{
    // -------------------------------------------------------------------------
    // In-memory object mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a single source instance to a new destination instance.
    /// Returns <see langword="null"/> when <paramref name="source"/> is
    /// <see langword="null"/>.
    ///
    /// Use this overload at call sites outside expression trees.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source, Action<MapOptions<TDest>>? options = null)
        => throw new NotImplementedException();

    /// <summary>
    /// Expression-safe single-item overload: no optional parameters, so it
    /// can appear inside parent mapper member-init expressions and inside
    /// lambdas passed to <c>ToDictionary</c>, <c>Where</c>, etc.
    /// The expression visitor inlines this mapper's expression at build time.
    ///
    /// Accepts a nullable source; returns <see langword="null"/> when
    /// <paramref name="source"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source)
        => throw new NotImplementedException();

    // -------------------------------------------------------------------------
    // LINQ / expression projection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the mapping as a composable LINQ expression tree, including
    /// any requested optional properties and variable bindings.
    /// The expression can be passed directly to a LINQ provider (e.g. EF Core).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression(
        Action<ProjectionOptions<TDest>>? options = null)
        => throw new NotImplementedException();
}
