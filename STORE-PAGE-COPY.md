# Steam Store Page — draft copy

**Status:** draft for review. Everything below describes features that exist in the build
today (2026-08-04) — nothing here is aspirational. If a feature is cut, cut the line.

---

## Name candidates

**Verified on Steam 2026-08-04 — there is an exact-name incumbent:**

| App | Title |
|---|---|
| 499210 | **Road Rage** (motorcycle combat, published, has reviews) |
| 884610 | Road Rage Royale |
| 1921330 | Echo Wars - Road Rage |
| 300380 | Road Redemption (uses "road rage" in its description) |

The mobile game is **Road Rage: Endless Chase**. That full phrase is unique on Steam, so
existing players searching it would find you — but anyone searching "Road Rage" cold lands
on app 499210, which outranks a zero-review release on both relevance and popularity.

Ranked options:

1. **RAMLINE** — distinctive, no collisions, already yours (the prefix in
   `com.ramline.roadrage`). Pair it with "from the creator of Road Rage: Endless Chase" in
   the description to capture the existing mobile audience without fighting for the term.
2. **ROAD RAGE: ENFORCER** — keeps the brand, differentiates from 499210, and "Enforcer" is
   already one of your trucks. Still competes for the crowded root term.
3. **ROAD RAGE: ENDLESS CHASE** — direct continuity with mobile. Safest for existing
   players, weakest for new discovery.

Recommendation: **option 1**. The Steam build has diverged from the mobile game anyway —
violator judging, the biome journey and the truck garage are not in Endless Chase — so it
is closer to a successor than a port, and a distinct name reflects that.

---

## Short description (Steam limit: 300 characters)

> You are the last consequence on a lawless highway. Drive a reinforced truck and ram the
> drivers who deserve it — the weavers, the speeders, the wrong-way idiots. Hit an innocent
> and you pay for it. Ten biomes, one continuous road, and nothing behind you but wreckage.

*(281 characters.)*

---

## About this game

**THE ROAD HAS NO POLICE. IT HAS YOU.**

Ramline is an arcade driving game about judgement at speed. Traffic breaks the law in front
of you — weaving across lanes, running the wrong way, tailgating, blowing past everyone at
double the limit. You decide who gets hit.

Get it right and you bank the takedown, build the combo and walk away with their money.
Get it wrong and you have just rammed a family car at 120 km/h, and the game will tell you
so.

**ONE ROAD, TEN WORLDS**

There are no levels and no loading screens. The highway streams continuously beneath you
and the world changes as you drive — pine forest gives way to a cliffside suburb, an
underpass drops you into a neon market street, a canyon opens into snowfield. Ten distinct
biomes, each with its own road profile, weather and light.

**PICK YOUR WEAPON**

Ten vehicles, from a free rusted pickup to an armoured semi that barely notices what it
hits. Armour is not a stat on a card — it decides how much speed an impact costs you and
how far you get shoved. Take the fast, fragile bike if you would rather dodge than ram.

**EVERYTHING HAS WEIGHT**

Rain slicks the asphalt and the neon bleeds across it. Snow flattens the light. Damage
accumulates until your run ends and you bank what you earned. Then you spend it, and go
again.

---

## Feature bullets (for the short list Steam shows)

- Ten streamed biomes on one continuous, never-repeating road
- Judge and ram four kinds of traffic offender — hit an innocent and pay for it
- Ten vehicles from pickup to armoured semi, with upgrades that change how impacts feel
- Dynamic weather: rain, storm and snow, each altering grip, light and visibility
- Combo scoring, daily missions and a garage economy

---

## Tags — SUBMITTED via Tag Wizard 2026-08-04

| Category | Tags |
|---|---|
| Top-level genres | Racing, Action |
| Genres | Arcade |
| Sub-genres | Combat Racing, Runner |
| Visuals & Viewpoint | 3D, Third Person, Stylized |
| Themes & Moods | Racing, Atmospheric, Nature, Futuristic |
| Features | Procedural Generation, Vehicular Combat, Score Attack, Combat |
| Players | Singleplayer |

**Deliberately NOT tagged, and why** — each of these is a plausible-looking claim the build
does not support, and a wrong tag both misroutes discovery and risks a review bounce:

- **Controller** — no gamepad input exists; only arrow keys.
- **Physics** — no rigidbody simulation. The car moves along the road spline via custom
  arcade code (`RoadDistance` + `LateralOffset`) and traffic is kinematic transform
  placement. Only a `BoxCollider` exists. Physics-filter users expect BeamNG-style sim.
- **Automobile Sim** — arcade handling, no gearbox/tyre/damage model.
- **Open World** — a single linear road, however long.
- **Perma Death** — runs end but cash and garage persist.
- **Roguelite / Action Roguelike** — meta-progression exists but there is no in-run build
  variety, which is what that audience expects.
- **Steam Achievements / Cloud Saves / Trading Cards** — no Steamworks SDK integration.

Re-check this list before ticking anything in "Supported Features"; add tags back as the
features actually land.

---

## System requirements

Measured on the development machine (RTX 5060 Ti, 1080p): 180–850 FPS depending on biome.
Greenwood is the heaviest. These are conservative estimates from that data — **re-measure
on a low-end PC before publishing.**

**Minimum**
- OS: Windows 10 64-bit
- Processor: Intel Core i5-6400 / AMD Ryzen 3 1200
- Memory: 8 GB RAM
- Graphics: NVIDIA GTX 1050 Ti / AMD RX 560 (2 GB)
- DirectX: Version 11
- Storage: 2 GB available space

**Recommended**
- OS: Windows 11 64-bit
- Processor: Intel Core i5-10400 / AMD Ryzen 5 3600
- Memory: 16 GB RAM
- Graphics: NVIDIA GTX 1660 / AMD RX 5600 XT
- DirectX: Version 12
- Storage: 2 GB available space

---

## Still needed before submission

- [ ] Decide the name (everything else keys off it)
- [ ] Header capsule — the single image most buyers judge you on. Consider a real artist.
- [ ] Trailer — Steam heavily favours pages with one, and Next Fest requires it
- [ ] Re-measure performance on a low-spec PC to validate the minimum spec above
- [ ] Confirm the touch UI is gone and gamepad works before anyone sees a build
