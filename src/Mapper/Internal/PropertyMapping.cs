using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

internal enum MappingKind { Required, Optional, Ignored }

/// <summary>
/// Represents a single property binding produced by the builder and consumed by
/// <see cref="Mapper{TSource,TDest}"/>.
/// </summary>
/// <param name="Destination">The destination property.</param>
/// <param name="Source">
/// The source expression, or <c>null</c> for <see cref="MappingKind.Ignored"/>
/// entries. Nested mapper calls (<c>mapper.Map()</c>, <c>.Project()</c>) are stored
/// raw and inlined at expression-build time by <see cref="NestedMapperInliner"/>.
/// </param>
/// <param name="Kind">Whether this mapping is required, optional, or ignored.</param>
/// <param name="CollectionConfig">
/// Optional configuration that controls how <c>MapTo</c> handles a collection
/// property. When <see langword="null"/>, <c>MapTo</c> uses the default
/// <see cref="CollectionMapToMode.Shallow"/> mode (replace the collection reference).
/// </param>
internal sealed record PropertyMapping(
    MemberInfo              Destination,
    LambdaExpression?       Source,
    MappingKind             Kind,
    CollectionMapToConfig?  CollectionConfig = null);

/// <summary>
/// Configuration for collection handling in <c>MapTo</c>, stored on
/// <see cref="PropertyMapping"/> for collection properties that need
/// non-default behaviour.
/// </summary>
internal sealed class CollectionMapToConfig
{
    /// <summary>The update strategy.</summary>
    public required CollectionMapToMode Mode { get; init; }

    /// <summary>
    /// For <see cref="CollectionMapToMode.DeepWithIdentity"/>: expression
    /// <c>src =&gt; src.Collection</c> that accesses the raw source collection
    /// (before projection).
    /// </summary>
    public LambdaExpression? SourceCollectionAccessor { get; init; }

    /// <summary>
    /// For <see cref="CollectionMapToMode.DeepWithIdentity"/>: compiled
    /// key-extractor for source elements.
    /// </summary>
    public Delegate? SourceKeySelector { get; init; }

    /// <summary>
    /// For <see cref="CollectionMapToMode.DeepWithIdentity"/>: compiled
    /// key-extractor for destination elements.
    /// </summary>
    public Delegate? DestKeySelector { get; init; }

    /// <summary>
    /// For <see cref="CollectionMapToMode.DeepWithIdentity"/>: compiled
    /// <c>Func&lt;TSourceItem, TDestItem&gt;</c> that maps a source element
    /// to a new destination element (wraps the nested mapper's <c>Map</c>).
    /// </summary>
    public Delegate? MapFunc { get; init; }

    /// <summary>
    /// For <see cref="CollectionMapToMode.DeepWithIdentity"/>: compiled
    /// <c>Action&lt;TSourceItem, TDestItem&gt;</c> that maps a source element
    /// onto an existing destination element (wraps the nested mapper's
    /// <c>MapTo</c>).
    /// </summary>
    public Delegate? MapToAction { get; init; }
}
