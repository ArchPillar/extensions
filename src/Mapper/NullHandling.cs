namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Controls what in-memory mapping does when a nested source is <see langword="null"/>.
/// LINQ projection is unaffected: a database never yields a <see langword="null"/>
/// collection navigation, and a nested reference that is <see langword="null"/>
/// always projects to <see langword="null"/>.
/// </summary>
public enum NullHandling
{
    /// <summary>
    /// Behaves like EF Core. A <see langword="null"/> nested object maps to
    /// <see langword="null"/>. A <see langword="null"/> source collection maps to an
    /// empty collection when the destination property is declared non-nullable, and to
    /// <see langword="null"/> when it is declared nullable. This is the default.
    /// </summary>
    Lenient,

    /// <summary>
    /// Treats nullable reference type annotations as a contract. A <see langword="null"/>
    /// nested source that is declared non-nullable, or a <see langword="null"/> source
    /// collection mapped into a destination declared non-nullable, throws an
    /// <see cref="InvalidOperationException"/> naming the mapper and the member.
    /// A <see langword="null"/> where both sides allow it still maps to <see langword="null"/>.
    /// </summary>
    Strict,
}
