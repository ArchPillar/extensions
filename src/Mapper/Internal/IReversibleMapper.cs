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
    /// The <paramref name="path"/> parameter is propagated from the parent build
    /// so that path-targeted transformers run only on the matching compilation path.
    /// </summary>
    public LambdaExpression GetReverseRawExpression(IncludeSet includes, TransformTarget path, int depth = 0);
}
