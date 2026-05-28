namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Small wrapper that holds a resolved variable value. Used by
/// <see cref="VariableReplacer"/> to emit a property-access expression
/// (<c>box.Value</c>) instead of a raw <see cref="System.Linq.Expressions.ConstantExpression"/>.
/// <para>
/// EF Core's query translator treats property access on a captured constant
/// (the classic closure pattern) as a SQL parameter, but inlines a bare
/// <see cref="System.Linq.Expressions.ConstantExpression"/> as a SQL literal.
/// Routing variable values through this box ensures they reach the database
/// as parameters — preserving query-plan reuse and avoiding any risk of the
/// value being injected into the SQL text itself.
/// </para>
/// </summary>
internal sealed class VariableValueBox<T>(T? value)
{
    /// <summary>
    /// The resolved variable value. Read-only so EF Core can safely cache
    /// query plans keyed on the surrounding expression.
    /// </summary>
    public T? Value { get; } = value;
}
