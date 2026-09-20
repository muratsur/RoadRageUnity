# Known Issues

Tracked issues that are understood but deliberately not yet fixed. Each entry notes the
symptom, the cause, where the relevant code lives, and the levers to try.

## Greenwood — grass blades smear across the road edge (cosmetic)

**Symptom:** Bright green grass streaks fan across the asphalt at the road edges,
most visible at low (chase-cam) angles.

**Cause:** The grass is drawn as cutout billboard cards. At shallow camera angles the
cards foreshorten to near-flat and visually sweep across the nearer road, so it is a
rendering/asset characteristic rather than a simple position bug — pushing the grass
laterally outward reduces but does not eliminate it. Confirmed pre-existing (present
before the "Forest Floor PBR" ground fix, PR #15); it arrived with the Greenwood rework
in PR #5.

**Where:** `Assets/Scripts/RoadRageBootstrap.cs`, `BuildForest()`:
- `"Forest Grass"` scatter — `ScatterBand(1.9f, 7.2f, 20f, … ForestPlant(d, l, 0.6f, 1.3f, "Forest Grass"))`
- `"Verge Undergrowth"` scatter — `ScatterBand(1.3f, 7.4f, 13f, … ForestPlant(d, l, 0.9f, 1.7f, "Verge Undergrowth"))`
- For reference: Greenwood road half-width is `1 × RoadPath.LaneWidth = 4.5m`; shoulder
  is `2.5m`, so the road + shoulder edge is at `7.0m`.

**Levers to try (best tuned live in the Editor, not via player rebuilds):**
- Shrink the near grass card height (`0.6–1.3` → e.g. `0.3–0.6`).
- Thin the density (raise the `ScatterBand` step, e.g. `1.9` → higher = fewer).
- Push the near lateral out past the shoulder + card width (`7.2` → ~`9.5`).
- Change the grass card render mode / orientation so blades stand rather than lie flat.

## Greenwood — guardrails render as white boxes (cosmetic)

**Symptom:** The roadside guardrails render as white box/crate shapes instead of a fence.

**Cause:** Likely a wrong material assignment or a model/normalization mismatch on the
guardrail mesh. Introduced with the Greenwood rework (PR #5).

**Where:** `Assets/Scripts/RoadRageBootstrap.cs`, `BuildForest()` — the guard-rail
`ScatterBand` that calls `PlaceBiomeModelOnRoad("Synthwave", "Fence/SM_fence", railMaterial, …)`
followed by `NormalizeModelHeight(...)`. Check the `railMaterial` fallback
(`materials.TryGetValue("Hills Metal", …) ? … : materials["Forest Mountain"]`) and the
`SM_fence` model/scale.

## Deferred larger work (not a bug — scoped, coordinated change)

**Crash / revive / results settlement flow (checklist Step 7, HUD restructure).** The
confirmed negative-cash revive bug and its "also address" items are fixed (PR #6). The
larger "defer banking until the run truly finishes" restructure is intentionally left as
a coordinated change requiring `RoadRageHUD`, `ArcadeCarController`, the landing/restart
flow, and playtesting.
