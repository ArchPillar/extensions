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

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        // The ambient overrides for this category come first (they win on a key collision), then any of the
        // inner factory's strings not already covered — so the listing reflects what a lookup would resolve.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in Ambient.EnumerateOverrides(_category, includeParentCultures))
        {
            seen.Add(pair.Key);
            yield return new LocalizedString(pair.Key, pair.Value, resourceNotFound: false);
        }

        if (_inner is null)
        {
            yield break;
        }

        foreach (LocalizedString localized in _inner.GetAllStrings(includeParentCultures))
        {
            if (seen.Add(localized.Name))
            {
                yield return localized;
            }
        }
    }

    private LocalizedString Resolve(string name, object[] arguments)
    {
        // A loaded override is a real (ICU) translation, so format it; on a miss, do NOT run the name through
        // the ICU formatter — the name is a ResourceManager-style key that may contain composite-format text
        // like "{0:C}". Fall through to the inner factory, then return the name verbatim / string.Format'd.
        var overrideValue = Ambient.TranslateOverride(_category, name, ToNamedArguments(arguments));
        if (overrideValue is not null)
        {
            return new LocalizedString(name, overrideValue, resourceNotFound: false);
        }

        if (_inner is not null)
        {
            LocalizedString fromInner = arguments.Length == 0 ? _inner[name] : _inner[name, arguments];
            if (!fromInner.ResourceNotFound)
            {
                return fromInner;
            }
        }

        return new LocalizedString(name, Format(name, arguments), resourceNotFound: true);
    }

    // The IStringLocalizer not-found convention: the name with its arguments applied via string.Format (not
    // ICU), and the name verbatim when that fails — matching ResourceManagerStringLocalizer.
    private static string Format(string name, object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return name;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, name, arguments);
        }
        catch (FormatException)
        {
            return name;
        }
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
