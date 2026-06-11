using System.Globalization;
using ArchPillar.Extensions.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

// ---------------------------------------------------------------------------
// Localization.AspNetSample
//
// Demonstrates ArchPillar.Extensions.Localization in an ASP.NET Core minimal API:
//   - Registering both the DefaultLocalizer and the IStringLocalizer adapter with AddArchPillarLocalization
//   - ASP.NET request-culture middleware driving the active culture from the ?culture= query string
//   - The DefaultLocalizer at /: named arguments and ICU plurals, in-code English overridden by de.arb
//   - The IStringLocalizer adapter at /strings, where a missing entry returns the key with
//     ResourceNotFound set (the failure path)
//
// Everything lives in this file; the German catalog is Translations/de.arb.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// English ships in code; a German catalog (Translations/de.arb) loads as an override at runtime. The
// StringLocalizer interop package registers both the native DefaultLocalizer and the IStringLocalizer adapter via
// AddArchPillarStringLocalizer.
builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en"
});

WebApplication app = builder.Build();

// Standard ASP.NET request-culture middleware. It sets CurrentUICulture per request (here from the
// ?culture= query string by default), which is exactly what the DefaultLocalizer reads — no extra wiring.
CultureInfo[] supportedCultures = [new("en"), new("de")];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

// Inject the DefaultLocalizer for the full model: in-code English default, German override, named arguments,
// and ICU plurals. Try /?culture=de and /?culture=en.
app.MapGet("/", (DefaultLocalizer localizer) => new
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

app.Run();

/// <summary>The application entry point, made public so <see cref="IStringLocalizer{T}"/> can close over it.</summary>
public partial class Program;
