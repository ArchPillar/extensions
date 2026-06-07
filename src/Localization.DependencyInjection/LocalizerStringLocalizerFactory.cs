using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// Composing <see cref="IStringLocalizerFactory"/> over the ambient store. <see cref="Create(Type)"/> scopes
/// the localizer to the resource type's full name — matching how <see cref="IStringLocalizer{T}"/> call sites
/// are extracted (and how the BCL <c>StringLocalizer&lt;T&gt;</c> calls <c>Create(typeof(T))</c>);
/// <see cref="Create(string, string)"/> uses the global category. Each created localizer composes over the
/// inner factory's localizer, when a factory was registered before this one (typically the
/// ResourceManager/<c>.resx</c> factory from <c>AddLocalization</c>), so existing translations keep resolving
/// on an ambient miss.
/// </summary>
internal sealed class LocalizerStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IStringLocalizerFactory? _inner;

    public LocalizerStringLocalizerFactory(IStringLocalizerFactory? inner)
    {
        _inner = inner;
    }

    public IStringLocalizer Create(Type resourceSource)
    {
        if (resourceSource is null)
        {
            throw new ArgumentNullException(nameof(resourceSource));
        }

        return new LocalizerStringLocalizer(resourceSource.FullName ?? resourceSource.Name, _inner?.Create(resourceSource));
    }

    public IStringLocalizer Create(string baseName, string location) =>
        new LocalizerStringLocalizer(string.Empty, _inner?.Create(baseName, location));
}
