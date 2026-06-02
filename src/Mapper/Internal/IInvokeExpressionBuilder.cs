using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Implemented by <see cref="Mapper{TSource,TDest}"/> so that
/// <see cref="NestedMapperInliner"/> can rewrite an <c>Invoke</c> call into a
/// delegate invocation without reflecting over the closed generic mapper type.
/// The mapper builds the expression from its own compile-time type knowledge.
/// </summary>
internal interface IInvokeExpressionBuilder
{
    /// <summary>
    /// Builds an expression that invokes this mapper's cached delegate on
    /// <paramref name="source"/>, via a non-mapper <see cref="MapperInvokeBox{TSource,TResult}"/>
    /// so a LINQ provider can parameterize it and evaluate the mapping in memory.
    /// </summary>
    public Expression BuildInvokeExpression(Expression source);
}
