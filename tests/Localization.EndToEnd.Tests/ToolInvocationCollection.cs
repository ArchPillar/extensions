namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// The tool renders through Spectre's process-wide console, which permits only one dynamic display — the status
/// spinner — at a time; two commands running concurrently fail with "Trying to run one or more interactive
/// functions concurrently". Every test class that invokes a command therefore joins this collection, so xUnit runs
/// them one after another instead of in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ToolInvocationCollection
{
    /// <summary>The collection name to put on each command-invoking test class.</summary>
    public const string Name = "ToolInvocation";

    private ToolInvocationCollection()
    {
    }
}
