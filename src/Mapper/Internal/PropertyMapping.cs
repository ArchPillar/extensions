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
/// The source expression (already inlined), or <c>null</c> for
/// <see cref="MappingKind.Ignored"/> entries.
/// </param>
/// <param name="Kind">Whether this mapping is required, optional, or ignored.</param>
internal sealed record PropertyMapping(
    MemberInfo        Destination,
    LambdaExpression? Source,
    MappingKind       Kind);
