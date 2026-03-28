namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Provides a helper method used by <c>MapTo</c> to update an existing
/// collection in-place (clear + re-add) rather than replacing the reference.
/// This preserves the original collection instance, which is important for
/// EF Core change tracking, observable collections, and any scenario where
/// other code holds a reference to the original collection.
/// </summary>
internal static class CollectionUpdater
{
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
