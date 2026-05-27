using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.TypeMappings;

/// <summary>
/// Relational type mapping for <see cref="Guid"/> that overrides
/// <see cref="RelationalTypeMapping.GenerateNonNullSqlLiteral(object)"/> to emit
/// <c>'…'::uuid</c> instead of a bare <c>'…'</c> string literal. The cast forces
/// PostgreSQL to type the constant as <c>uuid</c> rather than <c>text</c>, which
/// makes uuid constants safe to project as a read column.
/// </summary>
internal sealed class GuidUuidMapping : RelationalTypeMapping
{
    public static GuidUuidMapping Default { get; } = new();

    private GuidUuidMapping()
        : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(Guid)),
                storeType: "uuid",
                storeTypePostfix: StoreTypePostfix.None,
                dbType: System.Data.DbType.Guid))
    {
    }

    private GuidUuidMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new GuidUuidMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var guid = (Guid)value;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"'{guid}'::uuid");
    }

    protected override void ConfigureParameter(System.Data.Common.DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        if (parameter is global::Npgsql.NpgsqlParameter npg)
        {
            npg.NpgsqlDbType = NpgsqlDbType.Uuid;
        }
    }
}
