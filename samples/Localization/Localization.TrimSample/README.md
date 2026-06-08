# Localization.TrimSample

Validates the opt-in embed path — a main-assembly catalog and a culture satellite — under trimming / single-file / AOT, self-checking and printing OK/BROKEN.

## What it shows
- A main-assembly embedded catalog, discovered via an assembly attribute.
- A culture satellite, discovered via an assembly attribute and resolved lazily for `de`.
- A self-check that prints `EMBED OK` or `EMBED BROKEN`: both German lines mean embed discovery survived; the English defaults mean the trimmer dropped the resources or the discovery reflection.

## Running
```bash
dotnet run --project samples/Localization/Localization.TrimSample
```
Prints the German `main assembly:` and `satellite:` lines, then `EMBED OK`.

To exercise the real trimming / single-file scenario, publish and run the trimmed binary:
```bash
dotnet publish samples/Localization/Localization.TrimSample -c Release -r linux-x64
```
The trimmed single-file binary prints the German strings and `EMBED OK`.

## Notes
Under NativeAOT the satellite does NOT load and degrades to the in-code default — that is the point of the spike. The main-assembly catalog survives; the culture satellite is the path that breaks, which is exactly why `Localization.AotSample` avoids satellites entirely.
