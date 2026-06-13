using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// <c>EF.Functions</c> extensions for building PostgreSQL <c>jsonb</c> values.
/// </summary>
public static class JsonbDbFunctions
{
    private const string MarkerMessage =
        "This method is an EF Core translation marker and must not be called directly. " +
        "Register the integration with UseArchPillarNpgsqlImprovements() on your DbContext options.";

    /// <summary>
    /// Builds a PostgreSQL <c>jsonb</c> object from an object shape, translated to
    /// <c>jsonb_build_object(...)</c>. The keys are the member names of the shape and the
    /// values are the member values:
    /// <code>
    /// ctx.Tickets.Select(t => EF.Functions.ToJsonb(new
    /// {
    ///     id       = t.Id,
    ///     title    = t.Title,
    ///     severity = (int)t.Severity,
    /// }))
    /// // -> jsonb_build_object('id', t."Id", 'title', t."Title", 'severity', t."Severity")
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <paramref name="shape"/> must be an inline object initializer — an anonymous type
    /// (<c>new { a = …, b = … }</c>) or a named type with an object initializer
    /// (<c>new MyDto { A = …, B = … }</c>). Member names are read from the expression tree at
    /// query-compilation time and become SQL string literals; the shape object is never
    /// instantiated and no reflection runs per row. Positional constructors / records and
    /// pre-built instances are not supported (they carry no per-member expressions).
    /// </para>
    /// <para>
    /// This method is an EF Core translation marker: it only runs as part of a translated
    /// query. Invoking it directly throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TShape">The anonymous or named shape type.</typeparam>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="shape">An inline object initializer describing the jsonb object.</param>
    public static string ToJsonb<TShape>(this DbFunctions functions, TShape shape)
        => throw new InvalidOperationException(MarkerMessage);

    /// <summary>
    /// Internal flat marker that <c>ToJsonb</c> is desugared into by the query-compilation
    /// interceptor: alternating key/value elements, recognised by the method-call translator
    /// and emitted as <c>jsonb_build_object(...)</c>. Not part of the public API surface.
    /// </summary>
    internal static string JsonbBuildObjectCore(object?[] keysAndValues)
        => throw new InvalidOperationException(MarkerMessage);
}
