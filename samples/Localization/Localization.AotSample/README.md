# Localization.AotSample

Localizes a NativeAOT app the AOT-safe way: a loose file plus a main-assembly embedded catalog, with no culture satellite.

## What it shows
- A loose `.xliff` file copied beside the binary, loaded by the ambient store.
- A catalog embedded in the main assembly, advertised by an assembly attribute (no resource scan).
- Deliberately NO culture satellite: NativeAOT cannot load one, so it would silently degrade to the in-code default.

## Running
```bash
dotnet run --project samples/Localization/Localization.AotSample
```
Prints the German `files:` and `main assembly:` lines, then `AOT OK`.

To exercise the real NativeAOT scenario, publish a native binary and run it:
```bash
dotnet publish samples/Localization/Localization.AotSample -c Release -r linux-x64
```
The native binary prints the same German strings and `AOT OK`.

## Notes
This sample avoids culture satellites on purpose: NativeAOT cannot load a satellite assembly, so a satellite-routed translation would fall back to the in-code default. See `Localization.TrimSample` for the validation of that failure mode.
