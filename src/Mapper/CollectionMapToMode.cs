namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Controls how <see cref="Mapper{TSource,TDest}.MapTo"/> updates collection
/// properties on the destination instance.
/// </summary>
public enum CollectionMapToMode
{
    /// <summary>
    /// Replace the entire collection reference with a newly mapped collection.
    /// This is the default and matches the behaviour of
    /// <see cref="Mapper{TSource,TDest}.Map(TSource)"/>.
    /// </summary>
    Shallow,

    /// <summary>
    /// Preserve the existing collection instance: clear it and re-add newly
    /// mapped items. The collection reference on the destination is unchanged,
    /// which is useful when other code holds a reference to the original
    /// collection (e.g. observable collections, data-binding scenarios).
    /// </summary>
    Deep,

    /// <summary>
    /// Preserve both the collection instance and individual element instances
    /// that match by key. Existing items are updated via <c>MapTo</c>, new
    /// source items are mapped and added, and destination items not present
    /// in the source are removed. This is critical for EF Core change-tracked
    /// collections where the tracker needs to see individual entity state
    /// changes (<c>Modified</c> / <c>Added</c> / <c>Deleted</c>).
    /// </summary>
    DeepWithIdentity,
}
