namespace ArchPillar.Mapper;

/// <summary>
/// Configuration options for a <see cref="MapperContext"/> instance.
/// Pass an instance (or a configuration delegate) to the
/// <see cref="MapperContext"/> constructor.
/// </summary>
public sealed class MapperContextOptions
{
    /// <summary>
    /// When <see langword="true"/>, all mappers declared in the context are
    /// compiled immediately when the context is constructed rather than on
    /// first use.
    ///
    /// Default: <see langword="false"/> (lazy) — recommended for applications
    /// where startup time is critical.
    ///
    /// Set to <see langword="true"/> when you need predictable first-call
    /// latency or want compilation errors to surface at startup (e.g. in
    /// benchmark projects where cold-start cost must not pollute measurements).
    /// </summary>
    public bool EagerBuild { get; set; } = false;
}
