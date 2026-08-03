# Localization.WasmHostSample

An ASP.NET Core server that hosts the [`Localization.WasmSample`](../Localization.WasmSample/) Blazor WebAssembly
client, which in turn references a localized Razor class library ([`Localization.WasmLibrary`](../Localization.WasmLibrary/)).
This is the realistic deployment shape — **server hosts client, client references libraries** — and the one where
catalog publishing has to compose across three reference levels.

## What it shows

- How catalogs travel across the reference chain on publish: the **library** contributes its per-culture catalogs
  as static web assets; the **client** (the catalog "authority") gathers its own and the library's catalogs, merges
  them into one bundle per culture, and emits the `apl-catalogs.json` manifest; the **host** serves the client's
  published `wwwroot`.
- The published layout the runtime depends on: the merged bundle and the manifest under
  `wwwroot/Translations`, and **no** raw per-culture catalogs under `wwwroot/_content`. The raw catalogs are an
  intermediate the authority already merged — shipping them would be dead weight the manifest never points at, and
  before the publish-time prune they leaked into the host one reference level past the authority.

## Running

```bash
dotnet run --project samples/Localization/Localization.WasmHostSample
```

Open the printed URL in a browser: the server serves the WebAssembly client, which fetches its catalog manifest
and the German bundle over HTTP. Switch culture with the English/Deutsch buttons — the same client behaviour as the
standalone `Localization.WasmSample`, now served by a host.

## The publish assertion

The value of this sample is its **publish output**, which CI checks:

```bash
dotnet publish samples/Localization/Localization.WasmHostSample -c Release -o out
```

- `out/wwwroot/Translations/apl-catalogs.json` — the catalog manifest, present.
- `out/wwwroot/Translations/*.arb` — the merged per-culture bundle(s), present.
- `out/wwwroot/_content/**/Translations/*` — the libraries' raw catalogs, **absent** (merged, not shipped raw).

## Notes

The host authors no translatable strings of its own; it only serves the client. It sets
`ArchPillarLocalizationSampleAuthoring=true` so it imports the package's build targets the way a real consumer gets
them transitively (`buildTransitive`) — that is what runs the publish-time prune. A real project referencing the
NuGet packages gets this automatically.
