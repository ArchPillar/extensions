# Localization.WasmHostSample

An ASP.NET Core server that hosts the [`Localization.WasmSample`](../Localization.WasmSample/) Blazor WebAssembly
client, which in turn references a localized Razor class library ([`Localization.WasmLibrary`](../Localization.WasmLibrary/)).
This is the realistic deployment shape — **server hosts client, client references libraries** — and the one where
catalog publishing has to compose across three reference levels.

The host references the client with a plain `ProjectReference` and **no**
`Microsoft.AspNetCore.Components.WebAssembly.Server`: the client's assets reach the host through the static web
asset pipeline, which respects `AssetMode`. That is deliberate — it is the topology where the bug this sample
guards actually bites. The classic `WebAssembly.Server` host wholesale-copies the client's published output, which
masks it.

## What it shows

- How catalogs travel across the reference chain on publish: the **library** contributes its per-culture catalogs
  as static web assets (`AssetMode=All`); the **client** (the catalog "authority") gathers its own and the
  library's catalogs, merges them into one bundle per culture, and emits the `apl-catalogs.json` manifest — also
  `AssetMode=All`, so they cross the `ProjectReference` into the host. A `CurrentProject` asset does not, and on
  SDK versions that do not forward a referenced WebAssembly app's own assets the manifest and bundle would never
  reach the deployed `wwwroot`, leaving the app silently on its in-code defaults.
- The published layout the runtime depends on: the merged bundle and the manifest under `wwwroot/Translations`,
  and **no** raw per-culture catalogs under `wwwroot/_content` (the authority already merged them; the publish-time
  prune removes the copies that flow up a level past the authority).
- Serving with `MapStaticAssets()`, not plain `UseStaticFiles()`: the former honours the catalog content-type
  mappings the package targets register (`.arb` is `application/json`, XLIFF is XML, …) and the fingerprinted
  routes, so a `GET` for a bare file name the manifest lists (`/Translations/de.arb`) resolves. Plain
  `UseStaticFiles()` 404s on the unknown `.arb` type — a manifest that loads but whose entries 404 is its own
  failure mode.

## Running

```bash
dotnet run --project samples/Localization/Localization.WasmHostSample
```

Open the printed URL in a browser: the server serves the WebAssembly client, which fetches its catalog manifest and
the German bundle over HTTP. Switch culture with the English/Deutsch buttons — the same client behaviour as the
standalone `Localization.WasmSample`, now served by a host.

## The publish assertion

The value of this sample is its **publish output** and that the served files it names actually resolve, which CI
checks against the deploy shape (build once, publish `--no-build`):

```bash
dotnet build samples/Localization/Localization.WasmHostSample -c Release
dotnet publish samples/Localization/Localization.WasmHostSample -c Release --no-build -o out
```

- `out/wwwroot/Translations/apl-catalogs.json` — the catalog manifest, present.
- `out/wwwroot/Translations/*.arb` — the merged per-culture bundle(s), present, and containing the library's strings
  as well as the client's own.
- `out/wwwroot/_content/**/Translations/*` — the libraries' raw catalogs, **absent** (merged, not shipped raw).
- `out/Translations/*` — beside-the-binary file copies, **absent** (a WebAssembly app has no file system to read
  them, so the copy is suppressed rather than leaked to the host root).
- Running the published host, `GET /Translations/apl-catalogs.json` and every file it lists return `200`.

## Notes

The host authors no translatable strings of its own; it only serves the client. It sets
`ArchPillarLocalizationSampleAuthoring=true` so it imports the package's build targets the way a real consumer gets
them transitively (`buildTransitive`) — that is what runs the publish-time prune. A real project referencing the
NuGet packages gets this automatically.
