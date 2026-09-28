using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    public sealed class ArcadeCarController : MonoBehaviour, IRoadVehicle
    {
        public float SpeedKph { get; internal set; } = 0f;
        public float CountdownTimer { get; set; } = 3.2f;

        /// Measured from the spawned vehicle so collision matches the mesh.
        public float HalfLength { get; internal set; } = 2.5f;
        public float HalfWidth { get; internal set; } = 1.3f;
        // --- IRoadVehicle -------------------------------------------------------
        public float ContactDistance => RoadDistance;
        public float ContactLateral => LateralOffset;
        public float ContactHalfLength => HalfLength;
        public float ContactHalfWidth => HalfWidth;
        public float ContactHeight => verticalOffset;
        /// Heaviest thing on the road by a wide margin. Being shoved off your line by
        /// scenery traffic reads as losing the car, so the player absorbs the smallest
        /// share of every correction.
        public float ContactMass => HalfLength * HalfWidth * 4f;
        /// False while the aftertouch director owns the transform during a crash tumble.
        public bool ContactActive => isActiveAndEnabled && !ClearsTraffic;

        public void ApplyContactPush(float alongRoad, float acrossRoad)
        {
            RoadDistance += alongRoad;
            LateralOffset += acrossRoad;
        }

        private void OnEnable() => VehicleContacts.Register(this);
        private void OnDisable()
        {
            VehicleContacts.Unregister(this);
            RoadRageHaptics.EdgeScrape = 0f;
        }
        // ------------------------------------------------------------------------

        public float DistanceKm => totalDistance / 1000f;
        public float RoadDistance { get; internal set; } = 5f;
        public float LateralOffset { get; internal set; } = -2.25f;

        /// What a collision leaves behind, ported from the shipped build's _hero_impact.
        ///
        /// A hit used to move LateralOffset by a fixed 0.25 m or 0.55 m and that was all:
        /// an instant nudge, over before the frame ended, with nothing left to fight. The
        /// store build knocks the truck sideways and twists it off line, and the impulse
        /// bleeds off over about a second - you have to wrestle it back, which is the
        /// whole reason a crash costs you something beyond the damage number.
        ///
        /// Velocity and angle, not position: an instant nine-metre shove would teleport
        /// the player across the carriageway, and the shipped build integrates it too.
        private float impactKnock;
        private float impactVeer;
        /// Trailer autopilot. Hunts violators, avoids innocents and holds the throttle
        /// open so recorded footage shows the actual mechanic rather than someone
        /// fumbling arrow keys. Enabled with -cinematic.
        public static bool CinematicPilot;
        private float pilotLane;
        private float pilotSwerveUntil;
        private float pilotSwerveDir;

        private void DriveCinematically()
        {
            TouchThrottle = 1f;

            // An innocent directly in the path always wins - swerving away from one is
            // the clearest way to show that the game asks you to judge, not just crash.
            var innocent = TrafficCarController.InnocentInPath(RoadDistance, LateralOffset, 48f);
            if (innocent != null && Time.time > pilotSwerveUntil)
            {
                pilotSwerveDir = innocent.LaneOffset > LateralOffset ? -1f : 1f;
                pilotSwerveUntil = Time.time + 0.9f;
            }

            if (Time.time < pilotSwerveUntil)
            {
                var half = RoadPath.HalfWidthAt(RoadDistance) - 2f;
                pilotLane = Mathf.Clamp(LateralOffset + pilotSwerveDir * 6f, -half, half);
            }
            else
            {
                var target = TrafficCarController.FindViolatorAhead(RoadDistance, 150f);
                var blocked = target != null &&
                              TrafficCarController.InnocentInPath(RoadDistance, target.LaneOffset, 60f) != null;
                if (target != null && !blocked)
                {
                    // Lead the target slightly: violators weave, so aim where it is going.
                    pilotLane = target.LaneOffset;
                }
                else
                {
                    // Nothing to hunt - drift gently across the carriageway so the shot
                    // still has motion instead of tracking a dead-straight line.
                    var half = RoadPath.HalfWidthAt(RoadDistance) - 3f;
                    pilotLane = Mathf.Sin(Time.time * 0.22f) * half * 0.55f;
                }
            }

            var error = pilotLane - LateralOffset;
            TouchSteer = Mathf.Clamp(error * 0.42f, -1f, 1f);
        }

        public float TouchSteer { get; set; }
        public float TouchThrottle { get; set; }
        public float Speed => SpeedKph / 3.6f;
        public bool IsAccelerating => (GameInput.GetThrottle() + TouchThrottle) > 0.1f;
        public bool IsBraking => (GameInput.GetThrottle() + TouchThrottle) < -0.1f;
        public float SteerInput => Mathf.Clamp(GameInput.GetSteer() + TouchSteer, -1f, 1f);
        public float LateralVelocity => lateralVelocity;
        public bool IsAirborne => verticalOffset > 0.05f;
        /// High enough to pass over traffic rather than through it. Contact and
        /// separation are both skipped above this, so a jump clears the cars below.
        internal bool ClearsTraffic => verticalOffset >= 1.6f;

        /// Re-applies the road-space position. Update places the car, then the shared
        /// separation pass runs in LateUpdate and may still move it; without this the
        /// correction would not reach the transform until the following frame.
        internal void SyncToRoad() =>
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f + verticalOffset);

        /// Resolve contacts against every other vehicle once all of them have moved,
        /// then re-place. Update has already positioned the car; this is what carries
        /// the correction onto the transform in the same frame.
        private void LateUpdate()
        {
            VehicleContacts.ResolveOncePerFrame();
            if (isActiveAndEnabled) SyncToRoad();
        }
        public float AirtimeDuration { get; private set; }

        private float verticalOffset;
        private float verticalVelocity;
        private float lateralVelocity;
        private float totalDistance;
        private float nextImpactTime;
        // Daily distance is written to PlayerPrefs, so it is batched rather than saved per frame.
        private float distanceSinceDailyBump;
        private static readonly bool autoSteer = RoadRageBootstrap.CommandLineValue("-autosteer") != null;
        private static readonly bool autoThrottle = RoadRageBootstrap.CommandLineValue("-autodrive") != null;

        public void LaunchAirtime(float launchPower)
        {
            verticalVelocity = launchPower;
            verticalOffset = 0.15f;
            AirtimeDuration = 0f;
            if (RoadRageImpactShakeDirector.Instance != null)
            {
                RoadRageImpactShakeDirector.Instance.TriggerLightShake(0.35f);
            }
        }

        private void Update()
        {
            if (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive)
            {
                CountdownTimer = 3.0f;
                SpeedKph = 0f;
                lateralVelocity = 0f;
                verticalOffset = 0f;
                verticalVelocity = 0f;
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f);
                transform.rotation = RoadPath.Rotation(RoadDistance);
                return;
            }

            if (CountdownTimer > 0f)
            {
                CountdownTimer -= Time.deltaTime;
                SpeedKph = 0f;
                lateralVelocity = 0f;
                verticalOffset = 0f;
                verticalVelocity = 0f;
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f);
                transform.rotation = RoadPath.Rotation(RoadDistance);
                return;
            }

            // -autosteer weaves across the lanes so the unattended verification run
            // actually collides with traffic; without input it just holds its own lane.
            if (autoSteer) TouchSteer = Mathf.Sin(Time.time * 0.8f) * 1.2f;
            // -autodrive floors it without input. Unlike -cinematic it keeps the HUD
            // up, so chase-loop captures can show the quarry markers being used.
            if (autoThrottle) TouchThrottle = 1f;
            if (CinematicPilot) DriveCinematically();

            var steer = SteerInput;
            var rawThrottle = GameInput.GetThrottle() + TouchThrottle;
            var throttle = Mathf.Clamp(rawThrottle, -1f, 1f);

            // Car choice, engine upgrades, and tuning parts scale performance.
            var car = GameState.CurrentCar;
            var enginePower = 1f + GameState.UpgradeEngine * 0.05f;
            var isTurbo = GameState.TuningInduction == 1;
            var isDrift = GameState.TuningTires == 1;

            var topSpeedBonus = isTurbo ? 22f : 0f;
            var accelBonus = !isTurbo ? 1.25f : 1.0f; // Supercharger launch punch
            var isBoosting = RoadRageBoostDirector.Instance != null && RoadRageBoostDirector.Instance.IsBoosting;
            var boostMult = isBoosting ? 1.52f : 1.0f;
            var boostAccel = isBoosting ? 2.8f : 1.0f;

            float targetSpeed;
            float accelRate;

            if (throttle > 0.05f || CinematicPilot)
            {
                var maxSpeed = (150f * car.Speed + topSpeedBonus) * enginePower * boostMult;
                targetSpeed = maxSpeed * Mathf.Max(0.2f, throttle);
                accelRate = 38f * car.Acceleration * enginePower * accelBonus * boostAccel;
            }
            else if (throttle < -0.05f)
            {
                targetSpeed = 0f;
                accelRate = 80f * car.Acceleration;
            }
            else
            {
                // No throttle given: smoothly coast down to standing stop (0 km/h)
                targetSpeed = 0f;
                accelRate = 18f;
            }

            SpeedKph = Mathf.MoveTowards(SpeedKph, targetSpeed, Time.deltaTime * accelRate);

            var steerSpeed = isDrift ? 13.5f : 10.5f;
            lateralVelocity = Mathf.Lerp(lateralVelocity, steer * steerSpeed, 1f - Mathf.Exp(-7f * Time.deltaTime));
            var forwardTravel = SpeedKph / 3.6f * Time.deltaTime;
            totalDistance += forwardTravel;

            // Airborne physics simulation
            var airPitch = 0f;
            if (verticalOffset > 0f || verticalVelocity > 0f)
            {
                AirtimeDuration += Time.deltaTime;
                verticalOffset += verticalVelocity * Time.deltaTime;
                verticalVelocity -= 26f * Time.deltaTime; // Gravity
                airPitch = Mathf.Clamp(verticalVelocity * 1.8f, -14f, 22f);

                if (verticalOffset <= 0f)
                {
                    verticalOffset = 0f;
                    verticalVelocity = 0f;
                    // Landing impact!
                    if (AirtimeDuration > 0.2f && AirtimeDuration <= 0.45f) RoadRageHaptics.Light();
                    if (AirtimeDuration > 0.45f)
                    {
                        var bonus = Mathf.RoundToInt(AirtimeDuration * 1200f);
                        GameState.Award(bonus, $"🚀 AIRTIME STUNT ({AirtimeDuration:0.1}s)");
                        if (RoadRageBoostDirector.Instance != null)
                            RoadRageBoostDirector.Instance.AddBoost(40f, "AIRTIME STUNT");
                        if (RoadRageImpactShakeDirector.Instance != null)
                            RoadRageImpactShakeDirector.Instance.TriggerMediumShake(0.45f);
                    }
                    AirtimeDuration = 0f;
                }
            }

            // Distance points tick up as you drive; the oncoming side is worth double,
            // the risk/reward trade the shipped build uses to pull players across the line.
            GameState.Tick(Time.deltaTime);
            GameState.Score += Mathf.RoundToInt(SpeedKph * Time.deltaTime * 0.7f * (LateralOffset < -1.5f ? 2f : 1f));
            var travelledKm = forwardTravel / 1000f;
            GameState.RunDistanceKm += travelledKm;
            distanceSinceDailyBump += travelledKm;
            if (distanceSinceDailyBump >= 0.05f)
            {
                GameState.BumpDaily("distance", distanceSinceDailyBump);
                distanceSinceDailyBump = 0f;
            }

            RoadDistance = RoadPath.Wrap(RoadDistance + forwardTravel);
            var edge = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset + (lateralVelocity + impactKnock) * Time.deltaTime, -edge, edge);
            RoadRageHaptics.EdgeScrape = Mathf.Abs(LateralOffset) >= edge - 0.05f && verticalOffset <= 0f
                ? Mathf.InverseLerp(30f, 160f, SpeedKph)
                : 0f;
            // Both decay towards zero over roughly a second, so an impact reads as a shove
            // you drive out of rather than a step you never saw.
            impactKnock = Mathf.MoveTowards(impactKnock, 0f, 11f * Time.deltaTime);
            impactVeer = Mathf.MoveTowards(impactVeer, 0f, 26f * Time.deltaTime);
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f + verticalOffset);
            var desiredRotation = RoadPath.Rotation(RoadDistance)
                * Quaternion.Euler(-airPitch, steer * 9f + impactVeer, -steer * 4f);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-8f * Time.deltaTime));
            
            // Only test ground collision if not flying high over cars
            if (!ClearsTraffic)
            {
                TrafficCarController.ResolvePlayerCollision(this);
            }

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.UpdateEngineAudio(SpeedKph, 150f * car.Speed, throttle, false);
            }
        }

        public void RefillNitro()
        {
            SpeedKph = Mathf.Min(SpeedKph + 25f, 220f);
        }

        public bool ApplyTrafficImpact(TrafficCarController traffic, float longitudinalGap, float lateralGap)
        {
            if (Time.time < nextImpactTime) return false;
            nextImpactTime = Time.time + 0.35f;
            var sideSwipe = Mathf.Abs(lateralGap) > 1.25f && Mathf.Abs(longitudinalGap) < 3.2f;

            // Anti-penetration is no longer done here. It used to push on both axes at
            // once with its own shrunken hull sizes, fighting the traffic's separate
            // resolution of the same contact; both now go through the single relaxation
            // pass in TrafficCarController, which runs after every vehicle has moved.

            // Armour is the reason to drive a truck: it cuts how much speed an impact
            // scrubs off and how far you get shoved. Upgrades and ram bars stack on top of the chassis.
            var ramArmorBonus = GameState.TuningRamBar switch
            {
                2 => 1.65f, // Titanium Battering Ram
                1 => 1.30f, // Steel Push-Bar
                _ => 1.0f
            };
            var armour = GameState.CurrentCar.Armour * (1f + GameState.UpgradeArmour * 0.06f) * ramArmorBonus;
            var absorb = Mathf.Clamp(1f / Mathf.Max(0.25f, armour), 0.28f, 1.75f);

            if (traffic.IsWreck)
            {
                GameState.Combo = 0;
                GameState.Show("WRECKAGE");
                GameState.ApplyDamage(6f * absorb);
            }
            else if (traffic.IsViolator)
            {
                GameState.Takedowns++;
                GameState.BumpDaily("takedowns", 1f);
                var label = traffic.IsHitAndRunner ? "HIT & RUN" : traffic.Violation switch
                {
                    TrafficCarController.Offence.Weaving => "RECKLESS DRIVER",
                    TrafficCarController.Offence.Speeding => "SPEEDER",
                    TrafficCarController.Offence.WrongWay => "WRONG WAY",
                    _ => "TAILGATER",
                };
                // Running down a target that fled pays double - the chase is the game.
                var pursued = traffic.IsFleeing;
                GameState.Award(pursued ? (sideSwipe ? 300 : 500) : (sideSwipe ? 150 : 250),
                    pursued ? $"PURSUIT {label}" : label);
                if (traffic.IsHitAndRunner)
                {
                    GameState.Award(400, "JUSTICE SERVED");
                    traffic.ClearHitAndRun();
                    Debug.Log($"RR_EVENT hitandrun captured speedKmh={SpeedKph:0}");
                }
                if (RoadRagePolicePursuitDirector.Instance != null)
                    RoadRagePolicePursuitDirector.Instance.AddHeat(0.35f);
                if (RoadRageBoostDirector.Instance != null)
                    RoadRageBoostDirector.Instance.AddBoost(pursued ? 80f : 50f, "TAKEDOWN BOOST");
                GameState.ApplyDamage((sideSwipe ? 1.5f : 3f) * absorb);
            }
            else
            {
                GameState.Combo = 0;
                GameState.InnocentsHit++;
                GameState.Score = Mathf.Max(0, GameState.Score - 200);
                GameState.Show("INNOCENT DRIVER  -200");
                GameState.ApplyDamage(14f * absorb);
            }
            var contactPoint = transform.position + transform.forward * 2.2f + Vector3.up * 0.6f;
            CrashEffects.Active?.PlayAt(contactPoint);
            var audioVfx = GetComponent<RoadRageAudioAndVFX>();
            if (audioVfx != null)
                audioVfx.PlayCrashImpact(contactPoint, traffic.IsViolator);

            if (RoadRageImpactShakeDirector.Instance != null)
            {
                // 0.3 + 0.35 x severity, the shipped curve, so a graze still registers and
                // a square hit is not already saturated before the wreck lands.
                RoadRageImpactShakeDirector.Instance.TriggerMediumShake(
                    0.3f + 0.35f * (sideSwipe ? 0.45f : 0.9f));
            }

            if (GameState.Integrity <= 0f && !GameState.IsAftertouchActive)
            {
                if (RoadRageImpactShakeDirector.Instance != null)
                {
                    RoadRageImpactShakeDirector.Instance.TriggerHeavyCrashShake(1.0f);
                }

                if (RoadRageAftertouchDirector.Instance != null)
                {
                    var tumbleVel = transform.forward * (SpeedKph / 3.6f * 0.92f) + (traffic.transform.position - transform.position).normalized * 7.5f + Vector3.up * 8.5f;
                    var tumbleTorque = new Vector3(Random.Range(-18f, 18f), Random.Range(12f, 28f), Random.Range(-30f, 30f));
                    RoadRageAftertouchDirector.Instance.TriggerAftertouch(transform, this, tumbleVel, tumbleTorque);
                    return true;
                }
            }

            if (traffic.IsViolator || SpeedKph >= 60f)
            {
                if (RoadRageTakedownDirector.Instance != null && !traffic.IsWreck)
                {
                    var impactNormal = (traffic.transform.position - transform.position).normalized;
                    RoadRageTakedownDirector.Instance.TriggerTakedown(traffic.transform, contactPoint, impactNormal, SpeedKph);
                }
            }

            if (sideSwipe)
                SpeedKph = Mathf.Max(28f, SpeedKph - 22f * absorb);
            else if (traffic.IsWreck || traffic.Direction < 0f)
                SpeedKph = Mathf.Min(SpeedKph, Mathf.Lerp(12f, SpeedKph, 1f - absorb));
            else
                SpeedKph = Mathf.Min(SpeedKph, Mathf.Lerp(38f, SpeedKph, 1f - absorb));

            var pushDirection = Mathf.Abs(lateralGap) < 0.05f ? -1f : -Mathf.Sign(lateralGap);
            lateralVelocity += pushDirection * (sideSwipe ? 3.5f : 6.5f) * absorb;
            var pushEdge = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            // _hero_impact, from game.gd: shove towards the centre when you are already near
            // an edge, otherwise pick a side; knock, twist and scrub speed, all scaled by
            // how hard it was. A side-swipe is a graze, a square hit is not.
            var severity = (sideSwipe ? 0.45f : 0.9f) * absorb;
            var knockSide = Mathf.Abs(LateralOffset) > 4f ? -Mathf.Sign(LateralOffset) : pushDirection;
            if (Mathf.Abs(knockSide) < 0.01f) knockSide = 1f;
            impactKnock += knockSide * 9f * severity;
            impactVeer += knockSide * 14f * severity;
            SpeedKph *= 1f - 0.18f * severity;
            LateralOffset = Mathf.Clamp(LateralOffset, -pushEdge, pushEdge);
            return true;
        }
    }
}
