using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.AspNetCore;

/// <summary>
/// An <see cref="IStringLocalizer"/> that resolves DataAnnotations strings through an ArchPillar
/// <see cref="ILocalizer"/> scoped to the model type's category. The MVC framework looks a display name or error
/// message up by its literal; this looks the literal up as the key — the text-as-key the extractor wrote — unless
/// a <c>[Localized…]</c> twin on the member supplies a stable key for that literal, which it bridges to.
/// Positional arguments map to <c>{0}</c>, <c>{1}</c>, … so validation messages render through the ICU pipeline.
/// </summary>
internal sealed class ArchPillarDataAnnotationsLocalizer : IStringLocalizer
{
    private readonly ILocalizer _localizer;
    private readonly IReadOnlyDictionary<string, (string Key, string Default)> _twinsByLiteral;

    public ArchPillarDataAnnotationsLocalizer(ILocalizer localizer, Type modelType)
    {
        _localizer = localizer;
        _twinsByLiteral = BuildTwinMap(modelType);
    }

    public LocalizedString this[string name] => Resolve(name, []);

    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

    private LocalizedString Resolve(string name, object[] arguments)
    {
        (var key, var defaultMessage) = _twinsByLiteral.TryGetValue(name, out (string Key, string Default) twin)
            ? twin
            : (name, name);
        var value = _localizer.Translate(key, defaultMessage, Named(arguments));
        return new LocalizedString(name, value, resourceNotFound: false);
    }

    // Positional arguments become the named arguments "0", "1", … so a {0}-style validation message resolves
    // through the ICU renderer, matching the IStringLocalizer indexer's string.Format positions.
    private static (string Name, object? Value)[] Named(object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return [];
        }

        var named = new (string Name, object? Value)[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            named[index] = (index.ToString(CultureInfo.InvariantCulture), arguments[index]);
        }

        return named;
    }

    // Maps each member's system display/description literal to the stable (key, default) its [Localized…] twin
    // carries, so the framework's literal lookup bridges to the twin's key. Only a member carrying both a twin and
    // a system literal contributes; everything else falls through to literal-as-key.
    private static IReadOnlyDictionary<string, (string Key, string Default)> BuildTwinMap(Type modelType)
    {
        var map = new Dictionary<string, (string Key, string Default)>(StringComparer.Ordinal);
        AddTwins(modelType, map);
        foreach (PropertyInfo property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AddTwins(property, map);
        }

        return map;
    }

    private static void AddTwins(MemberInfo member, Dictionary<string, (string Key, string Default)> map)
    {
        LocalizedDisplayNameAttribute? displayTwin = member.GetCustomAttribute<LocalizedDisplayNameAttribute>();
        if (displayTwin is not null && DisplayLiteral(member) is { } displayLiteral)
        {
            map[displayLiteral] = (displayTwin.Key, displayTwin.Default);
        }

        LocalizedDescriptionAttribute? descriptionTwin = member.GetCustomAttribute<LocalizedDescriptionAttribute>();
        if (descriptionTwin is not null && DescriptionLiteral(member) is { } descriptionLiteral)
        {
            map[descriptionLiteral] = (descriptionTwin.Key, descriptionTwin.Default);
        }
    }

    // The display-name literal the framework looks the member up by: [DisplayName] or [Display(Name)] (GetName, so
    // it matches the framework's own resolution). Null when neither carries a non-empty value.
    private static string? DisplayLiteral(MemberInfo member)
    {
        if (member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName is { Length: > 0 } displayName)
        {
            return displayName;
        }

        return member.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } name ? name : null;
    }

    private static string? DescriptionLiteral(MemberInfo member)
    {
        if (member.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } description)
        {
            return description;
        }

        return member.GetCustomAttribute<DisplayAttribute>()?.GetDescription() is { Length: > 0 } displayDescription
            ? displayDescription
            : null;
    }
}
