using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// An <see cref="IStringLocalizerFactory"/> over <see cref="Localizer"/>. Resource source and base name
/// are ignored because the family uses a single global symbolic-key namespace rather than per-resource
/// files.
/// </summary>
internal sealed class LocalizerStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly Localizer _localizer;

    public LocalizerStringLocalizerFactory(Localizer localizer)
    {
        _localizer = localizer;
    }

    public IStringLocalizer Create(Type resourceSource) => new LocalizerStringLocalizer(_localizer);

    public IStringLocalizer Create(string baseName, string location) => new LocalizerStringLocalizer(_localizer);
}
