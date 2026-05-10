using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ArchPillar.Extensions.Identifiers.EntityFrameworkCore.Internal;

internal sealed class IdValueComparer<T>()
    : ValueComparer<Id<T>>(
        static (a, b) => a == b,
        static id => id.GetHashCode(),
        static id => id);
