# Valheim.ManagedReferences

Reference-only assemblies for compiling Valheim/BepInEx mods without requiring a local Valheim installation on the build machine.
The package is built by syncing DLLs from a Valheim install (`valheim_Data/Managed`) into `lib/net46` via `dotnet run tools/sync-managed.cs`.

## What’s inside

- Valheim managed assemblies copied into `lib/net46` (with optional exclusions via `ignore-managed.md`).
- A small set of extra compile-time references (currently `0Harmony.dll` and `BepInEx.dll`) synced from NuGet packages during the same step.

## How to use

1. Add the NuGet package to your mod project.
2. Reference the assemblies from the package as compile-time references.
3. Do not ship these DLLs with your mod; users must have their own Valheim + BepInEx installation.

## Versioning scheme

Example tag/version: `v1.2214.3`.
CI resolves the public Valheim version from the Steam News API and derives `<minor>` by concatenating the 2nd and 3rd version segments (e.g. `0.221.4` → `2214`).
The NuGet version is `1.<minor>.<patch>`, where `<patch>` is incremented based on the latest existing git tag matching `v1.<minor>.*`.