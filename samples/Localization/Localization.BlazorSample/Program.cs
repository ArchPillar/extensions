using System.Globalization;
using ArchPillar.Extensions.Localization;
using Localization.BlazorSample.Components;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// English ships in code; the German catalog (Translations/de.arb) loads as a runtime override. Both the
// Localizer and the IStringLocalizer adapter are registered, so components can inject either.
builder.Services.AddArchPillarLocalization(new LocalizerOptions
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
