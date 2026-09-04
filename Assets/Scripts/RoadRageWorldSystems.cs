using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    public static class RoadPath
    {
        /// Widest profile: three lanes each way at 4.5 m. Kept as the nominal constant for
        /// code that needs a bound, but the live road width varies by zone.
        public const float Width = 27f;
        public const float LaneWidth = 4.5f;
        public const float HalfWidth = Width * 0.5f;
        public const float ShoulderWidth = 2.5f;
        public const float RoadsideClearance = HalfWidth + ShoulderWidth + 0.75f;

        /// City zones want a six-lane highway; forest and canyon zones want a two-lane
        /// country road the canopy can close over. The streamer installs a provider that
        /// maps distance to half-width, tapering across zone seams so the carriageway
        /// narrows as you drive into the trees rather than stepping.
        public static System.Func<float, float> HalfWidthProvider;

        public static float HalfWidthAt(float distance) =>
            HalfWidthProvider?.Invoke(distance) ?? HalfWidth;

        public static float ClearanceAt(float distance) =>
            HalfWidthAt(distance) + ShoulderWidth + 0.75f;

        /// Lane centre for a signed fraction in [-1, 1] of the carriageway.
        public static float LaneLateral(float distance, float fraction) =>
            fraction * (HalfWidthAt(distance) - LaneWidth * 0.5f);
        /// Retained only for code that still needs a nominal span (ribbon sampling
        /// defaults). The road itself no longer wraps - distance grows without bound so
        /// the world can stream biome zones instead of looping the same 900 m.
        public const float Length = 900f;

        /// Identity now. Kept so call sites read the same, and so any distance that was
        /// historically wrapped stays continuous.
        public static float Wrap(float distance) => distance;

        /// Sum of sines with incommensurate wavelengths: continuous for unbounded
        /// distance and with no period a player could notice (the shortest common
        /// repeat is far beyond any run length).
        public static float CenterX(float distance)
        {
            return 13f * Mathf.Sin(distance / 143f)
                + 6f * Mathf.Sin(distance / 61.7f + 0.65f)
                + 2.5f * Mathf.Sin(distance / 27.3f - 0.4f);
        }

        /// Road elevation. Long rolling hills with a shorter undulation on top; amplitudes
        /// stay modest because the chase camera sits low and steep grades hide the road.
        public static float CenterY(float distance)
        {
            return 9f * Mathf.Sin(distance / 197f + 0.9f)
                + 3.5f * Mathf.Sin(distance / 83f - 0.3f);
        }

        public static Vector3 Center(float distance, float height = 0f) =>
            new(CenterX(distance), CenterY(distance) + height, distance);

        /// Flat tangent - used for lateral maths so lane offsets stay horizontal on a
        /// gradient rather than shrinking as the road pitches.
        public static Vector3 Forward(float distance)
        {
            var tangent = Center(distance + 0.6f) - Center(distance - 0.6f);
            tangent.y = 0f;
            return tangent.normalized;
        }

        public static Vector3 Tangent(float distance) => Forward(distance);

        /// True tangent including gradient, so vehicles pitch nose-up climbing.
        public static Vector3 ForwardPitched(float distance) =>
            (Center(distance + 0.6f) - Center(distance - 0.6f)).normalized;

        public static Vector3 Right(float distance) =>
            Vector3.Cross(Vector3.up, Forward(distance)).normalized;

        public static Vector3 Point(float distance, float lateral, float height = 0f) =>
            Center(distance, height) + Right(distance) * lateral;

        public static Quaternion Rotation(float distance) =>
            Quaternion.LookRotation(ForwardPitched(distance), Vector3.up);

        /// Signed gradient, positive uphill. Lets the car bleed speed on a climb.
        public static float Gradient(float distance) =>
            (CenterY(distance + 3f) - CenterY(distance - 3f)) / 6f;

        // On an open road these are plain differences - no modular arithmetic, because
        // there is no longer a seam for a car to be "behind" you through.
        public static float SignedDelta(float from, float to) => to - from;

        public static float ForwardGap(float from, float to, float direction)
        {
            var gap = direction >= 0f ? to - from : from - to;
            return gap < 0f ? float.PositiveInfinity : gap;
        }
    }

    public sealed class TrafficCarController : MonoBehaviour, IRoadVehicle
    {
        private static readonly List<TrafficCarController> ActiveCars = new();
        /// Read-only view for systems that need to act on every car - the ramp director
        /// tests each one for a launch the same way it tests the player, and the HUD
        /// projects each violator to a screen marker.
        public static IReadOnlyList<TrafficCarController> All => ActiveCars;

        public float RoadDistance { get; private set; }
        /// Signed fraction of the carriageway, -1 (outer left) to 1 (outer right).
        public float LaneFraction { get; private set; }
        /// laneDrift is *behaviour* - weaving, tailgating, a wreck sliding to the
        /// shoulder - and every one of those paths assigns it outright each frame.
        /// Anti-penetration pushback used to be written into the same field, so a
        /// weaving car's `laneDrift = sin(phase) * 5.2` threw the push away on the very
        /// next frame and the car drove straight back through whatever it had just been
        /// separated from. Separation therefore lives in its own channel that no
        /// behaviour writes to, and only relaxes back to zero once contact is over.
        public float LaneOffset
        {
            get
            {
                var edge = Mathf.Max(1f, RoadPath.HalfWidthAt(RoadDistance) + RoadPath.ShoulderWidth - HalfWidth);
                return Mathf.Clamp(RoadPath.LaneLateral(RoadDistance, LaneFraction) + laneDrift + separation,
                                   -edge, edge);
            }
        }
        private float laneDrift;
        private float separation;
        /// Enough to clear a neighbouring lane, not enough to launch a car off the road.
        private const float MaxSeparation = 4.5f;
        /// How fast a car eases back to its lane once nothing is touching it. Slow
        /// enough that it does not simply drive back into the car it was pushed off.
        private const float SeparationRelax = 1.1f;
        public float Direction { get; private set; }
        public bool IsWreck { get; private set; }
        public float WreckYaw { get; private set; }

        private float cruiseSpeedKph;
        private float currentSpeedKph;
        public float SpeedKph => currentSpeedKph;
        private float wreckRoll;
        private int variationSeed;

        /// The core mechanic: this is a vigilante game, so traffic must be judgeable.
        /// Violators earn score when rammed; innocents cost you. The tell is behaviour,
        /// not colour - it has to be readable at 120 km/h in a mirror-less chase camera.
        public enum Offence { None, Weaving, Speeding, WrongWay, Tailgating }
        public Offence Violation { get; private set; }

        public enum VehicleRole { Standard, FuelTanker, CarHauler }
        public VehicleRole Role { get; set; } = VehicleRole.Standard;
        public bool HasDetonated { get; private set; }

        public void DetonateTanker()
        {
            if (HasDetonated) return;
            HasDetonated = true;
            IsWreck = true;
            WreckYaw = Random.Range(-45f, 45f);

            var blastPos = transform.position + Vector3.up * 1.2f;
            CrashEffects.Active?.PlayAt(blastPos);

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayCrash(1.0f);
            }
            if (RoadRageHapticsDirector.Instance != null)
            {
                RoadRageHapticsDirector.Instance.TriggerHeavyCrashHaptic(1.0f);
            }

            GameState.Award(4000, "💥 TANKER MASSIVE EXPLOSION!");
            if (RoadRageBoostDirector.Instance != null)
            {
                RoadRageBoostDirector.Instance.AddBoost(100f, "TANKER CHAIN REACTION");
            }

            // A tanker is a pursuit breaker. Anything inside the blast goes, cruisers
            // included - which gives a chase a shape beyond outrunning it: bait them
            // past the tanker, then set it off.
            if (RoadRagePolicePursuitDirector.Instance != null)
            {
                var police = RoadRagePolicePursuitDirector.Instance.ActivePolice;
                for (var i = police.Count - 1; i >= 0; i--)
                {
                    var cop = police[i];
                    if (cop == null) continue;
                    if (Mathf.Abs(cop.RoadDistance - RoadDistance) > 30f) continue;
                    GameState.Show("🚨 TANKER TOOK A CRUISER!");
                    cop.WreckCop();
                }
            }

            // Chain reaction blast to surrounding traffic
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var other = ActiveCars[i];
                if (other != null && other != this && !other.IsWreck)
                {
                    var d = Mathf.Abs(RoadPath.SignedDelta(RoadDistance, other.RoadDistance));
                    if (d < 28f)
                    {
                        other.Crash(Random.Range(-1.5f, 1.5f), 140f);
                        GameState.Award(1500, "💥 TANKER BLAST WIPEOUT!");
                        GameState.Takedowns++;
                    }
                }
            }
        }

        /// Measured from the spawned mesh. The collision test used fixed 4.3 m / 2.05 m
        /// radii sized for a small car; with a 7.2 m truck against a 5.0 m car the meshes
        /// overlapped by ~1.8 m before the hit registered, which is the clipping.
        public float HalfLength { get; private set; } = 2.5f;
        public float HalfWidth { get; private set; } = 1.3f;

        public void SetFootprint(float halfLength, float halfWidth)
        {
            HalfLength = Mathf.Max(0.5f, halfLength);
            HalfWidth = Mathf.Max(0.4f, halfWidth);
        }

        /// Half-extents of the hull *projected onto the road axes*.
        ///
        /// The overlap test compares distances along the road and offsets across it, so
        /// it needs the box measured in that frame. A wreck slewed 60 degrees across the
        /// asphalt is 4 m wide in road space even though it is a 1.9 m car, and testing
        /// it with its raw half-width let live traffic drive through the part of the
        /// wreck that was sticking out. Projecting the oriented box onto each road axis
        /// is the separating-axis test for the two axes that matter here.
        public float LongitudinalExtent
        {
            get
            {
                if (WreckYaw == 0f) return HalfLength;
                var yaw = WreckYaw * Mathf.Deg2Rad;
                return Mathf.Abs(HalfLength * Mathf.Cos(yaw)) + Mathf.Abs(HalfWidth * Mathf.Sin(yaw));
            }
        }

        public float LateralExtent
        {
            get
            {
                if (WreckYaw == 0f) return HalfWidth;
                var yaw = WreckYaw * Mathf.Deg2Rad;
                return Mathf.Abs(HalfLength * Mathf.Sin(yaw)) + Mathf.Abs(HalfWidth * Mathf.Cos(yaw));
            }
        }
        // --- IRoadVehicle -------------------------------------------------------
        public float ContactDistance => RoadDistance;
        public float ContactLateral => LaneOffset;
        public float ContactHalfLength => LongitudinalExtent;
        public float ContactHalfWidth => LateralExtent;
        public float ContactHeight => verticalOffset;
        /// Scales with footprint, so a lorry shoulders a hatchback aside rather than
        /// the pair meeting in the middle.
        public float ContactMass => HalfLength * HalfWidth;
        public bool ContactActive => isActiveAndEnabled && !Ragdolled;

        public void ApplyContactPush(float alongRoad, float acrossRoad)
        {
            RoadDistance += alongRoad;
            // Into the separation channel, never laneDrift - behaviour rewrites that
            // every frame and would throw the correction away.
            separation = Mathf.Clamp(separation + acrossRoad, -MaxSeparation, MaxSeparation);
        }
        // ------------------------------------------------------------------------

        public bool IsViolator => Violation != Offence.None && !IsWreck;
        /// True once this violator has noticed the hunter on its tail and is running.
        /// Drives the run speed, harder weaving and the HUD quarry marker.
        public bool IsFleeing { get; private set; }
        /// A staged hit-and-run offender. Runs far longer than a spooked driver - the
        /// whole point is a personal, catchable chase - and pays a justice bonus.
        public bool IsHitAndRunner { get; private set; }
        /// Metres from the player, positive when this car is ahead. Feeds the HUD.
        public float GapToPlayer => RoadDistance - PlayerDistance;
        private const float FleeTriggerGap = 90f;
        private const float FleeLostGap = 175f;
        private const float FleeMaxKph = 185f;
        private const float HitAndRunLostGap = 420f;
        private const float HitAndRunMaxKph = 195f;
        private float weavePhase;
        private float weaveRate;
        /// Lateral drift target while queued behind an obstruction, set by the yield
        /// scan in Update. Zero when the road ahead is clear.
        private float overtakeDriftTarget;
        /// Hysteresis for the overtake: which way we are passing, and where the
        /// blocker sits - the drift holds until the car is fully past that point.
        private float overtakeDir;
        private float overtakeBlockerRoad = float.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => ActiveCars.Clear();

        public void Initialize(float distance, float lane, float speedKph, float direction,
            bool wreck = false, float wreckYaw = 0f, Offence violation = Offence.None)
        {
            Violation = wreck ? Offence.None : violation;
            weavePhase = Random.Range(0f, Mathf.PI * 2f);
            weaveRate = Random.Range(0.55f, 0.95f);
            // Speeders run ~1.5x traffic, capped so a 200 km/h hunter can still close on them.
            if (Violation == Offence.Speeding) speedKph = Mathf.Min(speedKph * 1.55f, 165f);
            if (Violation == Offence.WrongWay) direction = -direction;
            RoadDistance = RoadPath.Wrap(distance);
            LaneFraction = Mathf.Clamp(lane, -1f, 1f);
            cruiseSpeedKph = speedKph;
            currentSpeedKph = wreck ? 0f : speedKph;
            Direction = direction >= 0f ? 1f : -1f;
            IsWreck = wreck;
            WreckYaw = wreckYaw;
            wreckRoll = wreck ? Mathf.Sign(wreckYaw) * 2.5f : 0f;
            variationSeed = 17;
            foreach (var character in name) variationSeed = variationSeed * 31 + character;
            if (!ActiveCars.Contains(this)) ActiveCars.Add(this);
            PlaceOnRoad();
        }

        private void UpdateOffenceBehaviour(float delta)
        {
            // A queued driver edges around whatever blocked it - this outranks the
            // offence drift below, otherwise one wreck parks the whole highway.
            if (!IsWreck && overtakeDriftTarget != 0f)
            {
                laneDrift = Mathf.Lerp(laneDrift, overtakeDriftTarget, delta * 2.5f);
                return;
            }

            switch (Violation)
            {
                case Offence.Weaving:
                    // Wide, lazy lane drift - the most readable tell at speed.
                    weavePhase += delta * weaveRate * (IsFleeing ? 1.6f : 1f);
                    laneDrift = Mathf.Sin(weavePhase) * (IsFleeing ? 7.4f : 5.2f);
                    break;
                case Offence.Tailgating:
                    laneDrift = Mathf.Lerp(laneDrift, 0f, delta * 2f);
                    break;
                default:
                    // Any runner swerves when spooked, even a plain speeder - a fleeing
                    // target that drives in a straight line is not a chase.
                    if (IsFleeing)
                    {
                        weavePhase += delta * weaveRate * 1.3f;
                        laneDrift = Mathf.Sin(weavePhase) * 4.5f;
                    }
                    else
                    {
                        laneDrift = Mathf.Lerp(laneDrift, 0f, delta * 2f);
                    }
                    break;
            }
        }

        private void UpdateChaseState()
        {
            // Only same-direction violators can be chased; oncoming WrongWay cars pass
            // too fast to run from anything.
            if (IsWreck || Direction < 0f || Violation == Offence.None)
            {
                IsFleeing = false;
                return;
            }

            var gap = GapToPlayer;
            if (!IsFleeing && gap > 0f && gap < FleeTriggerGap)
            {
                IsFleeing = true;
                if (RoadRageAudioBridge.Instance != null)
                    RoadRageAudioBridge.Instance.PlayTurboFlutter();
            }
            else if (IsFleeing && (gap <= 0f || gap > (IsHitAndRunner ? HitAndRunLostGap : FleeLostGap)))
            {
                // The chase ends. Overtaking a spooked driver is just overtaking, but a
                // hit-and-runner you pass without catching has genuinely escaped - the
                // road only goes forward.
                if (IsHitAndRunner || gap > 0f)
                {
                    GameState.Show(IsHitAndRunner ? "HIT & RUNNER GOT AWAY" : "HE GOT AWAY - STAY ON HIS TAIL");
                    if (IsHitAndRunner)
                        Debug.Log($"RR_EVENT hitandrun escaped gap={gap:0}m");
                }
                IsHitAndRunner = false;
                IsFleeing = false;
            }
        }

        /// Called by the world when this car is staged as a hit-and-run offender: it
        /// rammed a civilian and now the player is personally after it. The shove is
        /// the direction AWAY from the wreck it just left, so it sidesteps out of that
        /// lane instead of ploughing into its own crash scene and self-wrecking.
        public void BeginHitAndRun(float shove)
        {
            if (IsWreck || Direction < 0f) return;
            IsHitAndRunner = true;
            IsFleeing = true;
            LaneFraction = Mathf.Clamp(LaneFraction + shove * 0.5f, -0.88f, 0.88f);
            weavePhase = shove > 0f ? 0.6f : -0.6f;
            crashImmunityUntil = Time.time + 2f;
        }

        /// The hunter caught the offender - the special chase is over.
        public void ClearHitAndRun() => IsHitAndRunner = false;
        private float crashImmunityUntil;

        /// Overtake drift, clamped so the passing car stays ON the asphalt - on the
        /// narrow two-lane biomes a raw 4 m shove pushed cars off the road edge.
        private float OvertakeDrift(float dir)
        {
            var half = RoadPath.HalfWidthAt(RoadDistance) - 1.2f;
            if (half <= 0f) return 0f;
            var baseOffset = LaneOffset - laneDrift;
            var lo = -half - baseOffset;
            var hi = half - baseOffset;
            return Mathf.Clamp(dir * 4f, Mathf.Min(lo, hi), Mathf.Max(lo, hi));
        }

        private void OnDestroy() => ActiveCars.Remove(this);

        /// Set by the streamer each frame. On an open road traffic can no longer wrap,
        /// so cars that fall too far behind are recycled ahead of the player instead.
        public static float PlayerDistance;

        /// Whether this biome has signals to obey. Only the city biomes place traffic
        /// lights, and a car braking for an invisible junction on a country road is worse
        /// than one that never stops - the shipped Godot build gates it the same way.
        public static bool SignalsActive;

        /// Whether this car can still score the player a near miss.
        ///
        /// Per car, re-armed once it is well clear, rather than one cooldown across the
        /// whole road. Threading a gap between two cars is two near misses in the shipped
        /// build and was one here, because a single global timer swallowed the second -
        /// and the second is the one that was hard.
        public bool NearMissArmed { get; set; } = true;

        /// Distance between junctions. BuildCyberSprawl puts a traffic light on every
        /// second 22 m block, so the signals a driver can see stand 44 m apart and the
        /// stop line has to agree with them.
        private const float SignalSpacing = 44f;
        private const float StopLineSetback = 4.5f;
        private const float SignalCycle = 13f;
        private const float SignalRedFor = 5.5f;

        /// One phase for the whole road rather than per junction, so a queue that stops
        /// together starts together. Wall time, not game time: the signal keeps its rhythm
        /// through a hitstop or a slow-motion takedown.
        public static bool SignalRed => Time.time % SignalCycle < SignalRedFor;

        /// Holds a car at the stop line for the whole red, and lets one already past it
        /// clear the junction rather than stopping in the middle of it.
        ///
        /// Applied to the step AFTER car-following has chosen a speed, for the reason the
        /// Godot build spells out: run it earlier and the follower logic drags a car
        /// straight through the line while it is busy closing on the car in front.
        private float ClampToStopLine(float from, float step)
        {
            if (!SignalsActive || IsWreck || IsFleeing || !SignalRed) return step;

            var line = Direction > 0f
                ? (Mathf.Floor(from / SignalSpacing) + 1f) * SignalSpacing - StopLineSetback
                : Mathf.Floor(from / SignalSpacing) * SignalSpacing + StopLineSetback;
            var ahead = (line - from) * Direction;
            if (ahead < -0.5f) return step;          // through it; keep going and clear

            var room = Mathf.Max(0f, ahead);
            // sqrt(2as) is the distance-to-stop curve, so the approach eases in instead of
            // slamming from cruise to nothing one metre short of the paint.
            var brake = Mathf.Sqrt(2f * 9f * room) * Time.deltaTime;
            var forward = step * Direction;
            return Direction * Mathf.Clamp(forward, 0f, Mathf.Min(room, brake));
        }

        /// Two cars meeting head-on both wreck, with no player involved.
        ///
        /// Ported from the shipped build, where a wrong-way rule-breaker that ploughs into
        /// oncoming traffic causes a genuine accident. Without it WrongWay is a cosmetic
        /// label: the offender drives the wrong way down an oncoming lane and passes
        /// through everything in it. At least one side has to be going the wrong way -
        /// lawful traffic in opposing lanes is separated laterally and must never trigger
        /// this, however close the lanes run.
        private void TryHeadOn(TrafficCarController other)
        {
            if (IsWreck || other.IsWreck) return;
            if (Violation != Offence.WrongWay && other.Violation != Offence.WrongWay) return;
            if (Mathf.Abs(other.LaneOffset - LaneOffset) > LateralExtent + other.LateralExtent) return;
            var gap = Mathf.Abs(other.RoadDistance - RoadDistance)
                      - (LongitudinalExtent + other.LongitudinalExtent);
            if (gap > 0f) return;

            // Closing speed, not either car's own: a 60 into a 60 is a 120 impact, and
            // splitting it between them is what makes both spin rather than one nudging
            // the other aside.
            var closing = currentSpeedKph + other.currentSpeedKph;
            var side = Mathf.Sign(LaneOffset - other.LaneOffset);
            if (Mathf.Abs(side) < 0.01f) side = 1f;
            Crash(side, closing * 0.5f);
            other.Crash(-side, closing * 0.5f);
            Debug.Log($"RR_EVENT headon at {RoadDistance:0}m closing={closing:0}kph");
        }
        private const float RecycleBehind = 140f;
        private const float WreckRecycleBehind = 70f;
        private const float RecycleAhead = 300f;
        /// A recycled car rerolls its allegiance: the player outruns same-direction
        /// traffic within seconds, so without a fresh supply of rule-breakers the road
        /// ahead turns into long empty stretches with nobody to hunt.
        private static readonly Offence[] RecycleOffences =
            { Offence.Weaving, Offence.Speeding, Offence.Tailgating, Offence.WrongWay };

        private void Recycle()
        {
            var relocated = false;
            // Wrecks clear sooner than live traffic: they are obstacles, and since
            // crashes now persist on screen a wreck parked at the horizon-length
            // recycle distance constipates a lane for far too long.
            var behindLimit = PlayerDistance - (IsWreck ? WreckRecycleBehind : RecycleBehind);
            if (Direction >= 0f)
            {
                // Same-direction traffic overtaken by the player reappears up ahead.
                if (RoadDistance < behindLimit)
                {
                    RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.55f, RecycleAhead);
                    relocated = true;
                }
            }
            else
            {
                // Oncoming traffic that has passed comes back from further up the road.
                if (RoadDistance < behindLimit)
                {
                    RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.7f, RecycleAhead * 1.4f);
                    relocated = true;
                }
            }

            if (RoadDistance > PlayerDistance + RecycleAhead * 1.6f)
            {
                RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.4f, RecycleAhead);
                relocated = true;
            }

            // Everything below belongs to the recycle EVENT. Recycle() runs every
            // frame: without the relocated gate, wrecks un-wrecked instantly and the
            // allegiance reroll flickered violator, fleeing and hit-and-run flags off
            // within a frame of being set.
            if (!relocated) return;

            // Teleported cars have no history: keeping the old following distance made a
            // recycled car brake for a gap it was no longer anywhere near.
            separation = 0f;

            // Reroll the offence while the car is off-screen. Keeps roughly one in two
            // recycled cars worth chasing without ever emptying the road of innocents.
            if (!IsWreck)
            {
                Violation = Random.value < 0.45f
                    ? RecycleOffences[Random.Range(0, RecycleOffences.Length)]
                    : Offence.None;
                IsHitAndRunner = false;
                return;
            }

            // A wreck recycled into fresh road should drive again, otherwise the player
            // eventually meets a highway made entirely of stationary crashes.
            IsWreck = false;
            WreckYaw = 0f;
            wreckRoll = 0f;
            // Staged accident-scene cars are spawned with a cruise speed of zero. Reviving
            // one without giving it a real speed turned it into a permanently parked car
            // in a live lane, and everything behind it matched zero and stopped too - the
            // highway filled with stalled traffic the further the player drove.
            if (cruiseSpeedKph < 20f)
                cruiseSpeedKph = Direction >= 0f ? Random.Range(68f, 124f) : Random.Range(95f, 140f);
            currentSpeedKph = cruiseSpeedKph;
        }

        /// True once something has handed this car to the physics engine - the crash
        /// blast during an aftertouch tumble. The controller then stops driving it and
        /// stops forcing it kinematic, because those two things fight.
        public bool Ragdolled { get; private set; }

        /// Hands the car to physics for good. Without this the blast set isKinematic
        /// false and applied an explosion impulse, and this controller set isKinematic
        /// back to true on the very next frame - so even once the blast could find a car
        /// to hit, it could never actually move one.
        public void ReleaseToPhysics()
        {
            if (Ragdolled) return;
            Ragdolled = true;
            IsWreck = true;
            VehicleContacts.Unregister(this);
        }

        private void Update()
        {
            if (Ragdolled) return;

            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            Recycle();
            UpdateChaseState();
            UpdateOffenceBehaviour(Time.deltaTime);
            if (!IsWreck)
            {
                var desiredSpeed = cruiseSpeedKph;
                var nearestGap = float.MaxValue;
                var blockerOffset = 0f;
                var blockerRoadDistance = 0f;
                var hasBlocker = false;
                var nearestBlockerGap = float.MaxValue;
                for (var i = ActiveCars.Count - 1; i >= 0; i--)
                {
                    var other = ActiveCars[i];
                    if (other == null)
                    {
                        ActiveCars.RemoveAt(i);
                        continue;
                    }
                    if (other == this) continue;
                    if (!other.IsWreck && Mathf.Sign(other.Direction) != Mathf.Sign(Direction))
                    {
                        // Oncoming cars are not followed - they close on each other and a
                        // follower would freeze face to face with one - but they can still
                        // collide, which is what this checks before skipping.
                        TryHeadOn(other);
                        continue;
                    }

                    // Tailgaters deliberately close the gap; speeders do not yield.
                    if (Violation == Offence.Tailgating && !other.IsWreck) continue;

                    var laneSpacing = Mathf.Max(1.2f, RoadPath.HalfWidthAt(RoadDistance) * 0.5f);
                    var lateralFootprint = Mathf.Min(LateralExtent + other.LateralExtent,
                                                     laneSpacing * (other.IsWreck ? 1.4f : 0.8f));
                    if (Mathf.Abs(other.LaneOffset - LaneOffset) > lateralFootprint) continue;
                    // Bumper to bumper, not centre to centre. Following distance measured
                    // between centres treats a 9 m lorry as though it were a hatchback,
                    // so a car would keep closing until the two hulls were already inside
                    // one another and only the separation pass was holding them apart.
                    var centreGap = RoadPath.ForwardGap(RoadDistance, other.RoadDistance, Direction);
                    if (centreGap <= 0.05f) continue;   // behind us, or the same car
                    var gap = centreGap - (LongitudinalExtent + other.LongitudinalExtent);
                    if (gap >= 38f) continue;
                    nearestGap = Mathf.Min(nearestGap, gap);
                    if (gap < nearestBlockerGap)
                    {
                        nearestBlockerGap = gap;
                        blockerOffset = other.LaneOffset;
                        blockerRoadDistance = other.RoadDistance;
                        hasBlocker = true;
                    }

                    var obstacleSpeed = other.IsWreck ? 0f : other.currentSpeedKph;
                    var safeSpeed = gap < 10f
                        ? obstacleSpeed * 0.9f
                        : Mathf.Lerp(Mathf.Max(obstacleSpeed * 0.9f, 18f), Mathf.Max(24f, obstacleSpeed),
                                     Mathf.InverseLerp(10f, 38f, gap));
                    desiredSpeed = Mathf.Min(desiredSpeed, safeSpeed);
                }

                var acceleration = desiredSpeed < currentSpeedKph ? 55f : 16f;
                // Gridlock escape with hysteresis: once a driver starts edging around
                // an obstruction it COMMITS to the pass. Releasing the drift as soon
                // as the lateral gap opened let the car slide back into the blocked
                // line and park at an angle forever.
                if (hasBlocker && desiredSpeed < cruiseSpeedKph * 0.55f)
                {
                    // A trailing car sitting directly behind another in the same lane has
                    // LaneOffset == blockerOffset, so Mathf.Sign returns 0 - no overtake
                    // direction is ever chosen, and it just matches the blocker's speed
                    // forever. If the blocker is stopped, this parks the follower (and
                    // everyone behind it) permanently. Fall back to a deterministic side.
                    overtakeDir = Mathf.Sign(LaneOffset - blockerOffset);
                    if (Mathf.Abs(overtakeDir) < 0.01f) overtakeDir = variationSeed % 2 == 0 ? 1f : -1f;
                    overtakeBlockerRoad = blockerRoadDistance;
                    overtakeDriftTarget = OvertakeDrift(overtakeDir);
                }
                else if (overtakeBlockerRoad > -1e8f && RoadDistance < overtakeBlockerRoad + 9f)
                {
                    overtakeDriftTarget = OvertakeDrift(overtakeDir);
                }
                else
                {
                    overtakeDriftTarget = 0f;
                    overtakeBlockerRoad = float.MinValue;
                }
                // A spooked runner pulls away hard; a hit-and-runner runs even harder.
                // Reckless, not blind: with a wreck or slower car right on top of them
                // they still brake, so a chase stays possible instead of ending in an
                // instant self-inflicted pileup.
                if (IsFleeing)
                {
                    var runSpeed = Mathf.Min(IsHitAndRunner ? HitAndRunMaxKph : FleeMaxKph,
                                             cruiseSpeedKph * (IsHitAndRunner ? 1.6f : 1.45f));
                    desiredSpeed = nearestGap < 12f
                        ? Mathf.Min(desiredSpeed, runSpeed)
                        : Mathf.Max(desiredSpeed, runSpeed);
                }
                currentSpeedKph = Mathf.MoveTowards(currentSpeedKph, desiredSpeed, acceleration * Time.deltaTime);
                var step = ClampToStopLine(RoadDistance, Direction * currentSpeedKph / 3.6f * Time.deltaTime);
                RoadDistance = RoadPath.Wrap(RoadDistance + step);
            }
            else
            {
                currentSpeedKph = Mathf.MoveTowards(currentSpeedKph, 0f, 36f * Time.deltaTime);
                laneDrift = Mathf.MoveTowards(laneDrift, wreckSlideTarget, 8f * Time.deltaTime);
                WreckYaw = Mathf.MoveTowards(WreckYaw, wreckYawTarget, 120f * Time.deltaTime);
                RoadDistance = RoadPath.Wrap(RoadDistance + Direction * currentSpeedKph / 3.6f * Time.deltaTime);
            }

            // Separation deliberately does NOT run here. Update order across cars is
            // arbitrary, so a car resolved early in the frame was resolved against the
            // *previous* positions of every car whose Update had not run yet - it was
            // pushed clear of where they used to be and left sitting inside where they
            // now are. All the movement finishes first; LateUpdate then separates
            // everything once, against final positions.
            separation = Mathf.MoveTowards(separation, 0f, SeparationRelax * Time.deltaTime);
            TickAirtime(Time.deltaTime);
        }

        /// Geometry is resolved by the shared pass across every vehicle on the road -
        /// traffic, police and the player alike - then this car places itself. Reactions
        /// (crashes, pileup damage) stay here because they are traffic's own rules.
        private void LateUpdate()
        {
            // A ragdolled car is the physics engine's now; placing it on the road would
            // yank it straight back out of its own crash.
            if (Ragdolled) return;

            VehicleContacts.ResolveOncePerFrame();
            if (reactionFrame != Time.frameCount)
            {
                reactionFrame = Time.frameCount;
                ReactToContacts();
            }
            PlaceOnRoad();
        }

        private void OnEnable() => VehicleContacts.Register(this);
        private void OnDisable() => VehicleContacts.Unregister(this);

        private static int reactionFrame = -1;

        /// Each pair visited once. Runs after the separation pass, so a pair still
        /// overlapping here is genuinely in contact rather than mid-correction.
        private static void ReactToContacts()
        {
            for (var a = 0; a < ActiveCars.Count; a++)
            {
                var first = ActiveCars[a];
                if (first == null) continue;
                for (var b = a + 1; b < ActiveCars.Count; b++)
                {
                    var second = ActiveCars[b];
                    if (second == null) continue;

                    var deltaDist = second.RoadDistance - first.RoadDistance;
                    if (Mathf.Abs(deltaDist) > first.LongitudinalExtent + second.LongitudinalExtent) continue;
                    var deltaLat = second.LaneOffset - first.LaneOffset;
                    if (Mathf.Abs(deltaLat) > first.LateralExtent + second.LateralExtent) continue;
                    if (Mathf.Abs(first.verticalOffset - second.verticalOffset) > 1.6f) continue;

                    first.ReactToContact(second, deltaDist, deltaLat);
                }
            }
        }

        private void ReactToContact(TrafficCarController other, float deltaDist, float deltaLat)
        {
            var relativeSpeed = Mathf.Abs(currentSpeedKph - other.currentSpeedKph);
            if (IsWreck || other.IsWreck || relativeSpeed > 18f)
            {
                var impact = Mathf.Max(currentSpeedKph, other.currentSpeedKph);
                var alreadyWrecked = IsWreck && other.IsWreck;
                // A hit-and-run runner never self-wrecks in traffic. Its whole weave
                // sweeps it across lanes at 100+ km/h closing speeds, so letting any
                // overlap wreck it ended the chase seconds after staging, every time.
                // It still wrecks everything it ploughs into - that is the point of it.
                var immune = Time.time < crashImmunityUntil || IsHitAndRunner;
                var otherImmune = Time.time < other.crashImmunityUntil || other.IsHitAndRunner;
                if (!other.IsWreck && !otherImmune) other.Crash(deltaLat, impact);
                if (!IsWreck && !immune) Crash(-deltaLat, impact);
                if (alreadyWrecked) return;

                var contactPoint = (transform.position + other.transform.position) * 0.5f + Vector3.up * 0.4f;
                CrashEffects.Active?.PlayAt(contactPoint);

                GameState.PileupDamage += 2500;
                GameState.BumpDaily("pileup", 2500);
            }
            else if (deltaDist * Direction > 0f)
            {
                currentSpeedKph = Mathf.Min(currentSpeedKph, other.currentSpeedKph);
            }
        }

        // --- Airborne -----------------------------------------------------------
        // Traffic had no vertical axis at all: PlaceOnRoad pinned every car to a fixed
        // 0.16 m, so a ramp could only ever be driven through. Ramps are part of the
        // road, and the road is shared, so traffic gets the same launch the player has.
        private float verticalOffset;
        private float verticalVelocity;
        private const float Gravity = 26f;

        public bool IsAirborne => verticalOffset > 0.05f;

        public void LaunchAirtime(float power)
        {
            if (IsAirborne || IsWreck) return;
            verticalVelocity = power;
            verticalOffset = 0.05f;
        }

        private void TickAirtime(float delta)
        {
            if (!IsAirborne && verticalVelocity <= 0f) return;
            verticalVelocity -= Gravity * delta;
            verticalOffset += verticalVelocity * delta;
            if (verticalOffset > 0f) return;

            verticalOffset = 0f;
            // A car that lands hard enough spins out - the landing is the payoff, and it
            // gives the player something to weave through afterwards.
            var wasFalling = verticalVelocity;
            verticalVelocity = 0f;
            if (wasFalling < -14f && !IsWreck) Crash(Random.Range(-1f, 1f), currentSpeedKph);
        }
        // ------------------------------------------------------------------------

        private void PlaceOnRoad()
        {
            transform.position = RoadPath.Point(RoadDistance, LaneOffset, 0.16f + verticalOffset);
            var facing = RoadPath.Rotation(RoadDistance);
            if (Direction < 0f) facing *= Quaternion.Euler(0f, 180f, 0f);
            transform.rotation = facing * Quaternion.Euler(0f, WreckYaw, wreckRoll);
        }

        private float wreckSlideTarget;
        private float wreckYawTarget;

        public void Crash(float lateralPush, float impactSpeedKph = 0f)
        {
            if (IsWreck) return;
            IsWreck = true;

            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // Momentum transfer: shove victim forward and slew it away across the asphalt.
            currentSpeedKph = Mathf.Max(currentSpeedKph * 0.4f, impactSpeedKph * 0.72f);

            var sign = Mathf.Abs(lateralPush) < 0.01f ? (variationSeed % 2 == 0 ? 1f : -1f) : Mathf.Sign(lateralPush);
            var variation = 55f + Mathf.Abs(variationSeed % 40);
            wreckYawTarget = sign * variation;
            WreckYaw = sign * variation * 0.2f;
            wreckRoll = sign * 2.5f;
            // Shove smoothly towards road shoulder
            wreckSlideTarget = Mathf.Clamp(laneDrift + sign * 5.2f, -8f, 8f);
        }

        /// Nearest violator ahead of the player, for the cinematic autopilot. Returns
        /// null if the road ahead is clear of offenders within range.
        /// Diagnostic: report cars that have stopped and what is in front of them.
        /// Per-car dump: the aggregate counts were not enough to explain the stalls.
        public static void DumpCars(float playerDistance)
        {
            foreach (var c in ActiveCars)
            {
                if (c == null) continue;
                Debug.Log($"RR_CAR {c.name,-16} spd={c.currentSpeedKph,6:0.0} cruise={c.cruiseSpeedKph,6:0.0} " +
                          $"dir={c.Direction,2:0} lane={c.LaneOffset,6:0.0} " +
                          $"relDist={(c.RoadDistance - playerDistance),7:0} " +
                          $"wreck={(c.IsWreck ? 1 : 0)} viol={c.Violation}");
            }
        }

        public static string StuckReport()
        {
            var stuck = 0;
            var wrecks = 0;
            foreach (var c in ActiveCars)
            {
                if (c == null) continue;
                if (c.IsWreck) wrecks++;
                else if (c.currentSpeedKph < 1f) stuck++;
            }
            return $"stuck={stuck} wrecks={wrecks} total={ActiveCars.Count}";
        }

        public static TrafficCarController FindViolatorAhead(float from, float lookAhead)
        {
            TrafficCarController best = null;
            var bestDist = float.PositiveInfinity;
            for (var i = 0; i < ActiveCars.Count; i++)
            {
                var car = ActiveCars[i];
                if (car == null) continue;
                if (!car.IsViolator) continue;
                var gap = car.RoadDistance - from;
                if (gap < 4f || gap > lookAhead) continue;
                if (gap < bestDist) { bestDist = gap; best = car; }
            }
            return best;
        }

        /// Closest innocent that the player is about to hit, so the pilot can swerve.
        public static TrafficCarController InnocentInPath(float from, float lateral, float lookAhead)
        {
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var car = ActiveCars[i];
                if (car == null) { ActiveCars.RemoveAt(i); continue; }
                if (car.IsViolator || car.IsWreck) continue;
                var gap = car.RoadDistance - from;
                if (gap < 2f || gap > lookAhead) continue;
                if (Mathf.Abs(car.LaneOffset - lateral) < 3.2f) return car;
            }
            return null;
        }

        /// Gameplay only. Geometry for the player is resolved by the shared vehicle
        /// pass, which includes police and traffic on the same footing.
        public static void ResolvePlayerCollision(ArcadeCarController driver)
        {
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var traffic = ActiveCars[i];
                if (traffic == null)
                {
                    ActiveCars.RemoveAt(i);
                    continue;
                }

                var longitudinal = RoadPath.SignedDelta(driver.RoadDistance, traffic.RoadDistance);
                // Hulls used to be shrunk by 4% longitudinally and 8% laterally before
                // the test, so contact only registered once the meshes had already
                // interpenetrated by that much. They are compared at full size now, with
                // a small positive skin, so the hit lands as the bumpers meet.
                var reach = driver.HalfLength + traffic.LongitudinalExtent;
                if (Mathf.Abs(longitudinal) > reach) continue;
                var lateral = traffic.LaneOffset - driver.LateralOffset;
                var lateralReach = driver.HalfWidth + traffic.LateralExtent;
                if (Mathf.Abs(lateral) > lateralReach) continue;

                // Special Vehicle: Car Hauler ramp jump from behind
                if (traffic.Role == VehicleRole.CarHauler && longitudinal > 0.4f && Mathf.Abs(lateral) < 1.4f && driver.SpeedKph > 35f)
                {
                    driver.LaunchAirtime(17.5f);
                    GameState.Award(2000, "🚀 CAR HAULER MEGA-JUMP!");
                    if (RoadRageAudioBridge.Instance != null)
                    {
                        RoadRageAudioBridge.Instance.PlayTurboFlutter();
                    }
                    continue;
                }

                // Anti-penetration is handled by the shared vehicle pass; only the
                // gameplay consequences of the contact are decided here.
                if (longitudinal > 0f)
                {
                    // Momentum transfer: the car you rear-end is carried along rather
                    // than standing still while you drive into it.
                    traffic.currentSpeedKph = Mathf.Max(traffic.currentSpeedKph, driver.SpeedKph * 0.95f);
                }

                // Special Vehicle: Explosive Fuel Tanker Detonation
                if (traffic.Role == VehicleRole.FuelTanker && driver.SpeedKph > 30f && !traffic.HasDetonated)
                {
                    traffic.DetonateTanker();
                }

                // 2. Trigger gameplay impact & damage/score events
                var speedAtImpact = driver.SpeedKph;
                driver.ApplyTrafficImpact(traffic, longitudinal, lateral);
                if (!traffic.IsWreck)
                {
                    traffic.Crash(lateral, speedAtImpact);
                }
            }
        }
    }
}
