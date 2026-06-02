using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// Automatically rewrites direct <see cref="Mapper{TSource,TDest}"/> projection
/// calls inside a LINQ query into the mapper's inlined projection expression (via
/// <see cref="MapperCallRewriter"/>), so a hand-written <c>Select</c> can produce
/// a property with a mapper while the whole query is still translated server-side.
/// <para>
/// This interceptor runs <em>after</em> EF Core's parameter extraction, so it
/// cannot inline a mapper that contains an <see cref="Mapper{TSource,TDest}.Invoke(TSource)"/>
/// call — for those, callers use <c>InlineMappers()</c> (which rewrites at query
/// construction, before parameter extraction). Enum mappers are handled separately
/// by <see cref="EnumMapperQueryInterceptor"/>.
/// </para>
/// </summary>
internal sealed class MapperInliningInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        return new MapperCallRewriter(flattenVariableBoxes: true).Visit(queryExpression);
    }
}
