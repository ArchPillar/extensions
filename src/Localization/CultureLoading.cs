namespace ArchPillar.Extensions.Localization;

/// <summary>
/// How catalog files are loaded across cultures.
/// </summary>
public enum CultureLoading
{
    /// <summary>
    /// Read every culture's files up front, into one merged snapshot. The right choice for a server that
    /// serves many cultures concurrently and cannot predict which a given request needs.
    /// </summary>
    Eager,

    /// <summary>
    /// Load a culture's files only the first time that culture is requested. A single-user client (CLI,
    /// desktop, Blazor) loads just the active language and pulls another in — without a restart — only if the
    /// user switches to it. The first lookup in a not-yet-loaded culture pays a one-time read of that
    /// culture's (small) files; every lookup after is the same lock-free snapshot read as eager loading.
    /// </summary>
    OnDemand
}
