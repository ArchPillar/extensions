using System.Globalization;
using ArchPillar.Extensions.Localization;
using Localization.BlazorSample.Components;
using Microsoft.AspNetCore.Localization;

// ---------------------------------------------------------------------------
// Localization.BlazorSample
//
// Demonstrates ArchPillar.Extensions.Localization in a server-rendered Blazor app:
//   - Registering both the Localizer and the IStringLocalizer adapter with AddArchPillarLocalization
//   - Components injecting the Localizer for ICU plurals and the IStringLocalizer adapter for keyed lookups
//   - Request-culture middleware reading ?culture= so the per-page culture-switch links work on each navigation
//   - The IStringLocalizer path returning the key with ResourceNotFound when an entry is missing (the failure path)
//
// The UI lives in Components/Pages/Home.razor; the German catalog is Translations/de.arb.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// English ships in code; the German catalog (Translations/de.arb) loads as a runtime override. The
// StringLocalizer interop package registers both the native Localizer and the IStringLocalizer adapter, so
// components can inject either.
builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions
{
    TranslationsDirectory = Path.Combine(AppContext.BaseDirectory, "Translations"),
    SourceCulture = "en"
});

WebApplication app = builder.Build();

// The request-culture middleware sets CurrentUICulture from the ?culture= query string; each
// server-rendered navigation is a fresh request, so the culture-switch links below just work.
CultureInfo[] supportedCultures = [new("en"), new("de")];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseAntiforgery();
app.MapRazorComponents<App>();

app.Run();
