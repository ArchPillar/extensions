using System.Collections.Concurrent;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

/// <summary>
/// Thread-safe store for enum mapping tables, keyed by (TSource, TDest) type pair.
/// Populated by <see cref="EnumMapperQueryInterceptor"/> when it encounters
/// <c>EnumMapper&lt;,&gt;.Map()</c> calls, and queried by
/// <see cref="EnumMapperMethodCallTranslator"/> to produce flat SQL CASE expressions.
/// </summary>
internal sealed class EnumMappingStore
{
    private readonly ConcurrentDictionary<(Type Source, Type Dest), IReadOnlyList<(int SourceValue, int DestValue)>> _mappings = new();

    /// <summary>
    /// Registers the mapping table for the given <paramref name="enumMapper"/> instance.
    /// Enumerates all <typeparamref name="TSource"/> values, calls <c>Map()</c> for each,
    /// and stores the resulting (sourceInt, destInt) pairs.
    /// </summary>
    internal void Register<TSource, TDest>(EnumMapper<TSource, TDest> enumMapper)
        where TSource : struct, Enum
        where TDest : struct, Enum
    {
        (Type, Type) key = (typeof(TSource), typeof(TDest));

        _mappings.GetOrAdd(key, _ =>
        {
            TSource[] values = Enum.GetValues<TSource>();
            var pairs = new List<(int, int)>(values.Length);

            foreach (TSource value in values)
            {
                TDest mapped = enumMapper.Map(value);
                pairs.Add((Convert.ToInt32(value), Convert.ToInt32(mapped)));
            }

            return pairs;
        });
    }

    /// <summary>
    /// Registers a mapping table extracted at runtime from a non-generic
    /// <see cref="EnumMapper{TSource,TDest}"/> instance via reflection.
    /// Called by the interceptor when it discovers a <c>Map()</c> call.
    /// </summary>
    internal void RegisterDynamic(object enumMapper, Type sourceType, Type destType)
    {
        (Type, Type) key = (sourceType, destType);

        _mappings.GetOrAdd(key, _ =>
        {
            Array values = Enum.GetValues(sourceType);
            MethodInfo mapMethod = enumMapper.GetType().GetMethod("Map", [sourceType])!;
            var pairs = new List<(int, int)>(values.Length);

            foreach (var value in values)
            {
                var mapped = mapMethod.Invoke(enumMapper, [value]);
                pairs.Add((Convert.ToInt32(value), Convert.ToInt32(mapped)));
            }

            return pairs;
        });
    }

    /// <summary>
    /// Returns the mapping pairs for the given type pair, or <c>null</c> if not registered.
    /// </summary>
    internal IReadOnlyList<(int SourceValue, int DestValue)>? GetMappings(Type sourceType, Type destType)
    {
        _mappings.TryGetValue((sourceType, destType), out IReadOnlyList<(int SourceValue, int DestValue)>? result);
        return result;
    }
}
