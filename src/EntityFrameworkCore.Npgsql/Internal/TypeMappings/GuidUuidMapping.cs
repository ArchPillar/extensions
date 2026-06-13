using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.TypeMappings;

/// <summary>
/// EF Core <see cref="GuidTypeMapping"/> for PostgreSQL <c>uuid</c> that changes only
/// the SQL literal form, appending a <c>::uuid</c> cast (<c>'…'::uuid</c>). The cast
/// forces PostgreSQL to type the constant as <c>uuid</c> rather than <c>text</c>, so a
/// uuid constant projected as a read column comes back as <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// Npgsql has no provider-specific Guid mapping; it uses EF Core's base
/// <see cref="GuidTypeMapping"/>. Subclassing it (rather than re-deriving a
/// <see cref="RelationalTypeMapping"/> from scratch) keeps all of the base behaviour and
/// limits the change to the literal. Value formatting is delegated to the base
/// <see cref="RelationalTypeMapping.SqlLiteralFormatString"/> mechanism, so a custom
/// Guid-wrapper struct whose <c>ToString()</c> yields the bare guid still works. Parameters
/// and column reads are untouched.
/// </remarks>
internal sealed class GuidUuidMapping : GuidTypeMapping
{
    public static new GuidUuidMapping Default { get; } = new();

    private GuidUuidMapping()
        : base("uuid", System.Data.DbType.Guid)
    {
    }

    private GuidUuidMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override string SqlLiteralFormatString => "'{0}'::uuid";

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new GuidUuidMapping(parameters);
}
