using System.Globalization;
using Microsoft.Extensions.Localization;
using Ambient = ArchPillar.Extensions.Localization.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// Composing adapter from the ambient store to <see cref="IStringLocalizer"/>. The name is the key; a hit in
/// the ambient store wins; on a miss it falls through to a previously-registered localizer (such as the
/// ResourceManager/<c>.resx</c> one) so existing translations keep resolving; and when neither has it, the
/// name — the in-code default — is returned with <see cref="LocalizedString.ResourceNotFound"/> set. Positional
/// arguments map to <c>{0}</c>, <c>{1}</c>, ... and the lookup is scoped to this adapter's category (the
/// resource type's full name for <see cref="IStringLocalizer{T}"/> via the factory, global otherwise),
/// matching how those call sites are extracted.
/// </summary>
internal sealed class LocalizerStringLocalizer : IStringLocalizer
{
    private readonly string _category;
    private readonly IStringLocalizer? _inner;

    public LocalizerStringLocalizer(string category, IStringLocalizer? inner)
    {
        _category = category;
        _inner = inner;
    }

    public LocalizedString this[string name] => Resolve(name, []);

    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        _inner?.GetAllStrings(includeParentCultures) ?? [];

    private LocalizedString Resolve(string name, object[] arguments)
    {
        var value = Ambient.TranslateInCategory(_category, name, name, ToNamedArguments(arguments), out var overrideFound);
        if (overrideFound)
        {
            return new LocalizedString(name, value, resourceNotFound: false);
        }

        if (_inner is not null)
        {
            LocalizedString fromInner = arguments.Length == 0 ? _inner[name] : _inner[name, arguments];
            if (!fromInner.ResourceNotFound)
            {
                return fromInner;
            }
        }

        return new LocalizedString(name, value, resourceNotFound: true);
    }

    private static (string Name, object? Value)[] ToNamedArguments(object[] arguments)
    {
        var named = new (string Name, object? Value)[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            named[index] = (index.ToString(CultureInfo.InvariantCulture), arguments[index]);
        }

        return named;
    }
}
