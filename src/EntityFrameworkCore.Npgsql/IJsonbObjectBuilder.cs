namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// Fluent marker for building a PostgreSQL <c>jsonb</c> object inside an EF Core query
/// with compile-time-typed string keys. Obtain one from
/// <see cref="JsonbBuildObjectDbFunctions.JsonbObject(Microsoft.EntityFrameworkCore.DbFunctions, string, object?)"/>,
/// chain <see cref="JsonbBuildObjectDbFunctions.Add(IJsonbObjectBuilder, string, object?)"/>
/// calls, and finish with
/// <see cref="JsonbBuildObjectDbFunctions.Build(IJsonbObjectBuilder)"/>.
/// </summary>
/// <remarks>
/// This type is never instantiated at runtime; the chain is recognised by the query
/// translator and emitted as <c>jsonb_build_object(...)</c>.
/// </remarks>
public interface IJsonbObjectBuilder
{
}
