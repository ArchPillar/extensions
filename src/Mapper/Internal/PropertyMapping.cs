using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Mapper.Internal;

internal enum MappingKind { Required, Optional, Ignored }

/// <summary>
/// Represents a single property binding produced by the builder and consumed by
/// <see cref="Mapper{TSource,TDest}"/>.
/// </summary>
/// <param name="Destination">The destination property.</param>
/// <param name="Source">
/// The source expression, or <c>null</c> for <see cref="MappingKind.Ignored"/>
/// entries. When the mapping contains nested mapper calls, this stores the
/// raw (un-inlined) expression at build time; the inlined version is computed
/// lazily by <see cref="Mapper{TSource,TDest}"/> on first use.
/// </param>
/// <param name="Kind">Whether this mapping is required, optional, or ignored.</param>
/// <param name="NestedMapperAccessor">
/// A deferred accessor for the nested <see cref="Mapper{TSource,TDest}"/> instance
/// backing this mapping, set when the source expression is a <c>Project(mapper)</c>
/// or <c>mapper.Map(src.X)</c> call. The accessor is evaluated lazily (on first use)
/// so that nested mappers do not need to exist at build time.
/// Used by <c>BuildExpression</c> to cascade optional includes into the nested mapper.
/// </param>
/// <param name="NestedSourceAccess">
/// A lambda that produces the source value passed to the nested mapper (e.g.
/// <c>src =&gt; src.Lines</c> or <c>src =&gt; src.Customer</c>). Shares the same
/// parameter as <see cref="Source"/> (replaced by <c>BuildExpression</c> at call time).
/// </param>
/// <param name="IsCollection">
/// <see langword="true"/> when the nested mapper is applied as a collection projection
/// (<c>Project(mapper)</c>); <see langword="false"/> for a scalar <c>mapper.Map(src.X)</c>.
/// </param>
internal sealed record PropertyMapping(
    MemberInfo        Destination,
    LambdaExpression? Source,
    MappingKind       Kind,
    Func<IMapper>?    NestedMapperAccessor = null,
    LambdaExpression? NestedSourceAccess   = null,
    bool              IsCollection         = false);
