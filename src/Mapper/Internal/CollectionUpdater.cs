namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Runtime helpers invoked by compiled <c>MapTo</c> delegates to update
/// collection properties in-place rather than replacing the reference.
/// Methods are <see langword="public"/> (on an <see langword="internal"/>
/// class) so that <see cref="System.Reflection.BindingFlags.Public"/>
/// can be used when resolving them via reflection — avoiding the S3011
/// analyzer warning for accessibility bypass.
/// </summary>
internal static class CollectionUpdater
{
    // -----------------------------------------------------------------
    // Deep mode: clear + re-add newly mapped items
    // -----------------------------------------------------------------

    /// <summary>
    /// Clears <paramref name="target"/> and re-populates it with items from
    /// <paramref name="source"/>. When <paramref name="source"/> is
    /// <see langword="null"/> the target is simply cleared. When
    /// <paramref name="target"/> and <paramref name="source"/> are the same
    /// reference the call is a no-op (avoids clearing the source before
    /// reading it, e.g. when a clone mapper maps an object onto itself).
    /// </summary>
    public static void ReplaceContents<T>(ICollection<T> target, IEnumerable<T>? source)
    {
        if (source is null)
        {
            target.Clear();
            return;
        }

        if (ReferenceEquals(target, source))
        {
            return;
        }

        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }

    // -----------------------------------------------------------------
    // DeepWithIdentity mode: merge by key
    // -----------------------------------------------------------------

    /// <summary>
    /// Performs an identity-based merge of <paramref name="sourceItems"/> into
    /// <paramref name="destCollection"/>:
    /// <list type="number">
    /// <item>Index existing destination items by key.</item>
    /// <item>For each source item: if a matching destination item exists,
    ///        update it via <paramref name="mapTo"/>; otherwise create a new
    ///        one via <paramref name="map"/> and add it.</item>
    /// <item>Remove destination items that have no matching source item.</item>
    /// </list>
    /// When <paramref name="sourceItems"/> is <see langword="null"/> the
    /// destination collection is cleared (all items removed).
    /// </summary>
    public static void MergeWithIdentity<TSourceItem, TDestItem, TKey>(
        ICollection<TDestItem> destCollection,
        IEnumerable<TSourceItem>? sourceItems,
        Func<TSourceItem, TKey> sourceKeySelector,
        Func<TDestItem, TKey> destKeySelector,
        Func<TSourceItem, TDestItem> map,
        Action<TSourceItem, TDestItem> mapTo)
        where TKey : notnull
    {
        if (sourceItems is null)
        {
            destCollection.Clear();
            return;
        }

        // Index existing destination items by key
        var destByKey = new Dictionary<TKey, TDestItem>();
        foreach (TDestItem destItem in destCollection)
        {
            destByKey[destKeySelector(destItem)] = destItem;
        }

        var newItems = new List<TDestItem>();

        foreach (TSourceItem srcItem in sourceItems)
        {
            TKey key = sourceKeySelector(srcItem);
            if (destByKey.Remove(key, out TDestItem? destItem))
            {
                mapTo(srcItem, destItem);
            }
            else
            {
                newItems.Add(map(srcItem));
            }
        }

        // Remove destination items not present in source
        foreach (TDestItem item in destByKey.Values)
        {
            destCollection.Remove(item);
        }

        // Add new items
        foreach (TDestItem item in newItems)
        {
            destCollection.Add(item);
        }
    }

    // -----------------------------------------------------------------
    // Type inspection
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns the element type <c>T</c> if <paramref name="type"/> implements
    /// <see cref="ICollection{T}"/> and is not an array (arrays implement
    /// <see cref="ICollection{T}"/> but <see cref="ICollection{T}.Add"/> throws).
    /// Returns <see langword="null"/> otherwise.
    /// </summary>
    internal static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>))
        {
            return type.GetGenericArguments()[0];
        }

        foreach (Type iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ICollection<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
