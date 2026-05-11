using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Non-generic interface implemented by <see cref="Mapper{TSource,TDest}"/>
/// and <see cref="EnumMapper{TSource,TDest}"/> so that parent mappers can
/// retrieve the nested expression without knowing the generic type arguments.
/// </summary>
internal interface IMapper
{
    /// <summary>
    /// Returns the raw mapping expression with no variable replacement applied.
    /// Variable nodes remain as <c>Convert(Variable&lt;T&gt;)</c> in the tree so
    /// that a caller can substitute them using <see cref="VariableReplacer"/> or
    /// <see cref="VariableDictReplacer"/> in a single post-build pass.
    /// <para>
    /// The <paramref name="depth"/> parameter tracks the current nesting level
    /// during recursive inlining. If it exceeds
    /// <see cref="NestedMapperInliner.MaxNestingDepth"/>, the inliner throws
    /// <see cref="InvalidOperationException"/> to prevent infinite recursion
    /// from circular mapper references.
    /// </para>
    /// </summary>
    public LambdaExpression GetRawExpression(IncludeSet includes, int depth = 0);

    /// <summary>
    /// Forces expression assembly and delegate compilation with default
    /// (empty) includes and no variable bindings. Called by
    /// <see cref="MapperContext.EagerBuildAll"/> to surface mapping errors
    /// at startup and eliminate cold-start latency.
    /// </summary>
    public void Compile();
}
