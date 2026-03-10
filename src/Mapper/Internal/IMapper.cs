using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// Non-generic interface implemented by <see cref="Mapper{TSource,TDest}"/> so that
/// <see cref="NestedMapperInliner"/> can retrieve the base expression without knowing
/// the generic type arguments at compile time.
/// </summary>
internal interface IMapper
{
    /// <summary>
    /// Returns the mapper's required-only expression with all variables resolved to
    /// their default values. Used when inlining a nested mapper into a parent expression.
    /// </summary>
    LambdaExpression GetBaseExpression();
}
