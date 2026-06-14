# ArchPillar.Extensions.Localization.AspNetCore

Optional ASP.NET Core hosting helpers for `ArchPillar.Extensions.Localization`. A hosted Blazor WebAssembly
client fetches its translation catalogs (`.arb`, `.xliff`, `.po`) over HTTP, but the static file middleware
returns 404 for unknown file extensions by default — so the catalogs never load and the app silently falls back
to its in-code defaults. These helpers register the catalog content types.

For a single static-files registration, use the one-call convenience:

```csharp
app.UseArchPillarTranslationFiles();          // root
app.UseArchPillarTranslationFiles("/party");  // under a request path
```

When the app already configures `UseStaticFiles`, register the catalog types on a content-type provider and
pass it to each call instead — handy when several WebAssembly clients are hosted under different paths:

```csharp
var provider = new FileExtensionContentTypeProvider().AddArchPillarTranslationFormats();

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/admin", ContentTypeProvider = provider });
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/party", ContentTypeProvider = provider });
```

The client side — fetching the catalogs and discovering them through the build-emitted manifest — lives in the
core [`ArchPillar.Extensions.Localization`](https://www.nuget.org/packages/ArchPillar.Extensions.Localization)
package (`Localizer.AddCatalogsFromManifestAsync`). This package only concerns serving the catalog files.
