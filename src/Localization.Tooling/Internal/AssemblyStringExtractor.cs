using Mono.Cecil;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Recovers translatable strings from a built assembly (Decision D-K), so extraction covers strings the source
/// generator never sees. It reads each module once with Mono.Cecil (a tool-only dependency; nothing flows to a
/// consumer's product) and runs two independent passes over it: <see cref="CallSiteExtractor"/> for IL call sites
/// and <see cref="AnnotationExtractor"/> for display-annotation strings. Shared across a batch so the resolver and
/// the call-site binding cache are reused for every assembly in one scan.
/// </summary>
internal sealed class AssemblyStringExtractor : IDisposable
{
    private readonly AssemblyModuleReader _reader;
    private readonly CallSiteExtractor _callSites = new();

    /// <summary>
    /// Creates an extractor whose resolver additionally probes <paramref name="referenceDirectories"/> — the
    /// <c>--reference</c> escape hatch, for a build layout where a declaring assembly does not sit beside the
    /// assembly that calls into it (a <c>ProjectReference</c> with <c>Private=false</c>, most often).
    /// </summary>
    public AssemblyStringExtractor(IEnumerable<string>? referenceDirectories = null)
    {
        _reader = new AssemblyModuleReader(referenceDirectories);
    }

    /// <summary>
    /// The translatable sites in <paramref name="assemblyPath"/>: the IL call sites always, and the display
    /// annotations when <paramref name="includeAnnotations"/> is set (a project opts out to emit only IL call
    /// sites). Both passes read one module, parsed once.
    /// </summary>
    public (CallSiteScan CallSites, IReadOnlyList<RawCallSite> Annotations) Extract(string assemblyPath, bool includeAnnotations)
    {
        // A file the reader cannot open is not a managed assembly (a native library beside the app, an
        // unreadable file); it carries no strings and is skipped, never failing the scan around it.
        using ModuleDefinition? module = _reader.Read(assemblyPath);
        if (module is null)
        {
            return (CallSiteScan.Empty, []);
        }

        CallSiteScan callSites = _callSites.Extract(module);
        IReadOnlyList<RawCallSite> annotations = includeAnnotations ? AnnotationExtractor.Extract(module) : [];
        return (callSites, annotations);
    }

    public void Dispose() => _reader.Dispose();
}
