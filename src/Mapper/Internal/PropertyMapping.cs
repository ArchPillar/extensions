using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

internal enum MappingKind { Required, Optional, Ignored }

/// <summary>
/// Represents a single property binding produced by the builder and consumed by
/// <see cref="Mapper{TSource,TDest}"/>.
/// </summary>
/// <param name="Destination">The destination property.</param>
/// <param name="Source">
/// The source expression, or <c>null</c> for <see cref="MappingKind.Ignored"/>
/// entries. Nested mapper calls (<c>mapper.Map()</c>, <c>.Project()</c>) are stored
/// raw and inlined at expression-build time by <see cref="NestedMapperInliner"/>.
/// </param>
/// <param name="Kind">Whether this mapping is required, optional, or ignored.</param>
internal sealed record PropertyMapping(
    MemberInfo        Destination,
    LambdaExpression? Source,
    MappingKind       Kind);
