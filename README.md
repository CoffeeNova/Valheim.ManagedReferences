# Valheim.ManagedReferences

A small repo to build a NuGet package that contains reference DLLs copied from a local Valheim installation (`valheim_Data/Managed`) for use as compile-time references in Valheim/BepInEx mod projects.

## What it does
- Copies a predefined list of DLLs from a local Valheim install folder into `lib/net46/`.
- Packs them into a `.nupkg` using a `.nuspec` file.

## How to use (local dev)
1. Ensure Valheim is installed (Steam).
2. Run the sync script to copy DLLs from your local game folder into the repo.
3. Commit.
