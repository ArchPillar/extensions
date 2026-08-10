using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Resolves the localized display name and description a type or member carries as an annotation — the runtime
/// counterpart of the build-time annotation extraction, for the consumers ASP.NET's DataAnnotations pipeline does
/// not reach (Blazor, a console renderer, any hand-rolled UI). It reads, by reflection, a
/// <see cref="LocalizedAttribute"/> or, failing that, the system attribute plus its <c>[Localized…]</c> twin, then
/// resolves the resulting key through the localizer under the declaring type's category — the
/// <c>(category, key, default)</c> the extractor wrote. A member with no annotation renders as its own name, so a
/// caller never has to null-check. Reflection over a member's attributes is inherent to reading attributes at
/// runtime; this and <see cref="EnumLocalizationExtensions"/> are the only places the library does it.
/// </summary>
public static class MemberLocalizationExtensions
{
    /// <summary>
    /// Returns the localized display name of <paramref name="member"/> through the process-wide ambient store.
    /// </summary>
    /// <param name="member">The type or member to label. A <see cref="Type"/> is itself a member, so a type's own
    /// display name resolves through this overload too.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no display annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public static string GetLocalizedDisplayName(this MemberInfo member) =>
        GetLocalizedDisplayName(member, Localizer.Ambient);

    /// <summary>
    /// Returns the localized display name of <paramref name="member"/> through <paramref name="context"/> — the
    /// isolated-context overload (tests, multi-tenant hosting).
    /// </summary>
    /// <param name="member">The type or member to label.</param>
    /// <param name="context">The localization context to resolve through.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no display annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="context"/> is
    /// <see langword="null"/>.</exception>
    public static string GetLocalizedDisplayName(this MemberInfo member, LocalizationContext context)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(context);

        return Resolve(member, context, DisplayNameAnnotation(member));
    }

    /// <summary>
    /// Returns the localized display name of the member <paramref name="member"/> selects — the expression form, so
    /// a caller writes <c>MemberLocalizationExtensions.GetLocalizedDisplayName&lt;RegisterModel&gt;(x =&gt;
    /// x.Password)</c> instead of reaching for <see cref="Type.GetProperty(string)"/> themselves.
    /// </summary>
    /// <typeparam name="T">The type declaring the member.</typeparam>
    /// <param name="member">An expression selecting a property or field of <typeparamref name="T"/>.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no display annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="member"/> does not select a property or field.</exception>
    public static string GetLocalizedDisplayName<T>(Expression<Func<T, object?>> member) =>
        MemberOf(member).GetLocalizedDisplayName();

    /// <summary>
    /// Returns the localized display name of the member <paramref name="member"/> selects, through
    /// <paramref name="context"/> — the isolated-context overload of the expression form.
    /// </summary>
    /// <typeparam name="T">The type declaring the member.</typeparam>
    /// <param name="member">An expression selecting a property or field of <typeparamref name="T"/>.</param>
    /// <param name="context">The localization context to resolve through.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no display annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="context"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="member"/> does not select a property or field.</exception>
    public static string GetLocalizedDisplayName<T>(Expression<Func<T, object?>> member, LocalizationContext context) =>
        MemberOf(member).GetLocalizedDisplayName(context);

    /// <summary>
    /// Returns the localized description of <paramref name="member"/> through the process-wide ambient store.
    /// </summary>
    /// <param name="member">The type or member to describe.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no description annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public static string GetLocalizedDescription(this MemberInfo member) =>
        GetLocalizedDescription(member, Localizer.Ambient);

    /// <summary>
    /// Returns the localized description of <paramref name="member"/> through <paramref name="context"/> — the
    /// isolated-context overload (tests, multi-tenant hosting).
    /// </summary>
    /// <param name="member">The type or member to describe.</param>
    /// <param name="context">The localization context to resolve through.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no description annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="context"/> is
    /// <see langword="null"/>.</exception>
    public static string GetLocalizedDescription(this MemberInfo member, LocalizationContext context)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(context);

        return Resolve(member, context, DescriptionAnnotation(member));
    }

    /// <summary>
    /// Returns the localized description of the member <paramref name="member"/> selects — the expression form.
    /// </summary>
    /// <typeparam name="T">The type declaring the member.</typeparam>
    /// <param name="member">An expression selecting a property or field of <typeparamref name="T"/>.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no description annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="member"/> does not select a property or field.</exception>
    public static string GetLocalizedDescription<T>(Expression<Func<T, object?>> member) =>
        MemberOf(member).GetLocalizedDescription();

    /// <summary>
    /// Returns the localized description of the member <paramref name="member"/> selects, through
    /// <paramref name="context"/> — the isolated-context overload of the expression form.
    /// </summary>
    /// <typeparam name="T">The type declaring the member.</typeparam>
    /// <param name="member">An expression selecting a property or field of <typeparamref name="T"/>.</param>
    /// <param name="context">The localization context to resolve through.</param>
    /// <returns>The translation for the current UI culture, the source-language default, or the member's name when
    /// it carries no description annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="context"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="member"/> does not select a property or field.</exception>
    public static string GetLocalizedDescription<T>(Expression<Func<T, object?>> member, LocalizationContext context) =>
        MemberOf(member).GetLocalizedDescription(context);

    /// <summary>
    /// The <c>(key, default)</c> a member's display-name annotation carries: the self-contained
    /// <see cref="LocalizedAttribute"/> first, then the system attribute whose literal is the key, with a
    /// <see cref="LocalizedDisplayNameAttribute"/> twin supplying the default for it. Null when neither is present.
    /// </summary>
    internal static (string Key, string Default)? DisplayNameAnnotation(MemberInfo member)
    {
        if (member.GetCustomAttribute<LocalizedAttribute>() is { } localized)
        {
            return (localized.Key, localized.Default);
        }

        var key = member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
            ?? member.GetCustomAttribute<DisplayAttribute>()?.Name;
        return key is null ? null : (key, member.GetCustomAttribute<LocalizedDisplayNameAttribute>()?.Default ?? key);
    }

    /// <summary>
    /// The <c>(key, default)</c> a member's description annotation carries. On <see cref="LocalizedAttribute"/> the
    /// description is optional: its text is the default and its key is <c>DescriptionKey</c> or, unset, that text.
    /// </summary>
    internal static (string Key, string Default)? DescriptionAnnotation(MemberInfo member)
    {
        if (member.GetCustomAttribute<LocalizedAttribute>() is { Description: { } description } localized)
        {
            return (localized.DescriptionKey ?? description, description);
        }

        var key = member.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? member.GetCustomAttribute<DisplayAttribute>()?.Description;
        return key is null ? null : (key, member.GetCustomAttribute<LocalizedDescriptionAttribute>()?.Default ?? key);
    }

    // The category is the declaring type's full name — a member's enclosing type, a type's own name — matching what
    // the extractor wrote and what the DataAnnotations bridge looks up by.
    private static string Resolve(MemberInfo member, LocalizationContext context, (string Key, string Default)? annotation)
    {
        if (annotation is not { } found)
        {
            // No annotation means no catalog entry; the member's own name is the sensible label.
            return member.Name;
        }

        Type category = member as Type ?? member.DeclaringType ?? member.ReflectedType!;
        return context.TranslateInCategory(CategoryName.Of(category), found.Key, found.Default, [], out _);
    }

    // Unwraps the conversion a value-typed member picks up from the object? return, so x => x.Count and
    // x => x.Name both reach their MemberExpression.
    private static MemberInfo MemberOf<T>(Expression<Func<T, object?>> member)
    {
        ArgumentNullException.ThrowIfNull(member);

        Expression body = member.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : member.Body;
        return body is MemberExpression { Member: PropertyInfo or FieldInfo } selected
            ? selected.Member
            : throw new ArgumentException("The expression must select a property or field.", nameof(member));
    }
}
