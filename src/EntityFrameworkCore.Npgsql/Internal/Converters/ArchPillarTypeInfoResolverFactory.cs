using System.Collections.Concurrent;
using Npgsql.Internal;
using Npgsql.Internal.Postgres;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

/// <summary>
/// Resolver factory installed via <see cref="global::Npgsql.NpgsqlDataSourceBuilder.AddTypeInfoResolverFactory(PgTypeInfoResolverFactory)"/>
/// that registers the ArchPillar wire converters: <see cref="DateTime"/> and
/// <see cref="DateTimeOffset"/> ↔ <c>timestamptz</c> (UTC-enforcing), and any
/// CLR enum ↔ <c>int4</c>.
/// </summary>
internal sealed class ArchPillarTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();

    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    private static TypeInfoMappingCollection BuildBaseMappings()
    {
        var mappings = new TypeInfoMappingCollection();

        mappings.AddStructType<DateTimeOffset>(
            "timestamp with time zone",
            BuildDateTimeOffsetInfo,
            MatchRequirement.DataTypeName);

        mappings.AddStructType<DateTime>(
            "timestamp with time zone",
            BuildDateTimeInfo,
            MatchRequirement.DataTypeName);

        return mappings;
    }

    private static PgTypeInfo BuildDateTimeOffsetInfo(PgSerializerOptions options, TypeInfoMapping mapping, bool requiresDataTypeName)
    {
        _ = requiresDataTypeName;
        return mapping.CreateInfo(options, new DateTimeOffsetTimestampTzConverter());
    }

    private static PgTypeInfo BuildDateTimeInfo(PgSerializerOptions options, TypeInfoMapping mapping, bool requiresDataTypeName)
    {
        _ = requiresDataTypeName;
        return mapping.CreateInfo(options, new DateTimeTimestampTzConverter());
    }

    private sealed class Resolver : IPgTypeInfoResolver
    {
        private static readonly TypeInfoMappingCollection _baseMappings = BuildBaseMappings();
        private readonly ConcurrentDictionary<Type, TypeInfoMappingCollection> _enumMappings = new();

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
        {
            if (type is null)
            {
                return null;
            }

            PgTypeInfo? info = _baseMappings.Find(type, dataTypeName, options);
            if (info is not null)
            {
                return info;
            }

            if (type.IsEnum)
            {
                TypeInfoMappingCollection enumMappings = _enumMappings.GetOrAdd(type, BuildEnumMappings);
                return enumMappings.Find(type, dataTypeName, options);
            }

            return null;
        }

        private static TypeInfoMappingCollection BuildEnumMappings(Type enumType)
        {
            var mappings = new TypeInfoMappingCollection();
            Type converterType = typeof(EnumInt4Converter<>).MakeGenericType(enumType);

            var mapping = new TypeInfoMapping(enumType, "integer", Factory)
            {
                MatchRequirement = MatchRequirement.DataTypeName,
            };
            mappings.Add(mapping);
            return mappings;

            PgTypeInfo Factory(PgSerializerOptions options, TypeInfoMapping m, bool requiresDataTypeName)
            {
                _ = requiresDataTypeName;
                var converter = (PgConverter)Activator.CreateInstance(converterType)!;
                return TypeInfoMappingHelpers.CreateInfo(m, options, converter);
            }
        }
    }

    private sealed class ArrayResolver : IPgTypeInfoResolver
    {
        private static readonly TypeInfoMappingCollection _baseMappings = BuildArrayMappings();

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
        {
            if (type is null)
            {
                return null;
            }

            return _baseMappings.Find(type, dataTypeName, options);
        }

        private static TypeInfoMappingCollection BuildArrayMappings()
        {
            TypeInfoMappingCollection arrays = new(BuildBaseMappings());
            arrays.AddStructArrayType<DateTimeOffset>("timestamp with time zone");
            arrays.AddStructArrayType<DateTime>("timestamp with time zone");
            return arrays;
        }
    }
}
