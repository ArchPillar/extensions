using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// Non-generic interface implemented by <see cref="EnumMapper{TSource,TDest}"/> so that
/// <see cref="NestedMapperInliner"/> can retrieve the conditional expression tree without
/// knowing the generic type arguments at compile time.
/// </summary>
internal interface IEnumMapper
{
    /// <summary>
    /// Returns the mapping as a conditional expression chain.
    /// Delegates to <see cref="EnumMapper{TSource,TDest}.ToExpression()"/>.
    /// </summary>
    LambdaExpression GetExpression();
}
