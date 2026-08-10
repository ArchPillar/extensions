#if NET8_0_OR_GREATER
using System.ComponentModel.DataAnnotations;
#endif

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Declares a member's display text — a stable key and its source-language default — in one attribute, with no
/// system attribute to hang them on. This is the low-noise form: where the twin attributes need a
/// <c>[Display(Name = …)]</c> to carry the key, this carries both itself, so a member needs one line instead of
/// two. A description is optional and follows the same shape: set <see cref="Description"/> for its text and, if
/// you want a string id rather than text-as-key, <see cref="DescriptionKey"/> for its key.
/// <para>
/// Reach for <c>[Display]</c> (with a <see cref="LocalizedDisplayNameAttribute"/> twin when you want a string id)
/// when something other than this library must also read the annotation — the framework's own
/// <c>Order</c>/<c>GroupName</c>/<c>Prompt</c>, or a third-party consumer that looks for
/// <c>DisplayAttribute</c> specifically. Both forms extract identically and resolve under the declaring type's
/// category, so they can be mixed freely in one model.
/// </para>
/// </summary>
/// <remarks>Initializes a new instance with the display name's key and source-language default.</remarks>
/// <param name="key">The stable symbolic key this member's display name resolves under.</param>
/// <param name="defaultValue">The source-language display name (the in-code default and terminal fallback).</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedAttribute(
    string key,
    string defaultValue)
    : Attribute
{
    /// <summary>Gets the stable key the display name resolves under.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the source-language display name.</summary>
    public string Default { get; } = defaultValue;

    /// <summary>
    /// Gets or sets the source-language description. When unset the member carries no description at all; when
    /// set it is the description's default, and its key is <see cref="DescriptionKey"/> or, failing that, this
    /// text itself (the same text-as-key default the system attributes use).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the stable key the description resolves under. Ignored unless <see cref="Description"/> is
    /// also set, since a key with no source text has nothing to fall back to.
    /// </summary>
    public string? DescriptionKey { get; set; }
}

/// <summary>
/// Supplies the source-language default text for a display name whose stable key — a string id — lives in the
/// system attribute it accompanies (<c>[DisplayName]</c> or <c>[Display(Name = …)]</c>). Reach for it when you
/// prefer a string id to the text-as-key default: put the id in the system attribute (which the framework looks
/// up by) and the human-readable default here. Without this twin the system attribute's literal is both key and
/// default.
/// </summary>
/// <remarks>Initializes a new instance with the source-language default text.</remarks>
/// <param name="defaultValue">The source-language display name (the in-code default and terminal fallback);
/// the stable key comes from the accompanying <c>[DisplayName]</c> / <c>[Display(Name)]</c>.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDisplayNameAttribute(
    string defaultValue)
    : Attribute
{
    /// <summary>Gets the source-language display name.</summary>
    public string Default { get; } = defaultValue;
}

/// <summary>
/// Supplies the source-language default text for a description whose stable key lives in the system attribute it
/// accompanies (<c>[Description]</c> or <c>[Display(Description = …)]</c>). The description counterpart of
/// <see cref="LocalizedDisplayNameAttribute"/>.
/// </summary>
/// <remarks>Initializes a new instance with the source-language default text.</remarks>
/// <param name="defaultValue">The source-language description (the in-code default and terminal fallback);
/// the stable key comes from the accompanying <c>[Description]</c> / <c>[Display(Description)]</c>.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDescriptionAttribute(
    string defaultValue)
    : Attribute
{
    /// <summary>Gets the source-language description.</summary>
    public string Default { get; } = defaultValue;
}

/// <summary>
/// The non-generic base of <see cref="LocalizedMessageAttribute{TValidation}"/>, so every constructed message
/// twin can be read back without knowing its validator type at compile time — <c>GetCustomAttributes</c> of this
/// type returns them all, each exposing its <see cref="Default"/> and <see cref="ValidationType"/>. Not applied
/// directly; apply the generic form.
/// </summary>
public abstract class LocalizedMessageAttribute
    : Attribute
{
    private protected LocalizedMessageAttribute(string defaultValue, Type validationType)
    {
        Default = defaultValue;
        ValidationType = validationType;
    }

    /// <summary>Gets the source-language error message (the in-code default and terminal fallback).</summary>
    public string Default { get; }

    /// <summary>Gets the validation attribute type whose <c>ErrorMessage</c> is the stable key.</summary>
    public Type ValidationType { get; }
}

/// <summary>
/// Supplies the source-language default text for a validation attribute's error message whose stable key — a
/// string id — lives in that validator's <c>ErrorMessage</c>. <typeparamref name="TValidation"/> names which
/// validator on the member the message belongs to, so a property carrying several validators stays unambiguous:
/// pair <c>[Required(ErrorMessage = "user.email.required")]</c> with
/// <c>[LocalizedMessage&lt;RequiredAttribute&gt;("An email address is required.")]</c> and the message extracts
/// and resolves under <c>user.email.required</c>.
/// </summary>
/// <typeparam name="TValidation">The validation attribute this message belongs to (for example <c>[Required]</c>
/// or <c>[Range]</c>). Constrained to <c>ValidationAttribute</c> where the framework provides it in-box (net8.0
/// and later).</typeparam>
/// <remarks>
/// <see cref="AttributeUsageAttribute.AllowMultiple"/> is <see langword="true"/>: C# counts every constructed
/// form of a generic attribute as the same attribute for the duplicate-application check, so a member carrying
/// several validators would not compile otherwise.
/// </remarks>
/// <remarks>Initializes a new instance with the source-language default error message.</remarks>
/// <param name="defaultValue">The source-language error message; the stable key comes from
/// <typeparamref name="TValidation"/>'s <c>ErrorMessage</c> on the same member.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = true)]
public sealed class LocalizedMessageAttribute<TValidation>(
    string defaultValue)
    : LocalizedMessageAttribute(defaultValue, typeof(TValidation))
#if NET8_0_OR_GREATER
    where TValidation : ValidationAttribute
#endif
{
}
