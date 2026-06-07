using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// An <see cref="IStringLocalizerFactory"/> over the ambient localizer. Resource source and base name are
/// ignored because the family uses a single global symbolic-key namespace rather than per-resource files.
/// </summary>
internal sealed class LocalizerStringLocalizerFactory : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => new LocalizerStringLocalizer();

    public IStringLocalizer Create(string baseName, string location) => new LocalizerStringLocalizer();
}
