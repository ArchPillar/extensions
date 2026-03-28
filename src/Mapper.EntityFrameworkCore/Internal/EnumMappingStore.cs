using System.Collections.Concurrent;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

internal sealed class EnumMappingStore
{
    private readonly ConcurrentDictionary<(Type Source, Type Dest), IReadOnlyList<(int SourceValue, int DestValue)>> _mappings = new();

    internal void RegisterFromContext(MapperContext context)
    {
        PropertyInfo[] properties = context.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo property in properties)
        {
            Type propertyType = property.PropertyType;

            if (!propertyType.IsGenericType)
            {
                continue;
            }

            Type genericDef = propertyType.GetGenericTypeDefinition();

            if (genericDef == typeof(EnumMapper<,>))
            {
                Type[] typeArgs = propertyType.GetGenericArguments();
                var mapper = property.GetValue(context)!;
                RegisterDynamic(mapper, typeArgs[0], typeArgs[1]);
            }
            else if (genericDef == typeof(SymmetricEnumMapper<,>))
            {
                Type[] typeArgs = propertyType.GetGenericArguments();
                var mapper = property.GetValue(context)!;

                // Register forward direction via the Forward inner mapper.
                PropertyInfo forwardProp = propertyType.GetProperty("Forward")!;
                var forwardMapper = forwardProp.GetValue(mapper)!;
                RegisterDynamic(forwardMapper, typeArgs[0], typeArgs[1]);

                // Register reverse direction via the Reverse inner mapper.
                PropertyInfo reverseProp = propertyType.GetProperty("Reverse")!;
                var reverseMapper = reverseProp.GetValue(mapper)!;
                RegisterDynamic(reverseMapper, typeArgs[1], typeArgs[0]);
            }
        }
    }

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
