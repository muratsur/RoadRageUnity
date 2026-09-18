# RoadRageUnity — Graphics & Environment Upgrade Plan

**Question answered:** *"How do I upgrade the graphics assets and environment to 3D / near-realistic?"*
**Companion to:** `PRODUCTION-GATES.md` (measurement rules, Gate A–D, §8 traps)
**Author:** Arena agent review · **Date:** 2026-09-18 · **Commit reviewed:** `b8bdbe1`

---

## 0. The short answer

**The project is already 3D, and it is already PBR.** There are 685 mesh files and 465 textures
under `Assets/`, a URP HDR pipeline with MSAA 4×, SSAO, ACES/Neutral tonemapping, a
three-layer splat terrain shader and HDRP-authored NYC building packs converted with
`Tools/HdrpToUrp`. Nothing needs to be "upgraded to 3D".

What is missing is **three specific things**, in this order:

1. **An environment lighting model.** Right now ambient light is three hand-typed colours
   (now `RoadRageBootstrap.cs:1419`), there is
   no skybox asset, no baked GI, no probe volumes (`m_LightProbeSystem: 0` in
   `Assets/Settings/RoadRageURP.asset`), no SSR, and **the reflection probes are dead code**
   (§2.2). PBR only reads as "real" when something *light-shaped* is around it. This is why the
   assets look toy-like even though they are not.
2. **A device that can afford it.** Gate A measured **< 1 FPS on the reference phone** and
   **31 FPS desktop** with the content that exists *today*. Realistic content costs 5–20× more
   pixel and lighting work. Photoreal-on-a-Helio-G85 is not a tuning problem.
3. **An authored (baked) world.** The world is generated at runtime by 10,819 lines of C#.
   Every technique that makes an environment look real — lightmaps, Adaptive Probe Volumes, LOD
   chains, occlusion data, decal atlases, texture streaming — is an *authoring-time* technique.
   You cannot buy your way past this; it is an architecture decision.

**The realistic ceiling for this repo as it stands is "high-fidelity stylized"** (think
*Need for Speed: Most Wanted 2012*). Getting past that needs §3 decision 1 and §5 stage 1.5.

**Do not buy photoreal asset packs yet.** Swapping assets into a world with no GI, no skybox,
no reflections and a deliberate 35% desaturation pass will look *worse*, not better — the
sharper the asset, the more obviously it is "pasted on". §5 stage 0 is free and moves the look
further than any purchase.

---

## 1. What is actually in the project (measured, not claimed)

| Area | Reality |
|---|---|
| Engine | Unity `6000.5.4f1`, URP `17.6.0` (HDRP `17.5.0` is *also* in `Packages/manifest.json` but unused) |
| Render path | **Forward** (`m_RenderingMode: 0`), 4 additional lights per object, HDR on, MSAA 4×, `m_RenderScale: 1` |
| Shadows | 160 m, 4 cascades, 2048 main-light map (`RoadRageURP.asset`) |
| SSAO | On, intensity 1.6, **radius 0.55 (world units — 55 cm, i.e. interior scale; §8 of the gates warns about this)** |
| Depth/opaque texture | **Both off** (`m_RequireDepthTexture: 0`, `m_RequireOpaqueTexture: 0`) → no SSR, no soft particles, no depth-based effects |
| Post FX | Bloom **disabled** (`RoadRageBootstrap.cs:1387-1389`), MotionBlur 0.18, ColorAdjustments (contrast 14), Vignette 0.15, Tonemap Neutral |
| Sky | No skybox material. `EnsureGlobalHorizonSky` (`:6148`) reparents mountain **meshes** and card cumulus clouds to a camera-following transform (`GlobalHorizonFollower`, `:10802`) |
| Ambient | Trilight, three colours written per frame from `BiomeMood` |
| World | 10 biomes, 150 m streamed chunks, 5400 m zones, all geometry built by `BuildRibbon` (`:2026`) at runtime. Ground is strips, not Unity Terrain |
| Ground shading | Custom `Assets/Shaders/TerrainSplat.shader` — 3 albedo + 3 normal layers, weights in vertex colour. Genuinely good, but 3 layers is the ceiling |
| Assets | 267 FBX in `Resources/Biomes`, 95 in `Resources/Buildings`, 19 in `Resources/Vehicles`, 15 in `Resources/Props` |
| Vehicle art | `PolygonStreetRacer` + `SK_Veh_Preset_*` — stylized low-poly kits. This is the object on screen 100% of the time |
| Buildings | **NYC-Like City Buildings (PBR)** — the best asset in the repo. `NYCVariants` stack ~5.1 sections of 18–25k tris ⇒ ~100k tris per tower |
| Texture import | **1052 files capped at 512 px**, 138 at 1024, 544 at 2048. Road/ground textures import at `aniso: 1` |
| LODs | One LODGroup in the whole project: the MidBlock skyline impostor box (`:3143`) |
| Pedestrians / characters | **None** (no pedestrian, crowd or character content in `Assets/`) |
| Documented perf | Desktop 648 MB build; **Android < 1 FPS** on Helio G85 (2026-08-04) |

---

## 2. The four things blocking "realistic"

### 2.1 There is no environment lighting model
Ambient = three colours. Sky = mesh cards. GI = none. Probe volumes = off. Depth texture = off.
Reflections = see below. This is the difference between "materials lit by a lamp" and
"materials that live in a place". **Highest realism-per-hour item in the whole document.**

### 2.2 Reflection probes are wired but inert — verified
*(State as of commit `b8bdbe1`, before the §9 change; line numbers are that commit's.)*
- `private ReflectionProbe reflectionProbe;` declared at `:106` — and never assigned.
- Six call sites configured it: `:4720`, `:4782`, `:4971`, `:5958` (size, blendDistance,
  resolution 256, `RefreshMode.EveryFrame`, intensity 0.90–1.25).
- `BuildReflectionProbe()` at `:1341` had an **empty body**.
- Nothing ever called `AddComponent<ReflectionProbe>()`, and `BuildReflectionProbe()` was never
  called either — the method had no call site at all.
- `:7376` attached `ReflectionProbeDriver` to the camera and passed it that null probe, so its
  `LateUpdate` returned immediately every frame.
- Meanwhile the road and shoulders ask for probe reflections:
  `EnableProbeReflections(...)` sets `ReflectionProbeUsage.Simple` on the asphalt and both
  shoulders (`:1921`, `:1973-1974`; now `:2038`, `:2090-2091`).

**Result: wet asphalt reflecting neon — the single strongest "this is real" cue in a night
city, and a thing this codebase clearly intends — does not exist in the running game.** A
1024 px probe, re-rendered every N frames with box projection, is a few hours of work.

### 2.3 Art direction is actively fighting realism (deliberately)
These were reasonable calls made to fix specific defects. Collectively they cap realism:
- `SurfaceDesaturation = 0.35f` (`:570`) applied to **every** non-signal material (`:587`).
- Bloom forced off globally (`:1387-1389`) even though `BiomeMood.BloomIntensity` and
  `BloomThreshold` exist and are lerped per zone (`:4122`) — the plumbing is built and unused.
- Tonemap Neutral + contrast 14 + `GradeSaturation`; `MinAmbientLuma` floors
  (`:1608`) to stop black facades.
- `IsSignalColour` (`:575`) exempts neon/signs/brake lights — correctly, but it means the rest
  of the world is *meant* to read flat.

### 2.4 Texture and geometry budget is capped below "close-up realistic"
512 px on 1052 textures is fine at 60 m; it is mud at 3 m, and a driving game puts kerbs,
asphalt, guardrails and vehicle bodywork at 3 m constantly. `aniso: 1` on road surfaces is a
visible aliasing smear at speed. And with no authoring pass there are no LOD chains, so the
choice is "full detail always" or "impostor box".

---

## 3. Decision 1 — pick the target platform (everything else depends on it)

> **DECIDED 2026-09-18: option B — stay mobile, target stylized-real.**
> The HDRP evaluation is recorded as *deferred and scoped to a PC-only experiment*, because
> **HDRP cannot ship on Android or iOS.** Unity does not support HDRP on mobile build targets;
> HDRP requires a compute-capable device and a desktop/console graphics API (DX11/12, Metal,
> Vulkan on desktop), and building to Android/iOS is blocked by an explicit build-target check
> inside HDRP. The community package `alelievr/HDRP-Mobile` exists precisely because it patches
> that check out with an IL post-processor, and its own README says Unity does not support these
> targets, to expect bugs, and that Vulkan-only is mandatory. So:
>
> - **Mobile path → URP, stylized-real.** Everything in §5 stage 0 works here.
> - **HDRP → a separate PC/Steam experiment**, and only if a PC product is actually wanted.
>   Running an HDRP spike on this branch would consume the same months and produce a build that
>   cannot be installed on Murat's phone, i.e. it cannot pass Gate A by construction.

Gate A already failed; this is a scope decision, not a graphics decision.

| Option | What it means | Graphics ceiling | Cost |
|---|---|---|---|
| **A. PC / Steam (recommended if "realistic" is the goal)** | Drop Android. Target the RTX 5060 Ti-class machine already used for measurement | Photoreal-adjacent (URP+) or genuinely photoreal (HDRP) | Re-scope the product; drop touch/adaptive perf; re-baseline everything |
| **B. Mobile, stylized-real** | Keep the phone. Chase *Asphalt 9 / NFS 2012* — high-fidelity **stylized** | "Looks great, obviously a game" | Cheapest. Stage 0 + bloom + reflections + better vehicles. Same assets, mostly |
| **C. Two tiers** | PC photoreal + mobile stripped, one codebase | PC: photoreal. Mobile: stylized | Highest. Nothing here is currently measured per-tier, and the mobile tier has never been re-verified since SSAO became functional |

**Photorealism and the current target device are mutually exclusive.** That is the finding, and
it is Murat's call to make (§10 of the gates says the same about the realism work of 2026-08-03).

### Decision 2 — URP or HDRP

HDRP `17.5.0` **is already a project dependency**, and `Tools/HdrpToUrp/convert.py` exists, so
this project came *from* HDRP. For "close to realistic" on PC, HDRP is the honest answer:
physical sky + volumetric clouds/fog, SSR and ray-traced reflections, contact shadows, area
lights, decals, a real tonemapping stack.

The cost is not a checkbox: ~50 `Shader.Find` sites, 8 `new Material(` sites, a port of
`TerrainSplat.shader`, a rewrite of the `Volume`/`BiomeMood` post stack, and every biome
re-tuned. It also guarantees the mobile target dies, and it re-raises the two
variant-stripping traps already documented in gates §8 (runtime-created materials are not
scanned by the stripper).

**URP can reach "excellent stylized-real". URP cannot reach photoreal.** HDRP can, at the cost
of a pipeline migration before any new content is added.

---

## 4. Decision 3 — the architectural one (the real cost of realism)

`RoadRageBootstrap.cs` builds the world at runtime, every session. Lightmaps, APV, occlusion
culling data, LOD chains, decal atlases and texture streaming are all **bake-time** artefacts.

So there are two futures:

- **Runtime forever:** keep procedural generation, accept no GI. Ceiling = stylized-real with
  the best realtime lighting you can afford. Cheap, flexible, and the biomes stay infinite.
- **Bake the world:** add an editor step that generates representative chunks, saves them as
  prefabs/scenes with lightmaps + APV + LODs, and streams *author-able* data at runtime. This
  unlocks real GI, real LODs and real occlusion. It is the single largest change in this
  document, and it is the prerequisite for genuinely photoreal environments.

A middle path exists and is probably right: **keep runtime generation for the road corridor
(which the player never sees twice) and bake the set-dressing around it** (building blocks, hero
props, signage clusters) as prefabs with baked lighting.

---

## 5. The upgrade ladder — ordered by realism gained per hour spent

### Stage 0 — make the existing assets look real *first* (days, no purchases)

| # | Change | Where | Why it matters |
|---|---|---|---|
| S0.1 | **Create the reflection probe.** 1024 px, box projection, `RefreshMode` on an interval via `ReflectionProbeDriver` (already written, already attached) | `RoadRageBootstrap.cs:106`, `:1341`, `:7376` | Wet road + neon + car paint. The intent is already there; only the object is missing |
| S0.2 | **Turn bloom on** with per-mood `BloomIntensity`/`BloomThreshold` (fields + zone lerp already exist) | `:1387-1389`, `:1527` onward | Emissive signage reads as *light* rather than a bright texture. Biggest single night-city cue |
| S0.3 | **Require depth + opaque textures** | `Assets/Settings/RoadRageURP.asset`: `m_RequireDepthTexture: 1`, `m_RequireOpaqueTexture: 1` | Unlocks SSR later, soft particles, better SSAO |
| S0.4 | **Switch to Forward+** (`m_RenderingMode: 2`) and enable the shadow resolution tiers | same file | Removes the 4-lights-per-object ceiling — a neon street currently cannot have 20 signs lit |
| S0.5 | **SSAO radius to building scale** (1.5–3 m, not 0.55) and raise samples or switch to the higher-quality source | `Assets/Settings/RoadRageRenderer.asset` | Gates §8 says it explicitly: radius is in world units; the current value does nothing at building scale |
| S0.6 | **Road surfaces: `aniso` 8–16, 2048 import, and a wetness curve instead of a flat darken** | texture `.meta` files; `ApplyRoadWetness` `:1323` | The road is the most-looked-at surface in the game. Currently the least flattering treatment |
| S0.7 | **Scope the desaturation per biome** instead of a global 0.35 | `:570`, `:587`, `BiomeMood` | Let Greenwood/Hollywood/Canyon keep their colour while the neon biomes stay neutralised |

**Do S0.1 and S0.2 before touching any asset.** Both are already half-built in the code, both
are reversible, and both are measurable in the gates' evidence format.

### Stage 1 — environment lighting (weeks)

- **S1.1 HDRI skybox + `ambientMode = Skybox`** for daylight biomes (keep Trilight only for
  enclosed biomes: Sewer Tunnel, Ice Station). Ambient from an image, not three floats.
- **S1.2 Screen Space Reflections** (PC tier only) on top of the probe from S0.1.
- **S1.3 Replace the `FogMode.ExponentialSquared` solid-colour fog** (`:1348`) with height/
  analytic fog and a real colour gradient; volumetric on the HDRP path. Solid fog at 0.0055 is
  what kills depth cues and flattens distant geometry.
- **S1.4 Replace the sky construction.** Card cumulus clusters and camera-parented mountain
  meshes (`:6148`-`:6280`) are the tell that reads "PS2 backdrop". Either an HDRI + cloud
  layer, or HDRP volumetric clouds, or real distant terrain.
- **S1.5 Bake lighting** (see §4) — or consciously accept no GI and stop expecting photoreal.
- **S1.6 Time-of-day variants** — the cheapest realism multiplier that exists: same geometry,
  new sun angle and sky. The mood system already supports it.

### Stage 2 — content (months)

- **S2.1 Vehicles first.** The car fills a third of the frame, always. Requirement list: real
  PBR bodywork, separate glass/interior/lights/tyre materials, clearcoat, working emissive
  lamps, correct wheel pivots, damage-ready panels. Replacing 19 stylized stubs is worth more
  than replacing every building in the game.
- **S2.2 Hero street props:** kerbs, guardrails, bollards, street furniture, traffic signals,
  signage — authored as trimsheet kits with a decal pass, not one material per prop.
- **S2.3 Build the city biomes on the NYC PBR set you already own** and retire the stylized
  kits from Manhattan / Neon City / Hong Kong. Add window emissive masks and a facade atlas.
- **S2.4 Vegetation:** atlas + impostor pipeline. Gate A's finding is the governing constraint
  here — the cost is *screen coverage of foliage*, not object count, so thinning near bands and
  impostoring distance beats instancing.
- **S2.5 People.** There are none. Even crude distant pedestrians and a crowd at the start/finish
  change the read from "tech demo" to "place".
- **S2.6 Per-biome HDRI lighting sets** (see S1.6).

### Stage 3 — presentation polish

Per-biome grading LUTs (instead of contrast 14), lens dirt and screen-space reflections on the
windscreen, DoF in menus/garage, camera FoV and roll tuning, headlight cookies, particle lights,
tyre smoke shading. Cheap, and it is what separates "asset pack" from "shipped game".

---

## 6. Where to get assets — and the three repo-specific traps

**Sources:** Poly Haven (CC0 HDRI + 4K PBR), ambientCG (CC0 PBR), Unity Asset Store and Fab
(paid; note Megascans is *no longer* free for non-Unreal engines since 2025 — verify the licence
per asset, never assume), Sketchfab (CC-BY / paid). Prefer a small number of coherent packs over
a large number of mismatched ones; art-direction coherence matters more than texel density.

**Trap 1 — the licence rule.** Gates §2 makes importing an asset into `Resources/` without a
recorded licence a **stop-work violation**. Add an `Assets/Resources/ASSET-LICENSES.md`
(source URL, licence, date, author) as part of the first import commit, not later.

**Trap 2 — assets load by *path string*, not GUID, and fail silently.**
`BiomeTexture` (`:666`) and `BiomeModel` (`:2259`) resolve
`Resources.Load<...>($"Biomes/{pack}/Meshes/{resourceName}")`, falling back to flat colour with
only a `Debug.LogWarning("Missing biome model")`. The entire `DedupedBiomeTextures` table
(`:606`) exists *because* of this failure mode. **Every asset move or rename must update these
call sites**, or the world silently renders untextured. Script this check — it is mechanical and
it will bite on the first bulk import.

**Trap 3 — build size.** 648 MB Windows build today, and **everything under `Resources/` ships
whether referenced or not**. A single realistic environment set is 10–50× the texture bytes of
the current library. Photoreal content *requires* migrating off `Resources/` to Addressables
(PC) with LZ4 bundles, plus per-platform texture import overrides instead of one global 512 px
cap.

---

## 7. Measurement — fix the numbers before spending money

The gates' own documents disagree:

- §1 baseline: **Greenwood 31 FPS** on RTX 5060 Ti at 1280×720.
- §4 Gate A finding: **Greenwood, canopy on, 126 FPS**; `-nocanopy` 457 FPS.

Both are labelled as measured. Before any realism investment, re-run that capture and settle it
— the two figures imply completely different headroom, and the entire "can this be photoreal"
question turns on which is true.

**Acceptance test for every stage in §5:**

```text
Test:        fixed shot list, one build, one biome, one quality tier
Measurement: frame time (ms, p95 not mean) · VRAM · draw calls · triangles · build size
Source:      logged values, not screenshots (gates §0)
Evidence:    gates §9 format, recorded in PRODUCTION-GATES.md
Human check: show it to someone who has not seen it and ask "does this look real?"
             — "realistic" is a human judgement and no log answers it
```

Also worth doing at Stage 0: re-verify the **mobile tier actually strips what it claims**
(Gate A says it has not been checked since SSAO became functional).

---

## 8. Recommended sequence

1. **Decide §3 decision 1** (target). Nothing else is decidable until then.
2. **Stage 0** — one week, no purchases, reverses deliberate anti-real choices, measurable.
3. **Re-measure Greenwood** (§7) and resolve the 31 vs 126 FPS discrepancy.
4. **Decide §4** (runtime-forever vs bake-the-world) — this sets the ceiling.
5. **Migrate off `Resources/`** to Addressables before the first large asset import.
6. **Then** buy and import content, in the §5 stage 2 order: vehicles → street props →
   buildings → vegetation → people.
7. **Stage 3** polish last; it interacts with every tuning value above it.

If the answer to §3 decision 1 is "stay on mobile", then the honest target is **stylized-real**
(Gate B of this document), and the photoreal ambition should be scoped as a separate PC/Steam
product rather than a quality setting.

---

## 9. Stage 0 — IMPLEMENTED 2026-09-18

Committed on `arena/01a0b63a-roadrageunity` as `45bd8c1`.

```text
Build:            not built - no Unity install in the authoring environment
Test:             SymbolCheck (CI), the project's own floor
Expected:         0 undeclared calls, 0 unknown members, 0 missing Resources references
Actual:           PASS - "Verify the checker still detects a planted bug" and
                  "Check the project for undefined methods" both succeeded
                  (run 35393018001, https://github.com/muratsur/RoadRageUnity/actions/runs/35393018001)
Measurement:      CI conclusion, not a frame time
Pass/Fail:        PARTIAL - the static floor passes; rungs 1-3 of the ladder have not run
Next action:      Unity compile, then the -nobloom / -noreflections A/B on device
```

**Still not compiled in Unity and not measured on any device.** SymbolCheck is names only: it
does not check argument counts or types, and it is explicitly not a substitute for a real Unity
compile (`PRODUCTION-GATES.md` §8). Treat the visual changes below as "written, unverified" until
someone has opened the project and run it. This change does not get an exemption for being
well-intentioned.

### What changed

| # | Change | Site |
|---|---|---|
| 1 | **The reflection probe now exists.** `BuildReflectionProbe()` creates it (Realtime + `ViaScripting`), called from `BuildLighting` after `ApplyPlatformQuality` so the tier is settled and before `BuildCamera`, which parents it to the chase camera and attaches the driver | `RoadRageBootstrap.cs:1360` (was an empty body) |
| 2 | Six dead per-biome probe blocks collapsed into one helper, `TuneReflectionProbe(intensity, size, blendDistance)`. Per-biome size and intensity are preserved; `EveryFrame` is gone from all of them | `:1407`, `:4846`, `:4901`, `:5083`, `:6066` |
| 3 | **`QualitySettings.realtimeReflectionProbes` is now forced on.** Quality level 0 — this project's Android default — ships with it **off**, so the probe would have rendered nothing on the exact platform that matters, silently | `:1381` (flag `:217`) |
| 4 | **Capture schedule is tier-aware** (`ReflectionProbeDriver`): rich tier 4 captures/second, 256 px; low tier recaptures only after 40 m of travel, 64 px. Also logged | `:8328` |
| 5 | **Bloom is on**, driven by the existing `BiomeMood.BloomIntensity`/`BloomThreshold`, with a branch default (0.55 rich / 0.28 low) and a `-nobloom` switch. Held in `zoneBloom` and re-applied per frame in `BlendZoneLighting` | `:1471`, `ApplyBloom` `:1515`, per-frame call `:4237` |
| 6 | **SSAO radius 0.55 → 2** (metres). The old value was 55 cm: interior scale, doing nothing at building scale — exactly the trap `PRODUCTION-GATES.md` §8 documents | `Assets/Settings/RoadRageRenderer.asset` |
| 7 | **Anisotropic filtering 1 → 4** on the 18 highest-coverage surface textures (carriageway, shoulders, city pavements, all three splat layers) | 18 `.meta` files |
| 8 | **Mobile texture cap 512 → 1024 on those same 18 textures only.** 464 of the 465 textures in `Resources/` carry an Android/iOS override pinning them to 512 px; the carriageway was one of them. Everything else keeps the 512 cap deliberately — see the memory note below | 18 `.meta` files |

### How to measure it (gates §9 format)

```text
# A/B the two new effects independently, in the same build:
RoadRageUnity.exe -biome=NEON%20CITY -startkm=10            # both on
RoadRageUnity.exe -biome=NEON%20CITY -startkm=10 -nobloom
RoadRageUnity.exe -biome=NEON%20CITY -startkm=10 -noreflections
grep RR_REFLECT <player log>     # probe built at N px, capture heartbeat, tier
grep RR_BLOOM   <player log>     # (add one if you want the applied values logged)
```

Expected: a visible change in the night biomes, and a measurable cost. If the frame time does not
move at all, the probe is still inert — check `RR_REFLECT probe built` against
`RR_REFLECT capture 1` and confirm the captures are actually happening.

Texture memory added by change 8: 1024×1024 ASTC 6×6 is ~1 MB per texture plus mips, so roughly
**+19 MB** across the 18 textures. That is small next to the ~970 MB installed size, but it is a
number to watch, and reverting is a two-line edit per file.

### Deliberately NOT changed, and why

- **`m_RequireOpaqueTexture` / `m_RequireDepthTexture`** — the opaque-texture copy is a
  whole-frame cost and its payoff (SSR) is a PC technique. SSAO already gets depth through its
  depth-normals prepass, so nothing here needs it. Revisit only on a PC branch.
- **Forward+** — it lifts the 4-lights-per-object ceiling, which is a real limitation in Neon
  City, but it is a measured A/B on the target device, not something to flip blind on a platform
  that is not making frame rate yet.
- **MSAA, SSAO-per-tier, shadow settings** — see §10. These are project-settings decisions with a
  large frame cost and they need numbers, not opinions.

---

## 10. New mobile-tier findings (2026-09-18) — these need decisions

Discovered while auditing `ProjectSettings/QualitySettings.asset` for the probe work. Each of
these is checkable in the file today.

| Finding | Evidence | Consequence |
|---|---|---|
| **The mobile tier renders no shadows at all.** Quality level 0 ("Very Low") has `shadows: 0` (Disable). `ApplyPlatformQuality()` sets `shadowDistance` and `shadowCascades` on the low tier but never sets `QualitySettings.shadows` — only the rich branch does | `QualitySettings.asset` level 0; `ApplyPlatformQuality` `:1307` (rich branch) | The log line says *"70m 1-cascade shadows"*; the actual setting is **shadows off**. A shadowless scene is a large part of "flat and fake" on device, and the log is actively misleading about it |
| **Realtime reflection probes were off on the mobile tier** | level 0 `realtimeReflectionProbes: 0` | Fixed in §9 #3 — this is why the probe work needed to touch QualitySettings |
| **MSAA 4× applies to every tier** | `RoadRageURP.asset` `m_MSAA: 4`, and all four quality levels point at the same URP asset | 4× MSAA on a Mali-G52 at 2460×1080 is a very large per-pixel cost in a scene already diagnosed as fill-bound. This is a prime suspect for Gate A's < 1 FPS and it is a one-line A/B |
| **SSAO is on for every tier** | single renderer asset, `m_Active: 1`, and no per-level renderer override exists on any quality level | 12 samples at 1.6 intensity on a fill-bound phone. Gates §4 already flags that the mobile tier has never been re-verified since SSAO became functional |
| **The Brooklyn pass force-raises the quality level to Ultra** | `QualitySettings.SetQualityLevel(3, true)` `:4850` and `:5005` — on every platform | On a phone, entering that biome switches to the Ultra tier. Harmony with the point above, not with the target device |
| **Texture caps are global, not per-surface** | 464 of 465 `Resources/` textures pinned to 512 px on Android/iOS | Correct as a memory decision. §9 #8 makes exactly three exceptions where screen coverage justifies it. Further exceptions should be argued the same way |

**Suggested measurement, cheapest first:** one Android build, four runs of the same 60-second
capture — MSAA 4 → 2 → 0, and SSAO on → off — with frame time logged. That is one build and
tells you whether Gate A is reachable at all on this content before any asset work starts.

---

## Appendix — exact code sites referenced

Line numbers are current as of the §9 change. References in §1–§2 that describe the *audit*
(i.e. the pre-change state) are labelled as commit `b8bdbe1` numbers and are deliberately not
updated — they are the evidence for what was wrong.

| File | Line | What |
|---|---|---|
| `Assets/Scripts/RoadRageBootstrap.cs` | 106 | `reflectionProbe` field — declared, never assigned until §9 |
| | 217-218 | `-nobloom` / `-noreflections` verification flags parsed in `Awake` |
| | 576, 593 | `SurfaceDesaturation = 0.35f`, applied to all non-signal materials |
| | 620 / 673 | `DedupedBiomeTextures` table / `BiomeTexture` — path-string loads with silent fallback |
| | 1307 | `ApplyPlatformQuality` rich branch — the only place `QualitySettings.shadows` is set |
| | 1326 | `ApplyRoadWetness` — smoothness lerp + flat albedo darken |
| | 1360 / 1445 | `BuildReflectionProbe` definition / its call from `BuildLighting` |
| | 1381 | `QualitySettings.realtimeReflectionProbes` forced on (level 0 has it off) |
| | 1407 | `TuneReflectionProbe` — replaces the six dead per-biome probe blocks |
| | 1418-1419 | `FogMode.ExponentialSquared` fog + `AmbientMode.Trilight` — the *entire* environment model |
| | 1471 / 1515 / 4237 | `zoneBloom` handle / `ApplyBloom` / its per-frame call in `BlendZoneLighting` |
| | 1611, 1721 | `BiomeMood` tables and `Neutralize` |
| | 1667 | `DefaultBloomIntensity` and friends |
| | 2038, 2090-2091 / 2122 | `EnableProbeReflections` call sites / definition |
| | 2143 | `BuildRibbon` — all ground, road and pavement geometry |
| | 2374 | `BiomeModel` — path-string load, `Missing biome model` warning |
| | 3253 | `AddSkylineImpostor` — the only LODGroup in the project |
| | 3694-3697 | `BloomDisabled` / `ReflectionsEnabled` runtime switches |
| | 4850, 5005 | `QualitySettings.SetQualityLevel(3, true)` — the city passes force the Ultra tier |
| | 7479 | `BuildCamera` parents the probe to the chase camera and attaches the driver |
| | 8328 | `ReflectionProbeDriver` — tier-aware capture schedule |
| `Assets/Settings/RoadRageURP.asset` | `m_RenderingMode: 0`, `m_RequireDepthTexture: 0`, `m_RequireOpaqueTexture: 0`, `m_LightProbeSystem: 0`, `m_MSAA: 4`, `m_GPUResidentDrawerMode: 0` | Pipeline settings. MSAA 4 applies to every tier |
| `Assets/Settings/RoadRageRenderer.asset` | SSAO `Radius: 2` (was 0.55), `Intensity: 1.6`, `SampleCount: 12`, `m_Active: 1` | SSAO applies to every tier — there is no per-level renderer |
| `ProjectSettings/QualitySettings.asset` | level 0 "Very Low" `shadows: 0`, `realtimeReflectionProbes: 0`; `m_PerPlatformDefaultQuality: Android: 0` | The mobile tier's real settings — see §10 |
| `Assets/Shaders/TerrainSplat.shader` | 3-layer ground blend | Three layers is the shading ceiling |
| `Packages/manifest.json` | `com.unity.render-pipelines.high-definition: 17.5.0` | HDRP is installed but cannot target Android/iOS |
| `Tools/HdrpToUrp/convert.py` | — | Evidence of a prior HDRP→URP migration |
