# Road Rage Unity — Production Gates

**Project:** RoadRageUnity (Unity 6000.5.4f1, URP)
**Relationship to the shipped game:** the live iOS/Android product is the Godot build in
`Projects/RoadRage3D`. This project is a graphics-and-systems rebuild of that game. It is
not yet a shippable product and has never been played by anyone.
**Companion document:** `InspectionStation/GAME-PRODUCTION-PLAYBOOK.md` (§1, §3, §8, §9,
§10, §12 apply here; §7 and §11 are Unreal-specific and do not).
**Last reviewed:** 2026-08-03

---

## 0. The rule

**Prove what exists on target hardware with a real player before adding anything else.**

A change is complete only when all four are true:

- [ ] It survives a fresh `-batchmode` player build (not just the editor).
- [ ] Zero compile errors and zero new warnings.
- [ ] It is verified by a **measurement**, not a screenshot — a logged value, a frame time,
      or a recorded player action.
- [ ] Evidence is recorded: build size, biome, distance, FPS, and what was actually run.

Screenshots prove appearance only. Every visual claim made on 2026-08-03 rests on
screenshots, which is why this document exists.

---

## 1. Measured baseline (2026-08-03)

Honest state, desktop only. No device numbers exist.

| Fact | Value |
|---|---|
| Windows player build | 648 MB |
| Greenwood | **31 FPS** (RTX 5060 Ti, 1280×720) |
| Hollywood Hills | ~490 FPS |
| Canyon / Kowloon / Cyber | 180–850 FPS |
| Biomes | 10, streamed in 150 m chunks, 1800 m zones |
| Android FPS | **< 1 FPS on Helio G85 (2026-08-04)** |
| Fresh-player runs | **zero** |

Greenwood at 31 FPS on a desktop GPU is the headline defect. A mid-range phone is roughly
10–20× weaker. Until that number moves, nothing else about this project is decidable.

---

## 2. Stop-work rules

Stop feature work immediately when any of these happens:

- A change requires repeatedly adjusting values you cannot explain.
  *(Hollywood Hills cost four blind build cycles this way. Instrumenting actual world
  positions found the real bug in one.)*
- A biome renders correctly in one build and not the next with no code change between.
- Two systems appear to own the same placement or state.
- Build size increases without a stated reason.
- Frame rate drops below 30 in any biome on desktop.
- An asset enters `Resources/` without a recorded licence and source pack.

Return to the smallest reproduction — one biome, one `-startkm`, one logged value.

---

## 3. Test ladder — cheapest first

Never run a full build to test a value you can log.

1. **Compile check** — `-executeMethod` build, grep for `error CS`.
2. **Logged-value check** — add a temporary `Debug.Log` of the measured quantity
   (position, count, keyword state) and read it from the player log. This is the step
   that was skipped repeatedly on 2026-08-03.
3. **Single-biome capture** — `-biome=<name> -startkm=<km> -shot=<path>`.
4. **Sweep** — all ten biomes, grep logs for `Missing biome model`, `Missing surface`,
   `NullReference`, `Exception`.
5. **Journey check** — drive across a zone boundary and confirm the transition.
6. **Device check** — Android build on the actual phone.
7. **Fresh-player check** — someone else plays, uncoached.

---

## 4. Gate A — runs on the target device

The project cannot be called a mobile game until this passes.

- [ ] Android build produced and installed on Murat's own phone.
- [ ] Sustained **30 FPS minimum** in every biome, measured over a 60-second run.
- [ ] Greenwood specifically profiled — identify whether the 31 FPS desktop figure is
      canopy overdraw (thousands of alpha-clipped leaf cards) or something else.
      Overdraw is a bug; raw geometry cost is a tradeoff. They need different fixes.
- [ ] Build size under **300 MB** (currently 648 MB).
- [ ] Mobile quality tier verified to actually disable SSAO, drop texture resolution and
      thin vegetation — it has not been re-checked since SSAO became functional.
- [ ] No crash across a 10-minute continuous run.

**Exit evidence:** device model, per-biome FPS, build size, a screen recording.

### Gate A finding — Greenwood is overdraw-bound (measured 2026-08-03)

| Configuration | Renderers/chunk | Cutout renderers | FPS |
|---|---|---|---|
| Greenwood, canopy on | 850 | 824 | **126** |
| Greenwood, `-nocanopy` | 516 | 504 | **457** |
| Greenwood, GPU instancing + thinned near bands | 822 | 796 | **132** |
| Hollywood Hills (comparison) | 446 | 276 | ~490 |

Geometry is not the cause: Hollywood draws **more** triangles than Greenwood and runs 4x
faster. Draw calls are not the cause either — enabling GPU instancing and removing 28
renderers moved the frame rate by 5%.

The cost is **alpha-test overdraw**. The near canopy bands place 15–24 m trees at 8–20 m
lateral, so their leaf cards arch directly over the camera and fill the screen. Every
overlapping card shades full-screen pixels and alpha-testing defeats early-Z rejection, so
cost scales with layers of foliage in view, not with object count.

**Consequence:** the enclosed-canopy look is inherently expensive and near-certainly
unaffordable on a phone at this density. The lever that works is reducing *screen coverage
of foliage* — fewer near/overhead trees, or a cheaper foliage shader — not instancing,
batching or LODs.

### Gate A RESULT — FAILED on device (measured 2026-08-04)

**Device:** Motorola moto g stylus (2022) · MediaTek Helio G85 (MT6769V/CZ) · Mali-G52 ·
Android 12 · 2460x1080 · Vulkan.

| Check | Target | Actual |
|---|---|---|
| APK size | < 300 MB | **139 MB** PASS |
| Installed size | — | ~970 MB |
| Boots and renders | yes | **yes** — picker and Red Canyon both render correctly |
| Sustained FPS | >= 30 | **< 1 FPS** FAIL |

Three independent measurements agree: the on-screen counter reads `0 FPS`; SurfaceFlinger
returns no frame timestamps over a 10 s window; and the distance readout stayed at
`0.00 km` across 12 s at an indicated 83 km/h, so simulation time is barely advancing.

The build is **not playable** on this device. It is not "slow" - it is effectively frozen.

Scale check before blaming the handset: this is a budget SoC, but even a flagship an
optimistic 20x faster lands near 10-20 FPS, still short of the 30 FPS gate. The gap is
structural, not a device-class excuse.

**Also found on device only (does not reproduce on Windows):** IL2CPP managed stripping
removed collider types the code only ever creates via `AddComponent`, so the log shows
15x `Can't add component because class 'CapsuleCollider' doesn't exist` and 2x the same
for `SphereCollider`. Player and trigger colliders therefore do not exist on Android.
Fix with a `link.xml` preserve entry or a static type reference.

**Second device-only warning:** `Vulkan: Too much vertex data per render pass detected`.
The runtime-generated ribbon meshes (ground, road, terrain) are the likely source.

**If Gate A cannot pass:** the realism work from 2026-08-03 is the cause, and the choice
is to strip it back for mobile or to move this project to PC/Steam. That decision is
Murat's and should be made on these numbers, not on screenshots.

---

## 5. Gate B — the world holds together

Run one continuous 20 km drive without restarting.

- [ ] Every zone boundary produces a gateway; none is missing or duplicated.
- [ ] No visible seam, crack or lighting pop where chunks meet.
- [ ] No prop floating above or sunk below the ground at any elevation.
      *(Root cause found 2026-08-03: `NormalizeModelHeight` used absolute world Y while
      the road carries ±12 m of elevation.)*
- [ ] Traffic recycles without vanishing in view or appearing on top of the player.
- [ ] Weather transitions cleanly and the wet-road response matches the state.
- [ ] Memory does not grow across the run — chunk recycling actually frees.
- [ ] Frame time stays flat; no upward drift over 20 km.

---

## 6. Gate C — it is a game, not a drive

- [ ] Three complete runs end properly and bank score, cash and distance.
- [ ] Save survives closing and reopening the app.
- [ ] Garage: buy a vehicle, buy an upgrade, see both persist and affect handling.
- [ ] Armour visibly changes impact outcomes between the free ute and the Juggernaut.
- [ ] Daily missions roll, progress and can be claimed.
- [ ] Every biome is reachable through the picker and through the journey.
- [ ] No system requires a command-line flag to exercise.

---

## 7. Gate D — a fresh player

The gate this project has never approached.

- [ ] Hand a packaged build to someone who has not seen it.
- [ ] Do not explain the controls, the garage, or the goal.
- [ ] Record: what they did first, where they hesitated, what they misread, what made
      them react, and when they stopped.
- [ ] Ask one question afterwards: *"What were you trying to do?"*
- [ ] Rank the top three repeated problems.
- [ ] Fix those three and retest with a different player.

Do not add biomes, vehicles or effects until this has happened once.

---

## 8. Project-specific traps

### THE recurring fault: measured bounds != requested bounds

Four separate visual bugs on 2026-08-03/04 had the same root cause — code asked for an
object in a lateral band or at a height, and the thing that actually rendered occupied a
different volume. Every one of them looked like a different problem and none was fixed by
tuning the requested numbers.

| Symptom | Real cause |
|---|---|
| Mountains "floating in the sky" (Greenwood) | 90-170 m ridge meshes placed at 150-260 m lateral - one measured 320 m wide, 69 m up, 2 m from the player |
| Tenement across the road (Kowloon) | `NormalizeModelHeight` rescaled a building AFTER `EnsureOutsideRoad` ran, widening it to 47 m with its centre 12 m off the road |
| Slabs hanging over Hollywood | Terraced "bench" ribbons - unsupported horizontal planes at 14 m and 38 m |
| Neon City renders a pavement at eye level, truck invisible | `Right Neon Sidewalk` ribbon renders **17.9 m wide** for a ~7 m band, covering the carriageway |

**Diagnosis procedure — do this FIRST, not after three rounds of tuning:**
log the measured world offset and size of the suspect renderer and compare against what the
call site asked for:

```
lat  = Vector3.Dot(bounds.center - RoadPath.Center(d), RoadPath.Right(d))
y    = bounds.center.y - RoadPath.Center(d).y
size = bounds.size
```

The `RR_NEAR` probe in `LoopSelfTest` already prints exactly this. Running it once found the
Kowloon and Neon causes immediately; eyeballing screenshots and adjusting constants cost six
build cycles on Hollywood and never converged.

**Corollary:** anything that rescales a model (`NormalizeModelHeight`, `NormalizeModelSpan`)
invalidates any clearance test performed before it. `ClearRoadCorridor` sweeps each chunk
after build as a safety net, but its skip-list (names containing Sign/Bridge/Wire/Ground/
Ribbon/etc.) means ribbons are exempt — which is why the Neon sidewalk survived it.

### OPEN BUG (2026-08-04): Neon City sidewalk ribbons overlap the road

Not a camera fault - `RR_CAM` shows the chase camera correctly at `lat=-2.2` (limit 12) and
`height=5.5` throughout. The `Left/Right Neon Sidewalk` `BuildRibbon` calls in
`BuildNeonCity` produce ~18 m wide geometry for a ~7 m band. Suspect the `relative: true`
fractions being scaled by half-width twice, or `displace` inflating the bounds. Confirm by
comparing the `RR_NEAR` printed size against the lateral range passed in.



Hard-won on 2026-08-03. Each cost real time; none is obvious from documentation.

**Compiling the scripts without Unity proves nothing about method bodies.** Roslyn
skips body binding entirely when a compilation has any declaration-level error, and
compiling `Assets/Scripts` without UnityEngine produces hundreds of `CS0246`. One is
enough. Verified directly:

```csharp
class KnownBase { }
class C : KnownBase { void B() { DoesNotExist(); } }   // CS0103, caught
class C : SomeUnknownBase { void B() { DoesNotExist(); } }   // silence
```

So a stub-free `dotnet build` over the scripts reports a stable error count that is
identical whether or not the code calls methods that do not exist. Commit `ffaeb73`
shipped calling `GetBracketKey()` and `GetExposureKey()`, neither of which existed,
past exactly that check. Re-injecting both reproduces it: 722 `CS0246` / 10 `CS0103`
with the bug present, byte-identical to without it, and neither name mentioned once.

`Tools/symbolcheck.sh` covers this specific gap - it parses the project's own sources
and reports any unqualified call whose name the project never declares, diffed against
`Tools/SymbolCheck/baseline.txt`. Names only: it does not check argument counts or
types, and it is not a substitute for rung 1 of the test ladder, which is a real Unity
compile.

**Shader variant stripping.** Materials built at runtime with `new Material()` are not
scanned by URP's stripper, so `EnableKeyword` silently does nothing in a player build and
every surface falls back to its plain float values — which rendered the canyon as chrome.
`Material.IsKeywordEnabled` returns `true` either way, so it does **not** prove the variant
survived. Fix: anchor materials under `Resources/` (`ShaderVariantAnchor.cs`).

**Absolute vs road-relative ground.** The road carries elevation via `RoadPath.CenterY`.
Anything positioning against world Y instead of the road surface will sink into hills and
float over dips.

**SSAO radius is in world units.** A radius tuned for interiors (0.035) does nothing at
building scale and leaves everything looking pasted on.

**No tonemapping = plastic.** URP does not add one by default; without ACES every
highlight clips flat.

**Ribbon width.** A ribbon is two vertices across unless subdivided. A 300 m "hillside"
built as one quad is cardboard no matter how it is textured or lit.

**Asset scale.** `SM_mountains` is a 2.8 km landscape ring, not a prop. Check real
dimensions before scattering anything.

---

## 9. Evidence format

```text
Build:            (size, date)
Biome / distance:
Test:
Expected:
Actual:
Measurement:      (FPS, logged value, count — not "looks right")
Pass/Fail:
Next action:
```

---

## 10. Definition of done

A fresh player installs the build on a phone, drives from Greenwood through at least two
zone transitions at a stable frame rate, crashes into traffic and understands why it
scored, buys a truck they wanted, and starts another run without being told to.

Anything short of that is work in progress, however good the screenshots look.
