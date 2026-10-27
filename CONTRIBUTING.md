# Building Story Lens

Requires Windows, the .NET 10 SDK and a Dalamud API 15 development distribution. The pinned `Dalamud.NET.Sdk/15.0.0` uses `%APPDATA%\XIVLauncher\addon\Hooks\dev` by default; set `DALAMUD_HOME` to another compatible distribution if needed.

From the standalone StoryLens repository:

```powershell
dotnet restore .\StoryLens.csproj --locked-mode
dotnet build .\StoryLens.csproj -c Release --no-restore
dotnet restore .\tests\StoryLens.Tests.csproj --locked-mode
dotnet test .\tests\StoryLens.Tests.csproj -c Release --no-restore
```

The DLL is `bin/Release/StoryLens.dll`; the package is `bin/Release/StoryLens/latest.zip`. For local testing, register the DLL as a Dev Plugin Location in Dalamud. Tests cover managed logic; verify game-dependent changes in-game as well.

Read [CONTRACT.md](CONTRACT.md) for behavior, safeguards and validation history, and [AGENTS.md](AGENTS.md) for development conventions. The canonical development copy is `projects/active/StoryLens` in the sibling Dalamud workspace; keep shared source and documentation synchronized with this repository.

Releases are listed in [Matuno/DalamudPlugins](https://github.com/Matuno/DalamudPlugins). Follow that repository's update instructions to publish a tested, versioned ZIP and its matching manifest together. Pushing source here does not update the plugin listing automatically.
