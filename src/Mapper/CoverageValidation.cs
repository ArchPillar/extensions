namespace ArchPillar.Mapper;

/// <summary>
/// Controls how strictly the mapper builder validates that every destination
/// property is accounted for during <see cref="MapperBuilder{TSource,TDest}.Build"/>.
/// </summary>
public enum CoverageValidation
{
    /// <summary>
    /// Only non-nullable properties must be explicitly covered by
    /// <c>Map</c>, <c>Optional</c>, or <c>Ignore</c>. Nullable reference-type
    /// and nullable value-type properties are auto-ignored and default to
    /// <see langword="null"/> when left unmapped. This is the default.
    /// </summary>
    NonNullableProperties,

    /// <summary>
    /// Every writable destination property must be explicitly covered,
    /// regardless of nullability. Use this mode when silent <see langword="null"/>
    /// defaults for unmapped navigation properties are undesirable.
    /// </summary>
    AllProperties,

    /// <summary>
    /// Skip coverage validation entirely. No error is raised for unmapped
    /// properties. Use with caution — unmapped non-nullable value-type
    /// properties will silently receive <c>default</c> values.
    /// </summary>
    None,
}
