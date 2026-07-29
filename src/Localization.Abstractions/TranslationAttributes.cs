namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Marks the parameter whose argument is the stable symbolic translation key. A call binding a
/// compile-time constant to this parameter is a translation site.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslatableAttribute : Attribute
{
}

/// <summary>
/// Marks the parameter whose argument is the source-language default message (ICU MessageFormat).
/// This in-code default is the runtime source of truth and the terminal fallback.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslationDefaultAttribute : Attribute
{
}

/// <summary>
/// Marks the generic type parameter that supplies the translation category. When a translatable call's
/// receiver is a constructed generic type whose parameter carries this attribute, extraction and the
/// runtime both take the category from that type argument's full name — the <c>ILogger&lt;T&gt;</c>
/// model. Keeping the signal an attribute, rather than a hardcoded type name, lets anyone define their
/// own scoped localizer and have it detected identically.
/// </summary>
[AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple = false)]
public sealed class TranslationScopeAttribute : Attribute
{
}
