namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// An assembly's extracted template together with how much of that assembly the scan could actually read. The
/// two travel together because every caller that writes a catalog needs both: the entries to merge, and whether
/// the extraction is trustworthy enough to delete anything it does not contain.
/// </summary>
/// <param name="Catalog">The source-language template.</param>
/// <param name="Scan">What the IL scan found, and what it could not classify.</param>
internal sealed record AssemblyTemplate(Catalog Catalog, CallSiteScan Scan)
{
    /// <summary>
    /// Whether the template may be treated as the complete set of this assembly's strings — the precondition for
    /// pruning a catalog down to it. False when any referenced assembly could not be resolved.
    /// </summary>
    public bool IsComplete => Scan.IsComplete;

    /// <summary>A one-line account of what went unread, for the caller to report. Empty when complete.</summary>
    public string Describe() =>
        IsComplete
            ? string.Empty
            : $"{Scan.UnclassifiedCallCount} call site(s) could not be classified: could not resolve "
                + string.Join(", ", Scan.UnresolvedAssemblies);
}
