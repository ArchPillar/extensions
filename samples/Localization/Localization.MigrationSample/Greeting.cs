namespace MigrationSample;

/// <summary>
/// The resource scope. <c>IStringLocalizer&lt;Greeting&gt;</c> resolves the legacy
/// <c>Resources/Greeting[.de].resx</c>, and ArchPillar's ambient entries for these strings live under this
/// type's full name (the category) — the same key both the existing ResourceManager and the new store use.
/// </summary>
public sealed class Greeting;
