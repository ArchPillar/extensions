# Localization.WasmLibrary

A small Razor class library that ships its translation catalogs as **loose files** (static web assets), the mode a Blazor WebAssembly client can fetch over HTTP. It exists to prove that a consuming WebAssembly app gathers and merges a referenced library's catalogs alongside its own.

## What it shows

- A referenced library authoring localized strings (`Greeter`) with an English in-code default and a German catalog (`Translations/Acme.WasmGreeting.de.arb`).
- Shipping catalogs as **files**, not embedded satellites — so the build registers them as static web assets that flow to the consuming app. (For why this is preferred over satellites in WebAssembly, see [recommendations.md](../../../docs/localization/recommendations.md).)

## How it's consumed

[`Localization.WasmSample`](../Localization.WasmSample/) references this library. At build the WebAssembly app gathers this library's catalog into its own manifest; at publish it merges this library's catalog and its own into one bundle per culture. See that sample's README for how to run and verify.

This project isn't run on its own — it's a dependency of the WebAssembly sample.
