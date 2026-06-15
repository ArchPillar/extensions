using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.AspNetCore;

/// <summary>
/// An <see cref="IStringLocalizer"/> that resolves DataAnnotations strings through an ArchPillar
/// <see cref="ILocalizer"/> scoped to the model type's category. The MVC framework looks a display name or error
/// message up by the system attribute's value — the text itself (text-as-key) or a string id — so that value is
/// the key directly, with no remapping. The only thing the framework does not hand over is the source default for
/// a string id; this reads it from the member's <c>[Localized…]</c> twin (or a validator's <c>ErrorMessage</c>
/// key from <c>[LocalizedMessage&lt;T&gt;]</c>) so a string id still renders its source text when no translation
/// is loaded. Positional arguments map to <c>{0}</c>, <c>{1}</c>, … so validation messages render through ICU.
/// </summary>
internal sealed class ArchPillarDataAnnotationsLocalizer : IStringLocalizer
{
    private readonly ILocalizer _localizer;
    private readonly IReadOnlyDictionary<string, string> _defaultsByKey;

    public ArchPillarDataAnnotationsLocalizer(ILocalizer localizer, Type modelType)
    {
        _localizer = localizer;
        _defaultsByKey = BuildDefaults(modelType);
    }

    public LocalizedString this[string name] => Resolve(name, []);

    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

    private LocalizedString Resolve(string name, object[] arguments)
    {
        // The lookup name is the key. A twin gives that key a source default; without one the key is its own
        // default (the text-as-key case, where the name already is the source text).
        var defaultMessage = _defaultsByKey.TryGetValue(name, out var value) ? value : name;
        var rendered = _localizer.Translate(name, defaultMessage, Named(arguments));
        return new LocalizedString(name, rendered, resourceNotFound: false);
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

    // Maps each twinned member's stable key to the source default its [Localized…] twin carries: the display /
    // description key is the system attribute's value, the message key is the validator's ErrorMessage. A key with
    // no twin is absent from the map and falls back to being its own default (text-as-key).
    private static IReadOnlyDictionary<string, string> BuildDefaults(Type modelType)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        AddDefaults(modelType, map);
        foreach (PropertyInfo property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AddDefaults(property, map);
        }

        return map;
    }

    private static void AddDefaults(MemberInfo member, Dictionary<string, string> map)
    {
        if (member.GetCustomAttribute<LocalizedDisplayNameAttribute>() is { } displayTwin && DisplayKey(member) is { } displayKey)
        {
            map[displayKey] = displayTwin.Default;
        }

        if (member.GetCustomAttribute<LocalizedDescriptionAttribute>() is { } descriptionTwin && DescriptionKey(member) is { } descriptionKey)
        {
            map[descriptionKey] = descriptionTwin.Default;
        }

        foreach (LocalizedMessageAttribute message in member.GetCustomAttributes<LocalizedMessageAttribute>())
        {
            if (ErrorMessageKey(member, message.ValidationType) is { } messageKey)
            {
                map[messageKey] = message.Default;
            }
        }
    }

    // The display-name key the framework looks the member up by — [DisplayName] or [Display(Name)] (GetName, so it
    // matches the framework's own resolution). Null when neither carries a non-empty value.
    private static string? DisplayKey(MemberInfo member)
    {
        if (member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName is { Length: > 0 } displayName)
        {
            return displayName;
        }

        return member.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } name ? name : null;
    }

    private static string? DescriptionKey(MemberInfo member)
    {
        if (member.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } description)
        {
            return description;
        }

        return member.GetCustomAttribute<DisplayAttribute>()?.GetDescription() is { Length: > 0 } displayDescription
            ? displayDescription
            : null;
    }

    // The ErrorMessage of the validator named by a message twin — the key that validator's message resolves under.
    private static string? ErrorMessageKey(MemberInfo member, Type validationType) =>
        member.GetCustomAttributes()
            .OfType<ValidationAttribute>()
            .FirstOrDefault(attribute => attribute.GetType() == validationType)?.ErrorMessage;
}
