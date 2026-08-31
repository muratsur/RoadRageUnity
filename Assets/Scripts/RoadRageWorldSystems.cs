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

    public sealed class TrafficCarController : MonoBehaviour
    {
        private static readonly List<TrafficCarController> ActiveCars = new();
        /// Read-only view for the HUD's quarry markers and the capture self-test.
        public static IReadOnlyList<TrafficCarController> Active => ActiveCars;

        public float RoadDistance { get; private set; }
        /// Signed fraction of the carriageway, -1 (outer left) to 1 (outer right).
        public float LaneFraction { get; private set; }
        public float LaneOffset => RoadPath.LaneLateral(RoadDistance, LaneFraction) + laneDrift;
        private float laneDrift;
        public float Direction { get; private set; }
        public bool IsWreck { get; private set; }
        public float WreckYaw { get; private set; }

        private float cruiseSpeedKph;
        private float currentSpeedKph;
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

        private void Update()
        {
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
                    if (!other.IsWreck && Mathf.Sign(other.Direction) != Mathf.Sign(Direction)) continue;

                    // Tailgaters deliberately close the gap; speeders do not yield.
                    if (Violation == Offence.Tailgating && !other.IsWreck) continue;

                    var laneSpacing = Mathf.Max(1.2f, RoadPath.HalfWidthAt(RoadDistance) * 0.5f);
                    var lateralFootprint = Mathf.Min(other.IsWreck ? 4.0f : 2.25f,
                                                     laneSpacing * (other.IsWreck ? 1.4f : 0.8f));
                    if (Mathf.Abs(other.LaneOffset - LaneOffset) > lateralFootprint) continue;
                    var gap = RoadPath.ForwardGap(RoadDistance, other.RoadDistance, Direction);
                    if (gap <= 0.05f || gap >= 38f) continue;
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
                RoadDistance = RoadPath.Wrap(RoadDistance + Direction * currentSpeedKph / 3.6f * Time.deltaTime);
            }
            else
            {
                currentSpeedKph = Mathf.MoveTowards(currentSpeedKph, 0f, 36f * Time.deltaTime);
                laneDrift = Mathf.MoveTowards(laneDrift, wreckSlideTarget, 8f * Time.deltaTime);
                WreckYaw = Mathf.MoveTowards(WreckYaw, wreckYawTarget, 120f * Time.deltaTime);
                RoadDistance = RoadPath.Wrap(RoadDistance + Direction * currentSpeedKph / 3.6f * Time.deltaTime);
            }

            ResolveTrafficCollisions();
            PlaceOnRoad();
        }

        private void ResolveTrafficCollisions()
        {
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var other = ActiveCars[i];
                if (other == null) { ActiveCars.RemoveAt(i); continue; }
                if (other == this) continue;

                var deltaDist = other.RoadDistance - RoadDistance;
                var reach = (HalfLength + other.HalfLength) * 0.96f;
                if (Mathf.Abs(deltaDist) > reach) continue;

                var deltaLat = other.LaneOffset - LaneOffset;
                var latReach = (HalfWidth + other.HalfWidth) * (IsWreck || other.IsWreck ? 1.15f : 0.92f);
                if (Mathf.Abs(deltaLat) > latReach) continue;

                // 1. Instant anti-penetration pushback so vehicles NEVER clip inside each other
                var overlapDist = reach - Mathf.Abs(deltaDist);
                if (deltaDist > 0f)
                {
                    other.RoadDistance += overlapDist * 0.5f;
                    RoadDistance -= overlapDist * 0.5f;
                }
                else
                {
                    RoadDistance += overlapDist * 0.5f;
                    other.RoadDistance -= overlapDist * 0.5f;
                }

                var overlapLat = latReach - Mathf.Abs(deltaLat);
                var latSign = Mathf.Sign(deltaLat);
                if (Mathf.Abs(latSign) < 0.01f) latSign = 1f;
                other.laneDrift += latSign * overlapLat * 0.5f;
                laneDrift -= latSign * overlapLat * 0.5f;

                // 2. Dynamic Kinetic Collision & Chain-Reaction Pileups.
                // A hit-and-run runner never self-wrecks in traffic: its whole weave
                // sweeps it across lanes at 100+ km/h closing speeds, and letting any
                // overlap wreck it killed the chase seconds after staging every time.
                // It still wrecks everyone it ploughs into - that is the point.
                var immune = Time.time < crashImmunityUntil || IsHitAndRunner;
                var relativeSpeed = Mathf.Abs(currentSpeedKph - other.currentSpeedKph);
                if (IsWreck || other.IsWreck || relativeSpeed > 18f)
                {
                    if (!other.IsWreck && !(Time.time < other.crashImmunityUntil))
                    {
                        other.Crash(deltaLat, Mathf.Max(currentSpeedKph, other.currentSpeedKph));
                    }
                    if (!IsWreck && !immune)
                    {
                        Crash(-deltaLat, Mathf.Max(currentSpeedKph, other.currentSpeedKph));
                    }

                    var contactPoint = (transform.position + other.transform.position) * 0.5f + Vector3.up * 0.4f;
                    CrashEffects.Active?.PlayAt(contactPoint);

                    GameState.PileupDamage += 2500;
                    GameState.BumpDaily("pileup", 2500);
                }
                else
                {
                    if (deltaDist * Direction > 0f)
                    {
                        currentSpeedKph = Mathf.Min(currentSpeedKph, other.currentSpeedKph);
                    }
                }
            }
        }

        private void PlaceOnRoad()
        {
            transform.position = RoadPath.Point(RoadDistance, LaneOffset, 0.16f);
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

        public static void ResolvePlayerCollision(ArcadeCarController player)
        {
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var traffic = ActiveCars[i];
                if (traffic == null)
                {
                    ActiveCars.RemoveAt(i);
                    continue;
                }

                var longitudinal = RoadPath.SignedDelta(player.RoadDistance, traffic.RoadDistance);
                var reach = (player.HalfLength + traffic.HalfLength) * 0.96f;
                if (Mathf.Abs(longitudinal) > reach) continue;
                var lateral = traffic.LaneOffset - player.LateralOffset;
                var lateralReach = (player.HalfWidth + traffic.HalfWidth) * (traffic.IsWreck ? 1.15f : 0.92f);
                if (Mathf.Abs(lateral) > lateralReach) continue;

                // Special Vehicle: Car Hauler ramp jump from behind
                if (traffic.Role == VehicleRole.CarHauler && longitudinal > 0.4f && Mathf.Abs(lateral) < 1.4f && player.SpeedKph > 35f)
                {
                    player.LaunchAirtime(17.5f);
                    GameState.Award(2000, "🚀 CAR HAULER MEGA-JUMP!");
                    if (RoadRageAudioBridge.Instance != null)
                    {
                        RoadRageAudioBridge.Instance.PlayTurboFlutter();
                    }
                    continue;
                }

                // 1. Continuous physical anti-penetration resolution (runs every frame to prevent any mesh clipping)
                var overlapLong = reach - Mathf.Abs(longitudinal);
                if (longitudinal > 0f) // Player is behind traffic: shove traffic car forward
                {
                    traffic.RoadDistance += overlapLong * 0.6f;
                    player.RoadDistance -= overlapLong * 0.4f;
                    // Transfer momentum so traffic car speeds up ahead of player instead of player driving through it
                    traffic.currentSpeedKph = Mathf.Max(traffic.currentSpeedKph, player.SpeedKph * 0.95f);
                }
                else // Player is in front of traffic
                {
                    player.RoadDistance += overlapLong * 0.6f;
                    traffic.RoadDistance -= overlapLong * 0.4f;
                }

                var overlapLat = lateralReach - Mathf.Abs(lateral);
                var latSign = Mathf.Sign(lateral);
                if (Mathf.Abs(latSign) < 0.01f) latSign = 1f;
                traffic.laneDrift += latSign * overlapLat * 0.65f;
                player.LateralOffset -= latSign * overlapLat * 0.35f;

                // Special Vehicle: Explosive Fuel Tanker Detonation
                if (traffic.Role == VehicleRole.FuelTanker && player.SpeedKph > 30f && !traffic.HasDetonated)
                {
                    traffic.DetonateTanker();
                }

                // 2. Trigger gameplay impact & damage/score events
                var speedAtImpact = player.SpeedKph;
                player.ApplyTrafficImpact(traffic, longitudinal, lateral);
                if (!traffic.IsWreck)
                {
                    traffic.Crash(lateral, speedAtImpact);
                }
            }
        }
    }
}
