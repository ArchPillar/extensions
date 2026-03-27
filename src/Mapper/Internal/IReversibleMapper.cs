using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Extended mapper interface for mappers that support reverse mapping
/// (e.g. <see cref="SymmetricEnumMapper{TLeft,TRight}"/>). The
/// <see cref="NestedMapperInliner"/> uses this to inline
/// <c>MapReverse()</c> calls.
/// </summary>
internal interface IReversibleMapper
{
    /// <summary>
    /// Returns the reverse mapping expression with no variable replacement applied.
    /// </summary>
    LambdaExpression GetReverseRawExpression(IncludeSet includes, int depth = 0);
}
