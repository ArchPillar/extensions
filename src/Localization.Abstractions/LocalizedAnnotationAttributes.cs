#if NET8_0_OR_GREATER
using System.ComponentModel.DataAnnotations;
#endif

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The localized twin of <see cref="System.ComponentModel.DisplayNameAttribute"/> and
/// <c>[Display(Name = …)]</c>. It rides <em>beside</em> the system attribute — it does not replace it — carrying
/// a stable symbolic <see cref="Key"/> and a clean source-language <see cref="Default"/>, so the display name is
/// extracted and resolved under a key that survives an edit to the literal. Apply both: the system attribute the
/// framework reads, this twin the library resolves through.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDisplayNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance whose key and default are the same text — the common case for an enum member
    /// whose stable key reads naturally as its source-language label (<c>[LocalizedDisplayName("Active")]</c>).
    /// </summary>
    /// <param name="key">The stable symbolic key, also used as the source-language default.</param>
    public LocalizedDisplayNameAttribute(string key)
        : this(key, key)
    {
    }

    /// <summary>
    /// Initializes a new instance with a distinct stable key and source-language default.
    /// </summary>
    /// <param name="key">The stable symbolic key the display name resolves under.</param>
    /// <param name="defaultValue">The source-language display name (the in-code default and terminal fallback).</param>
    public LocalizedDisplayNameAttribute(string key, string defaultValue)
    {
        Key = key;
        Default = defaultValue;
    }

    /// <summary>Gets the stable symbolic key the display name resolves under.</summary>
    public string Key { get; }

    /// <summary>Gets the source-language display name.</summary>
    public string Default { get; }
}

/// <summary>
/// The localized twin of <see cref="System.ComponentModel.DescriptionAttribute"/> and
/// <c>[Display(Description = …)]</c>, riding beside the system attribute with a stable symbolic <see cref="Key"/>
/// and a clean source-language <see cref="Default"/>. The description counterpart of
/// <see cref="LocalizedDisplayNameAttribute"/>.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance whose key and default are the same text.
    /// </summary>
    /// <param name="key">The stable symbolic key, also used as the source-language default.</param>
    public LocalizedDescriptionAttribute(string key)
        : this(key, key)
    {
    }

    /// <summary>
    /// Initializes a new instance with a distinct stable key and source-language default.
    /// </summary>
    /// <param name="key">The stable symbolic key the description resolves under.</param>
    /// <param name="defaultValue">The source-language description (the in-code default and terminal fallback).</param>
    public LocalizedDescriptionAttribute(string key, string defaultValue)
    {
        Key = key;
        Default = defaultValue;
    }

    /// <summary>Gets the stable symbolic key the description resolves under.</summary>
    public string Key { get; }

    /// <summary>Gets the source-language description.</summary>
    public string Default { get; }
}

/// <summary>
/// The localized twin for a validation attribute's error message, identified by the validator type
/// <typeparamref name="TValidation"/>. Where display name and description are a closed set with a named twin each,
/// validation attributes are open-ended — the built-ins plus any custom <c>ValidationAttribute</c> — so one
/// generic twin, keyed by the validator type, covers them all and disambiguates a member that carries several
/// validators at once. Carries a stable symbolic <see cref="Key"/> and a source-language <see cref="Default"/>.
/// </summary>
/// <typeparam name="TValidation">The validation attribute this message belongs to (for example <c>[Required]</c>
/// or <c>[Range]</c>). Constrained to <c>ValidationAttribute</c> where the framework provides it in-box (net8.0
/// and later).</typeparam>
/// <remarks>
/// <see cref="AttributeUsageAttribute.AllowMultiple"/> is <see langword="true"/>: C# counts every constructed form
/// of a generic attribute as the same attribute for the duplicate-application check, so a member carrying several
/// validators (<c>[LocalizedMessage&lt;RequiredAttribute&gt;]</c> and <c>[LocalizedMessage&lt;RangeAttribute&gt;]</c>)
/// would not compile otherwise.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = true)]
public sealed class LocalizedMessageAttribute<TValidation> : Attribute
#if NET8_0_OR_GREATER
    where TValidation : ValidationAttribute
#endif
{
    /// <summary>
    /// Initializes a new instance whose key and default are the same text.
    /// </summary>
    /// <param name="key">The stable symbolic key, also used as the source-language default.</param>
    public LocalizedMessageAttribute(string key)
        : this(key, key)
    {
    }

    /// <summary>
    /// Initializes a new instance with a distinct stable key and source-language default.
    /// </summary>
    /// <param name="key">The stable symbolic key the error message resolves under.</param>
    /// <param name="defaultValue">The source-language error message (the in-code default and terminal fallback).</param>
    public LocalizedMessageAttribute(string key, string defaultValue)
    {
        Key = key;
        Default = defaultValue;
    }

    /// <summary>Gets the stable symbolic key the error message resolves under.</summary>
    public string Key { get; }

    /// <summary>Gets the source-language error message.</summary>
    public string Default { get; }
}
