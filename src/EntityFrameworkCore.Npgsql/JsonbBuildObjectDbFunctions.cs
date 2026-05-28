using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// <c>EF.Functions</c> extensions translated by this package.
/// </summary>
/// <remarks>
/// All methods here are EF Core translation markers: they only run as part of a
/// LINQ-to-Entities query that is translated to SQL. Invoking any of them directly
/// throws <see cref="InvalidOperationException"/>.
/// <para>
/// The fixed-arity overloads pair each key with its value at compile time, which is the
/// ergonomic choice for the common cases. A key/value tuple-array form
/// (<c>params (string, object?)[]</c>) is intentionally not offered: EF Core cannot
/// translate <c>ValueTuple</c> construction inside a query, so it would fail at query
/// time rather than compile time. Use the <c>params object?[]</c> overload for an
/// arbitrary number of pairs.
/// </para>
/// </remarks>
public static class JsonbBuildObjectDbFunctions
{
    private const string MarkerMessage =
        "JsonbBuildObject is an EF Core translation marker and must not be called directly. " +
        "Register the integration with UseArchPillarNpgsqlImprovements() on your DbContext options.";

    /// <summary>
    /// Builds a single-key PostgreSQL <c>jsonb</c> object,
    /// translated to <c>jsonb_build_object(key, value)</c>.
    /// </summary>
    public static string JsonbBuildObject(
        this DbFunctions functions,
        string key,
        object? value)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Builds a two-key PostgreSQL <c>jsonb</c> object,
    /// translated to <c>jsonb_build_object(key1, value1, key2, value2)</c>.
    /// </summary>
    public static string JsonbBuildObject(
        this DbFunctions functions,
        string key1,
        object? value1,
        string key2,
        object? value2)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Builds a three-key PostgreSQL <c>jsonb</c> object,
    /// translated to <c>jsonb_build_object(key1, value1, key2, value2, key3, value3)</c>.
    /// </summary>
    public static string JsonbBuildObject(
        this DbFunctions functions,
        string key1,
        object? value1,
        string key2,
        object? value2,
        string key3,
        object? value3)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Builds a PostgreSQL <c>jsonb</c> object from an arbitrary number of alternating
    /// key/value pairs, translated to <c>jsonb_build_object(...)</c>. The arguments must
    /// alternate <c>key, value, key, value, …</c> with an even number of total elements.
    /// </summary>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="keysAndValues">Alternating key/value arguments; total count must be even.</param>
    public static string JsonbBuildObject(
        this DbFunctions functions,
        params object?[] keysAndValues)
        => throw new InvalidOperationException(MarkerMessage);
}
