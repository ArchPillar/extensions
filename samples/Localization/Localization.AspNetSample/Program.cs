using System.Globalization;
using ArchPillar.Extensions.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

// ---------------------------------------------------------------------------
// Localization.AspNetSample
//
// Demonstrates ArchPillar.Extensions.Localization in an ASP.NET Core minimal API:
//   - Registering both ILocalizer and the IStringLocalizer adapter with AddArchPillarLocalization
//   - ASP.NET request-culture middleware driving the active culture from the ?culture= query string
//   - ILocalizer at /: named arguments and ICU plurals, in-code English overridden by de.xliff
//   - The IStringLocalizer adapter at /strings, where a missing entry returns the key with
//     ResourceNotFound set (the failure path)
//   - [Localized] annotations at /form: field labels and hints declared on the model itself and read
//     back with the MemberInfo helpers, the path that needs no MVC and no IStringLocalizer. One field
//     is deliberately left unannotated, so it falls back to its own member name (the failure path)
//
// Everything lives in this file; the German catalog is Translations/de.xliff.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// English ships in code; a German catalog (Translations/de.xliff) loads as an override at runtime. The
// StringLocalizer interop package registers both the native ILocalizer and the IStringLocalizer adapter via
// AddArchPillarStringLocalizer.
builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en"
});

WebApplication app = builder.Build();

// Standard ASP.NET request-culture middleware. It sets CurrentUICulture per request (here from the
// ?culture= query string by default), which is exactly what ILocalizer reads — no extra wiring.
CultureInfo[] supportedCultures = [new("en"), new("de")];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

// Inject ILocalizer for the full model: in-code English default, German override, named arguments,
// and ICU plurals. Try /?culture=de and /?culture=en.
app.MapGet("/", (ILocalizer localizer) => new
{
    greeting = localizer.Translate("home.greeting", "Hello {name}", ("name", "Ada")),
    inbox = localizer.Translate(
        "inbox.count",
        "{count, plural, =0 {No messages} one {# message} other {# messages}}",
        ("count", 3))
});

// Inject IStringLocalizer for teams with existing ASP.NET code: the name is the key, a missing entry
// returns the name with ResourceNotFound set, and positional arguments map to {0}. Try /strings?culture=de.
app.MapGet("/strings", (IStringLocalizer<Program> localizer) =>
{
    LocalizedString summary = localizer["inbox.summary", 3];
    return new { value = summary.Value, resourceNotFound = summary.ResourceNotFound };
});

// Field labels and hints declared as annotations on the model, read back by reflection. A minimal API has
// no MVC model metadata and no IStringLocalizer, so this is the path that carries a form's chrome: the
// attribute is the translation site (extraction reads it from the assembly), and the helpers resolve it under
// the declaring type's category. Try /form?culture=de.
app.MapGet("/form", () =>
{
    var model = new RegisterModel();
    return new
    {
        // The type-free form: the declaring type comes from the member the expression reaches.
        email = new
        {
            label = MemberLocalizationExtensions.GetLocalizedDisplayName(() => model.Email),
            hint = MemberLocalizationExtensions.GetLocalizedDescription(() => model.Email)
        },
        // The same thing straight off a MemberInfo, which is what a generic form renderer would hold.
        password = typeof(RegisterModel).GetProperty(nameof(RegisterModel.Password))!.GetLocalizedDisplayName(),
        // No annotation at all: the member's own name is the label, so a renderer never gets a null.
        nickname = MemberLocalizationExtensions.GetLocalizedDisplayName(() => model.Nickname)
    };
});

app.Run();

// The strings a form needs live on the model. [Localized] carries the stable key and the source-language
// default in one attribute; the description's key is derived from the display key (user.email.description),
// so a field never repeats its own id.
internal sealed class RegisterModel
{
    [Localized("user.email", "Email address", Description = "We never share it.")]
    public string Email { get; set; } = "";

    [Localized("user.password", "Password", Description = "At least 12 characters.")]
    public string Password { get; set; } = "";

    public string Nickname { get; set; } = "";
}

/// <summary>The application entry point, made public so <see cref="IStringLocalizer{T}"/> can close over it.</summary>
public partial class Program;
