#if NET8_0_OR_GREATER
using System.ComponentModel.DataAnnotations;
#endif

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Supplies the source-language default text for a display name whose stable key — a string id — lives in the
/// system attribute it accompanies (<c>[DisplayName]</c> or <c>[Display(Name = …)]</c>). Reach for it when you
/// prefer a string id to the text-as-key default: put the id in the system attribute (which the framework looks
/// up by) and the human-readable default here. Without this twin the system attribute's literal is both key and
/// default.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDisplayNameAttribute : Attribute
{
    /// <summary>Initializes a new instance with the source-language default text.</summary>
    /// <param name="defaultValue">The source-language display name (the in-code default and terminal fallback);
    /// the stable key comes from the accompanying <c>[DisplayName]</c> / <c>[Display(Name)]</c>.</param>
    public LocalizedDisplayNameAttribute(string defaultValue)
    {
        Default = defaultValue;
    }

    /// <summary>Gets the source-language display name.</summary>
    public string Default { get; }
}

/// <summary>
/// Supplies the source-language default text for a description whose stable key lives in the system attribute it
/// accompanies (<c>[Description]</c> or <c>[Display(Description = …)]</c>). The description counterpart of
/// <see cref="LocalizedDisplayNameAttribute"/>.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class LocalizedDescriptionAttribute : Attribute
{
    /// <summary>Initializes a new instance with the source-language default text.</summary>
    /// <param name="defaultValue">The source-language description (the in-code default and terminal fallback);
    /// the stable key comes from the accompanying <c>[Description]</c> / <c>[Display(Description)]</c>.</param>
    public LocalizedDescriptionAttribute(string defaultValue)
    {
        Default = defaultValue;
    }

    /// <summary>Gets the source-language description.</summary>
    public string Default { get; }
}

/// <summary>
/// The non-generic base of <see cref="LocalizedMessageAttribute{TValidation}"/>, so every constructed message
/// twin can be read back without knowing its validator type at compile time — <c>GetCustomAttributes</c> of this
/// type returns them all, each exposing its <see cref="Default"/> and <see cref="ValidationType"/>. Not applied
/// directly; apply the generic form.
/// </summary>
public abstract class LocalizedMessageAttribute : Attribute
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
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = true)]
public sealed class LocalizedMessageAttribute<TValidation> : LocalizedMessageAttribute
#if NET8_0_OR_GREATER
    where TValidation : ValidationAttribute
#endif
{
    /// <summary>Initializes a new instance with the source-language default error message.</summary>
    /// <param name="defaultValue">The source-language error message; the stable key comes from
    /// <typeparamref name="TValidation"/>'s <c>ErrorMessage</c> on the same member.</param>
    public LocalizedMessageAttribute(string defaultValue)
        : base(defaultValue, typeof(TValidation))
    {
    }
}
