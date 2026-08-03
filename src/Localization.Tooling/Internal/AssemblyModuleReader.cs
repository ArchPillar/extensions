using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Reads assembly modules for a batch scan, owning the one Cecil resolver and its probe directories so the shared
/// dependency assemblies (ArchPillar.*, the framework) are loaded once for the whole run rather than once per
/// assembly. Both extraction passes read the same module through here, so an assembly's IL metadata is parsed once.
/// </summary>
internal sealed class AssemblyModuleReader : IDisposable
{
    private readonly DefaultAssemblyResolver _resolver = new();
    private readonly HashSet<string> _searchDirectories = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyModuleReader()
    {
        // The tool's own base directory carries the ArchPillar reference assemblies when running in-process.
        AddSearchDirectory(AppContext.BaseDirectory);
    }

    /// <summary>
    /// Reads the module at <paramref name="assemblyPath"/>. The assembly's own directory is added as a probe path
    /// first, since resolving a call target to its definition (to read its parameter attributes) needs the
    /// referenced ArchPillar assemblies that sit beside it in a real build output.
    /// <para>
    /// Symbols are read when a PDB is present, so a call site can be attributed to the source file it was written
    /// in (<see cref="CatalogEntry.References"/>). A missing or stripped PDB is not an error — the read succeeds
    /// with <c>HasSymbols</c> false and the scan simply recovers no file references.
    /// </para>
    /// </summary>
    public ModuleDefinition? Read(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        AddSearchDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            return ModuleDefinition.ReadModule(fullPath, new ReaderParameters
            {
                AssemblyResolver = _resolver,
                ReadSymbols = true,
                SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false)
            });
        }
        catch (BadImageFormatException)
        {
            // Not a managed assembly — a native library, a resource-only or otherwise unreadable file. A scan
            // walks whatever is on disk, so this is a file that is simply not a candidate, not a failure.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every type in the module, including nested types, depth-first.</summary>
    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => Descend(module.Types);

    private static IEnumerable<TypeDefinition> Descend(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in Descend(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    // Adds a probe directory to the shared resolver once, so repeated scans over the same output tree do not pile
    // up duplicate search paths.
    private void AddSearchDirectory(string directory)
    {
        if (_searchDirectories.Add(directory))
        {
            _resolver.AddSearchDirectory(directory);
        }
    }

    public void Dispose() => _resolver.Dispose();
}
