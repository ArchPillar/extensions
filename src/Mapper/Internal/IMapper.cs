using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// Non-generic interface implemented by <see cref="Mapper{TSource,TDest}"/>
/// and <see cref="EnumMapper{TSource,TDest}"/> so that parent mappers can
/// retrieve the nested expression without knowing the generic type arguments.
/// </summary>
internal interface IMapper
{
    /// <summary>
    /// Returns a mapping expression with the specified optional includes and
    /// variable bindings applied. <paramref name="includes"/> is a recursive
    /// tree: top-level names select optional properties at this level; nested
    /// entries cascade into child mappers. Variable bindings are propagated so
    /// that shared <see cref="Variable{T}"/> instances resolve consistently
    /// across the entire mapper hierarchy.
    /// </summary>
    LambdaExpression GetExpression(
        IncludeSet                           includes,
        IReadOnlyDictionary<object, object?> variableBindings,
        bool                                 nullSafeOptionals);
}
