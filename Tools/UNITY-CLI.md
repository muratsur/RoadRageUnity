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

From Unity's documentation, the current path is roughly:

- Unity 6.0 LTS or later (this project is on 6000.5.4f1, so that is satisfied).
- Install the Unity CLI. It is free and separate from Unity AI — installing it does not
  require a Unity AI subscription.
- Add the Unity Pipeline package to the project, then configure the client:
  `unity pipeline install` followed by `unity mcp configure claude-code`.
  That command updates the client's Unity entry while preserving its other config, so it
  is preferable to hand-writing `.mcp.json`.

**I could not verify the exact installer command.** `unity.com` and `docs.unity3d.com`
are both blocked by this container's network egress proxy, so the commands above come
from search result summaries rather than from the documentation itself. Run
`unity mcp configure claude-code` from the project root and let it write the client
config — do not copy a hand-made `.mcp.json` from anywhere, including from me, because
the relay path and transport are things the installer knows and I do not.

No `.mcp.json` has been added to this repo for that reason.

## Why the CLI matters here more than MCP

PRODUCTION-GATES requires that a change "survives a fresh `-batchmode` player build (not
just the editor)" and is "verified by a measurement, not a screenshot". Gate A failed on
device at under 1 FPS. Both of those need a repeatable build and a logged number, which
is what the scripts above provide. An MCP connection to a running Editor is useful for
inspecting a live scene; it does not produce a player build or a frame time.
