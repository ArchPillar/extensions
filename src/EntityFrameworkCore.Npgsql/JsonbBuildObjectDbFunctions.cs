using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// <c>EF.Functions</c> extensions translated by this package, plus the fluent
/// <see cref="IJsonbObjectBuilder"/> chain for building a <c>jsonb</c> object with
/// compile-time-typed string keys.
/// </summary>
/// <remarks>
/// All methods here are EF Core translation markers: they only run as part of a
/// LINQ-to-Entities query that is translated to SQL. Invoking any of them directly
/// throws <see cref="InvalidOperationException"/>.
/// </remarks>
public static class JsonbBuildObjectDbFunctions
{
    private const string MarkerMessage =
        "This method is an EF Core translation marker and must not be called directly. " +
        "Register the integration with UseArchPillarNpgsqlImprovements() on your DbContext options.";

    /// <summary>
    /// Starts a fluent <c>jsonb</c> object with its first key/value pair. Chain
    /// <see cref="Add(IJsonbObjectBuilder, string, object?)"/> for further pairs and finish
    /// with <see cref="Build(IJsonbObjectBuilder)"/>. Keys are typed as <see cref="string"/>,
    /// so a value can never be passed where a key is expected:
    /// <code>
    /// EF.Functions.JsonbObject("name", r.Name)
    ///     .Add("priority", (int)r.Priority)
    ///     .Build()
    /// </code>
    /// The first pair is part of the seed so the call references query data (an empty,
    /// constant seed would be evaluated client-side by EF before translation).
    /// </summary>
    public static IJsonbObjectBuilder JsonbObject(this DbFunctions functions, string key, object? value)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Adds a key/value pair to the <c>jsonb</c> object under construction.
    /// </summary>
    public static IJsonbObjectBuilder Add(this IJsonbObjectBuilder builder, string key, object? value)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Completes the fluent chain, producing the <c>jsonb</c> object as a string.
    /// </summary>
    public static string Build(this IJsonbObjectBuilder builder)
        => throw new InvalidOperationException(MarkerMessage);

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
}
