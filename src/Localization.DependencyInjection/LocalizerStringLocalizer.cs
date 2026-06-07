using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.DependencyInjection;

/// <summary>
/// Adapts the ambient localizer to <see cref="IStringLocalizer"/>: the name is the key, a missing entry
/// returns the name (the in-code default), and positional arguments map to <c>{0}</c>, <c>{1}</c>, ... The
/// culture is the current UI culture and the global namespace is used (the resource type of the generic
/// form is ignored). Reads <see cref="Localization"/> so DI and the ambient store share one source.
/// </summary>
internal class LocalizerStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] =>
        new(name, Localization.Default.Translate(name, name));

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, Localization.Default.Translate(name, name, ToNamedArguments(arguments)));

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
internal sealed class LocalizerStringLocalizer<T> : LocalizerStringLocalizer, IStringLocalizer<T>;
