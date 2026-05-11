using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Internal;

internal sealed class IdValueConverter<T>()
    : ValueConverter<Id<T>, Guid>(
        static id => id.Value,
        static guid => new Id<T>(guid));
