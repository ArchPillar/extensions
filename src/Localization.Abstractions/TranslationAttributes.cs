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
/// Marks the generic parameter that supplies the translation category, whose type argument's full name
/// becomes that category — the <c>ILogger&lt;T&gt;</c> model. It sits either on a type's parameter, so a
/// constructed receiver carries the scope (<c>ILocalizer&lt;T&gt;</c>, or a base such as
/// <c>Localized&lt;TSelf&gt;</c>), or on a method's own parameter, so a static or extension method
/// defines the scope through its type argument (<c>Label&lt;T&gt;(…)</c>). Keeping the signal an
/// attribute, rather than a hardcoded type name, lets anyone define their own scoped localizer and have
/// it detected identically. The attribute tells extraction which argument names the category; it does
/// not redirect the lookup, so a method that declares a scope must also resolve through it (for example
/// <c>Localizer.For&lt;T&gt;()</c>) or its strings are extracted under one category and looked up under
/// another.
/// </summary>
[AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple = false)]
public sealed class TranslationScopeAttribute : Attribute
{
}
