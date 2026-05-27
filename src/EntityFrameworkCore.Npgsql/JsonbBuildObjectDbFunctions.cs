using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// <c>EF.Functions</c> extensions translated by this package.
/// </summary>
public static class JsonbBuildObjectDbFunctions
{
    /// <summary>
    /// Builds a PostgreSQL <c>jsonb</c> object from alternating key/value pairs,
    /// translated to <c>jsonb_build_object(...)</c>. The arguments must alternate
    /// <c>key, value, key, value, …</c> with an even number of total elements.
    /// </summary>
    /// <remarks>
    /// This method must be called inside a LINQ-to-Entities query and translated
    /// to SQL. Invoking it directly throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="keysAndValues">Alternating key/value arguments; total count must be even.</param>
    /// <exception cref="InvalidOperationException">
    /// Always thrown when invoked outside an EF Core query translator.
    /// </exception>
    public static string JsonbBuildObject(
        this DbFunctions functions,
        params object?[] keysAndValues)
    {
        _ = functions;
        _ = keysAndValues;
        throw new InvalidOperationException(
            $"{nameof(JsonbBuildObject)} is an EF Core translation marker and must not be called directly. " +
            "Register the integration with UseArchPillarNpgsqlImprovements() on your DbContext options.");
    }
}
