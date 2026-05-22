using System.Collections.Concurrent;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

internal sealed class EnumMappingStore
{
    private readonly ConcurrentDictionary<(Type Source, Type Dest), IReadOnlyList<(int SourceValue, int DestValue)>> _mappings = new();

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

    internal IReadOnlyList<(int SourceValue, int DestValue)>? GetMappings(Type sourceType, Type destType)
    {
        _mappings.TryGetValue((sourceType, destType), out IReadOnlyList<(int SourceValue, int DestValue)>? result);
        return result;
    }
}
