# Known Issues

Tracked issues that are understood but deliberately not yet fixed. Each entry notes the
symptom, the cause, where the relevant code lives, and the levers to try.

## Greenwood — guardrails render as white boxes (cosmetic)

**Symptom:** The roadside guardrails render as white box/crate shapes instead of a fence.

**Cause:** Likely a wrong material assignment or a model/normalization mismatch on the
guardrail mesh. Introduced with the Greenwood rework (PR #5).

**Where:** `Assets/Scripts/RoadRageBootstrap.cs`, `BuildForest()` — the guard-rail
`ScatterBand` that calls `PlaceBiomeModelOnRoad("Synthwave", "Fence/SM_fence", railMaterial, …)`
followed by `NormalizeModelHeight(...)`. Check the `railMaterial` fallback
(`materials.TryGetValue("Hills Metal", …) ? … : materials["Forest Mountain"]`) and the
`SM_fence` model/scale.

## Resolved

**Greenwood — "flying green" grass cards at the road edge (PR #19).** The flat ground ribbons
at the road edge (Forest Grass Stripe, Shoulder Bank, Leaf Litter, Forest Litter Deep, Edge
Grass) were textured with the *cutout* foliage material `materials["Forest Grass"]`. A cutout
grass texture laid flat renders as floating green blade-shapes — the "flying green objects."
Fixed by pointing all 10 flat ribbons at the *opaque* `materials["Forest Floor PBR"]`; the
vertical grass tufts (`ForestPlant`, string label) were left as cutout. This is the proper
resolution of the checklist Step 8 ground-vs-cutout collision. Verified by a rendered capture.

**Crash / revive / results settlement flow (checklist Step 7).** The confirmed negative-cash
revive bug and its "also address" items were fixed in PR #6, and the larger "defer banking
until the run truly finishes" restructure landed in PR #17 (`EndRun` computes a pending
payout; a new `CommitRun` banks it once at finish; revive no longer unwinds anything).
Verified by the `-selftest` (`RR_TEST RESULT PASS`). Remaining confidence step: a hands-on
play-through of the results-screen buttons (garage/wheel/menu), which the self-test does not
click through.
