# Unity CLI and MCP for this project

## Batchmode builds (in this repo, ready to use)

`Assets/Editor/RoadRageCLI.cs` adds three batchmode entry points. `Tools/build.ps1`
(Windows) and `Tools/build.sh` (macOS/Linux) wrap them so a build is one command:

```powershell
.\Tools\build.ps1              # Windows player
.\Tools\build.ps1 -Target Android
.\Tools\build.ps1 -Scenes      # report what the build would ship, without building
```

They read the editor version from `ProjectSettings/ProjectVersion.txt` (currently
6000.5.4f1), exit non-zero when a build fails, and print the lines that matter:

```
RR_BUILD start target=StandaloneWindows64 scene=Assets/Scenes/Greenwood.unity
RR_BUILD result=Succeeded size=612.4MB errors=0 warnings=18 time=214s
```

Size is printed because PRODUCTION-GATES sets a 300 MB limit and the last recorded
Windows build was 648 MB.

### The build scene list is wrong

`ProjectSettings/EditorBuildSettings.asset` has exactly one enabled scene:

```
Assets/ACC_Drift_Lite/Scenes/Demo.unity
```

No such path exists in this project — the pack here is `ACC_Lite`. And
`Assets/Scenes/Greenwood.unity`, the project's own scene, is not in the list at all.

This has gone unnoticed because `RoadRageBootstrap` builds the entire game from a
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` hook, so it comes up whatever scene
ships — or none. The CLI entry points name the scene explicitly rather than trusting
that list, but the list itself is still wrong for any build made through the Editor UI.
Worth fixing in Build Settings.

## MCP

Unity's in-Editor MCP server (the one in `com.unity.ai.assistant`) is **deprecated**.
Unity's own documentation says to use the Unity CLI instead, citing faster iteration,
better stability, and the ability to target both runtime and the Editor. The CLI ships
an "MCP Mode" so existing MCP-based agents keep working.

### Setup, in order

Run from the project root (`C:\Users\Murat\Projects\RoadRageUnity`).

```powershell
# 1. Do you already have it? Unity Hub installs the CLI automatically now.
unity --version

# 2. Only if step 1 says the command is not recognised.
#    Windows ships as an MSIX / PowerShell install script; on macOS and Linux it is
#    curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | bash
#    brew, winget and apt support is announced but not shipped yet.

# 3. Add the Unity Pipeline package (com.unity.pipeline) to this project.
#    Targets the current directory or the running Editor by default.
unity pipeline install

# 4. Point Claude Code at it. This writes the client config itself, preserving
#    whatever else is already in it.
unity mcp configure claude-code
```

Prerequisite is Unity 6.0 or later; this project is on 6000.5.4f1, so that is fine.
The CLI is free and separate from Unity AI — installing it needs no Unity AI
subscription.

### Confidence

`unity.com`, `docs.unity.com` and `docs.unity3d.com` are all blocked by the network
egress proxy of the container this was written in, so none of the above was read from
Unity's documentation directly. The four commands are corroborated across several
independent search results; the Windows installer step is the weakest link, which is why
step 1 checks whether you need it at all.

Let step 4 write the config. No `.mcp.json` is committed here, and one should not be
pasted in by hand from any source, because the relay path and transport are things the
installer knows and a hand-written file would be guessing at.

## Why the CLI matters here more than MCP

PRODUCTION-GATES requires that a change "survives a fresh `-batchmode` player build (not
just the editor)" and is "verified by a measurement, not a screenshot". Gate A failed on
device at under 1 FPS. Both of those need a repeatable build and a logged number, which
is what the scripts above provide. An MCP connection to a running Editor is useful for
inspecting a live scene; it does not produce a player build or a frame time.
