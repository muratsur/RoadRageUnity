# Known Issues

Tracked issues that are understood but deliberately not yet fixed. Each entry notes the
symptom, the cause, where the relevant code lives, and the levers to try.

_No open issues at this time._

## Resolved

**Greenwood — guardrails read as white boxes (PR #21).** The guardrail (`SM_fence` model)
loads and is shaped fine, but it was textured with the shared `Hills Metal` material — a
bright, reflective light-grey — so the low-poly rail read as white crates against the sky.
Fixed by adding a dedicated matte `Forest Rail Metal` material and pointing the guardrail at
it. Verified by a rendered capture.

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
