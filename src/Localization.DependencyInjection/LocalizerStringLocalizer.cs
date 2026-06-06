using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// Adapts <see cref="Localizer"/> to <see cref="IStringLocalizer"/>: the name is the key, a missing
/// entry returns the name with <see cref="LocalizedString.ResourceNotFound"/> set, and positional
/// arguments map to <c>{0}</c>, <c>{1}</c>, ... ICU placeholders. The culture is the current UI culture.
/// </summary>
internal class LocalizerStringLocalizer : IStringLocalizer
{
    private readonly Localizer _localizer;

    public LocalizerStringLocalizer(Localizer localizer)
    {
        _localizer = localizer;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = _localizer.Translate(CultureInfo.CurrentUICulture, name, name, context: null, out var found);
            return new LocalizedString(name, value, resourceNotFound: !found);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = _localizer.Translate(
                CultureInfo.CurrentUICulture, name, name, context: null, out var found, ToNamedArguments(arguments));
            return new LocalizedString(name, value, resourceNotFound: !found);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

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

/// <summary>The generic <see cref="IStringLocalizer{T}"/> form; the resource type is ignored because keys are a single global namespace.</summary>
/// <typeparam name="T">The resource type (unused).</typeparam>
internal sealed class LocalizerStringLocalizer<T> : LocalizerStringLocalizer, IStringLocalizer<T>
{
    public LocalizerStringLocalizer(Localizer localizer)
        : base(localizer)
    {
    }
}
