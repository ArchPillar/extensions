using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;

namespace ArchPillar.Extensions.Localization.AspNetCore.Tests;

/// <summary>
/// The ASP.NET Core static-file helpers: registering the catalog content types so a hosted Blazor WebAssembly
/// client's <c>.arb</c>/<c>.xliff</c>/<c>.po</c> catalogs are served instead of 404-ing on an unknown extension.
/// </summary>
public sealed class TranslationStaticFileExtensionsTests
{
    [Fact]
    public void AddArchPillarTranslationFormats_RegistersEveryCatalogContentType()
    {
        var provider = new FileExtensionContentTypeProvider();

        provider.AddArchPillarTranslationFormats();

        Assert.Equal("application/json", provider.Mappings[".arb"]);
        Assert.Equal("application/xml", provider.Mappings[".xliff"]);
        Assert.Equal("application/xml", provider.Mappings[".xlf"]);
        Assert.Equal("text/plain", provider.Mappings[".po"]);
        Assert.Equal("text/plain", provider.Mappings[".pot"]);
    }

    [Fact]
    public void AddArchPillarTranslationFormats_ReturnsSameProviderForChaining()
    {
        var provider = new FileExtensionContentTypeProvider();

        Assert.Same(provider, provider.AddArchPillarTranslationFormats());
    }

    [Fact]
    public void AddArchPillarTranslationFormats_NullProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((FileExtensionContentTypeProvider)null!).AddArchPillarTranslationFormats());

    [Fact]
    public void UseArchPillarTranslationFiles_RegistersMiddlewareAndReturnsBuilder()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        WebApplication app = builder.Build();

        IApplicationBuilder result = app.UseArchPillarTranslationFiles("/party");

        Assert.Same(app, result);
    }

    [Fact]
    public void UseArchPillarTranslationFiles_NullApp_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IApplicationBuilder)null!).UseArchPillarTranslationFiles());
}
