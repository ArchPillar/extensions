# Localization.WasmSample

Demonstrates ArchPillar.Extensions.Localization running client-side in a Blazor WebAssembly app, with catalogs fetched over HTTP and culture switched live in the browser.

## What it shows

- Fetching an ARB catalog over HTTP from static web assets — the browser has no readable file system, so the directory source finds nothing
- Injecting `ILocalizer` and `IStringLocalizer<T>` into components over the ambient store
- Switching culture in code at runtime with a button — no page reload, no request middleware
- A missing override surfacing via `IStringLocalizer`'s `ResourceNotFound`, so the key shows through

## Running

```bash
dotnet run --project samples/Localization/Localization.WasmSample
```

The dev server prints a local URL (e.g. `http://localhost:5000`); open it in a browser. The page renders an inbox with ICU plurals; click the English/Deutsch buttons to switch culture and watch the strings re-render in place without a reload.

## Notes

English ships in code and is the source culture; only the German override (`wwwroot/Translations/de.xliff`) is fetched. The UI lives in `Pages/Home.razor`.
