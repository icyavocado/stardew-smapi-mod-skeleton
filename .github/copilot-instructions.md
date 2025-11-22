<!-- Copilot / AI agent instructions for this repo -->
# Copilot instructions — Stardew SMAPI mod skeleton

Purpose: help AI coding agents be immediately productive modifying this SMAPI mod skeleton.

- Big picture: This repo is a minimal Stardew Valley SMAPI mod skeleton. Each mod lives under `src/<ModName>/` and includes a `manifest.json`, a `.csproj`, and C# source files. SMAPI reads `manifest.json` to locate the `EntryDll` (the compiled DLL). Example: `src/HelloWorld/manifest.json` -> `EntryDll: HelloWorld.dll`.

- Build & run (discovered from repository files):
  - Build with .NET: `dotnet build src/HelloWorld/HelloWorld.csproj -c Release`.
  - Expected DLL output: `src/HelloWorld/bin/Release/net6.0/HelloWorld.dll`.
  - To test in-game, place the mod folder (or at minimum `manifest.json` + compiled DLL) into the game's Mods directory (this repo includes an `install_directory/` placeholder to drop game files).
  - The file `stardewvalley.targets` defines a `GamePath` property used by CI/scripts; it's minimal here.

- Project-specific conventions and patterns:
  - Per-mod structure: `src/<ModName>/` contains `manifest.json`, `<ModName>.csproj`, and source files (e.g. `ModEntry.cs`).
  - Entry point: classes inherit from `StardewModdingAPI.Mod` and override `public override void Entry(IModHelper helper)` (see `src/HelloWorld/ModEntry.cs`).
  - Event wiring: use `helper.Events` to subscribe to SMAPI events; prefer instance-level event handlers and `this.Monitor.Log` for logging.
  - `manifest.json` fields are authoritative for SMAPI: `UniqueID`, `EntryDll`, `MinimumApiVersion`. Keep them synced with project names and compiled assembly name.
  - Project targets .NET 6 (`TargetFramework` = `net6.0`) as seen in `src/HelloWorld/HelloWorld.csproj`.
  - Package dependencies are declared in the mod's `.csproj` (this example references `Pathoschild.Stardew.ModBuildConfig`).

- Integration and important cross-files to check when making changes:
  - `src/<ModName>/manifest.json` — change `EntryDll` if DLL name changes.
  - `src/<ModName>/*.csproj` — add NuGet packages or change `TargetFramework` here.
  - `src/<ModName>/ModEntry.cs` — primary program flow and event subscriptions.
  - `install_directory/` — placeholder for where built artifacts or game files might be copied during local testing.

- Guidance for AI edits (concrete, repo-specific):
  - If you rename the assembly or project, update `manifest.json`'s `EntryDll` to match the compiled filename.
  - Preserve the `Mod` inheritance pattern and `Entry(IModHelper)` signature for SMAPI to load mods correctly.
  - Use `this.Monitor.Log("message", LogLevel.Debug)` for debug-level logs — this pattern is used in `ModEntry.cs`.
  - When adding third-party packages, modify the `.csproj` and run a local `dotnet build` to ensure restore and compile succeed.

- Quick references (paths in this repo):
  - `src/HelloWorld/manifest.json` — sample manifest
  - `src/HelloWorld/ModEntry.cs` — sample entry point and event usage
  - `src/HelloWorld/HelloWorld.csproj` — project file / dependencies
  - `stardewvalley.targets` — CI/game path helper

If anything here is unclear or you want me to include additional examples (packaging steps, a small script to copy the built DLL into `install_directory/`), tell me which parts to expand and I'll iterate.
