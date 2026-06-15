using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Resolves the localized display name of an enum value from its member's annotation — the runtime counterpart
/// of the build-time annotation extraction. It reads, by reflection, a <see cref="LocalizedDisplayNameAttribute"/>
/// twin (a stable key and a clean default) or, failing that, a <c>[Display(Name = …)]</c> literal, then resolves
/// it through the localizer under the enum type's category (the <c>(category, key, default)</c> the extractor
/// wrote). A member with no display annotation, or a composite/undefined value, renders as its name — the same as
/// <see cref="object.ToString"/>. Reflection over the member's attributes is inherent to reading attributes at
/// runtime; this helper is the single place the library does it.
/// </summary>
public static class EnumLocalizationExtensions
{
    /// <summary>
    /// Returns the localized display name of <paramref name="value"/> through the process-wide ambient store.
    /// </summary>
    /// <param name="value">The enum value to label.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member name when
    /// the member carries no display annotation.</returns>
    public static string GetLocalizedDisplayName(this Enum value) =>
        Resolve(value, Localizer.Ambient);

    /// <summary>
    /// Returns the localized display name of <paramref name="value"/> through <paramref name="context"/> — the
    /// isolated-context overload (tests, multi-tenant hosting).
    /// </summary>
    /// <param name="value">The enum value to label.</param>
    /// <param name="context">The localization context to resolve through.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member name when
    /// the member carries no display annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static string GetLocalizedDisplayName(this Enum value, LocalizationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return Resolve(value, context);
    }

    private static string Resolve(Enum value, LocalizationContext context)
    {
        Type type = value.GetType();
        var name = value.ToString();
        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        if (field is null || KeyAndDefault(field) is not { } annotation)
        {
            // A composite/undefined value (no single member) or a member with no display annotation has no
            // catalog entry; its name is the sensible label, matching Enum.ToString().
            return name;
        }

        return context.TranslateInCategory(type.FullName ?? type.Name, annotation.Key, annotation.Default, [], out _);
    }

    // The (key, default) the member's display annotation carries: a [LocalizedDisplayName] twin wins with its
    // stable key and clean default; otherwise a [Display(Name = …)] literal is both. ([DisplayName] is not valid
    // on an enum member, so it does not apply here.) Null when no display annotation is present.
    private static (string Key, string Default)? KeyAndDefault(FieldInfo field)
    {
        LocalizedDisplayNameAttribute? twin = field.GetCustomAttribute<LocalizedDisplayNameAttribute>();
        if (twin is not null)
        {
            return (twin.Key, twin.Default);
        }

        DisplayAttribute? display = field.GetCustomAttribute<DisplayAttribute>();
        return display?.Name is { } displayName ? (displayName, displayName) : null;
    }
}
