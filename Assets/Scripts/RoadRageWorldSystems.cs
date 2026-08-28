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
        private float weavePhase;
        private float weaveRate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => ActiveCars.Clear();

        public void Initialize(float distance, float lane, float speedKph, float direction,
            bool wreck = false, float wreckYaw = 0f, Offence violation = Offence.None)
        {
            Violation = wreck ? Offence.None : violation;
            weavePhase = Random.Range(0f, Mathf.PI * 2f);
            weaveRate = Random.Range(0.55f, 0.95f);
            if (Violation == Offence.Speeding) speedKph *= 1.55f;
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
            switch (Violation)
            {
                case Offence.Weaving:
                    // Wide, lazy lane drift - the most readable tell at speed.
                    weavePhase += delta * weaveRate;
                    laneDrift = Mathf.Sin(weavePhase) * 5.2f;
                    break;
                case Offence.Tailgating:
                    laneDrift = Mathf.Lerp(laneDrift, 0f, delta * 2f);
                    break;
                default:
                    laneDrift = Mathf.Lerp(laneDrift, 0f, delta * 2f);
                    break;
            }
        }

        private void OnDestroy() => ActiveCars.Remove(this);

        /// Set by the streamer each frame. On an open road traffic can no longer wrap,
        /// so cars that fall too far behind are recycled ahead of the player instead.
        public static float PlayerDistance;
        private const float RecycleBehind = 140f;
        private const float RecycleAhead = 420f;

        private void Recycle()
        {
            if (Direction >= 0f)
            {
                // Same-direction traffic overtaken by the player reappears up ahead.
                if (RoadDistance < PlayerDistance - RecycleBehind)
                    RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.55f, RecycleAhead);
            }
            else
            {
                // Oncoming traffic that has passed comes back from further up the road.
                if (RoadDistance < PlayerDistance - RecycleBehind)
                    RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.7f, RecycleAhead * 1.4f);
            }

            if (RoadDistance > PlayerDistance + RecycleAhead * 1.6f)
                RoadDistance = PlayerDistance + Random.Range(RecycleAhead * 0.4f, RecycleAhead);

            if (!IsWreck) return;
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
            Recycle();
            UpdateOffenceBehaviour(Time.deltaTime);
            if (!IsWreck)
            {
                var desiredSpeed = cruiseSpeedKph;
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

                    // Lane offsets are fractions of the carriageway, so on a narrow road
                    // all six lanes compress into ~4 m and a fixed 2.25 m footprint made
                    // every car block every other one. The result was a single queue that
                    // deadlocked within seconds of spawning.
                    var laneSpacing = Mathf.Max(1.2f, RoadPath.HalfWidthAt(RoadDistance) * 0.5f);
                    var lateralFootprint = Mathf.Min(other.IsWreck ? 4.0f : 2.25f,
                                                     laneSpacing * (other.IsWreck ? 1.4f : 0.8f));
                    if (Mathf.Abs(other.LaneOffset - LaneOffset) > lateralFootprint) continue;
                    var gap = RoadPath.ForwardGap(RoadDistance, other.RoadDistance, Direction);
                    if (gap <= 0.05f || gap >= 38f) continue;

                    var obstacleSpeed = other.IsWreck ? 0f : other.currentSpeedKph;
                    // Follow at the obstacle's speed rather than stopping dead. Only a
                    // genuinely stationary obstacle (a wreck) brings traffic to a halt,
                    // otherwise a close gap froze the whole queue permanently.
                    var safeSpeed = gap < 10f
                        ? obstacleSpeed * 0.9f
                        : Mathf.Lerp(Mathf.Max(obstacleSpeed * 0.9f, 18f), Mathf.Max(24f, obstacleSpeed),
                                     Mathf.InverseLerp(10f, 38f, gap));
                    desiredSpeed = Mathf.Min(desiredSpeed, safeSpeed);
                }

                var acceleration = desiredSpeed < currentSpeedKph ? 55f : 16f;
                currentSpeedKph = Mathf.MoveTowards(currentSpeedKph, desiredSpeed, acceleration * Time.deltaTime);
                RoadDistance = RoadPath.Wrap(RoadDistance + Direction * currentSpeedKph / 3.6f * Time.deltaTime);
                EnforceSeparation();
            }
            else
            {
                // Carry the impact momentum, slide out of the lane and slew round before
                // coming to rest. A wreck used to freeze on the spot the instant it was
                // hit, which is what made impacts read as clipping rather than collision.
                currentSpeedKph = Mathf.MoveTowards(currentSpeedKph, 0f, 42f * Time.deltaTime);
                laneDrift = Mathf.MoveTowards(laneDrift, wreckSlideTarget, 7f * Time.deltaTime);
                WreckYaw = Mathf.MoveTowards(WreckYaw, wreckYawTarget, 70f * Time.deltaTime);
                RoadDistance = RoadPath.Wrap(RoadDistance + Direction * currentSpeedKph / 3.6f * Time.deltaTime);
            }

            PlaceOnRoad();
        }

        /// Speed control alone never prevented overlap: a car braking to zero still ends
        /// up inside the one ahead when the gap closes faster than it can decelerate, and
        /// recycled cars can spawn on top of each other. This is the hard constraint -
        /// after moving, no two cars sharing a lane may occupy the same metre of road.
        private const float CarLength = 4.9f;

        private void EnforceSeparation()
        {
            // Lane offsets are fractions of the carriageway, so on a narrow two-lane road
            // every lane collapses to within ~2 m of every other. A fixed 2.1 m threshold
            // therefore treated ALL cars - including oncoming - as sharing a lane, so they
            // shoved each other every frame and mutually clamped to a stop. That was the
            // shaking, stationary traffic in Greenwood.
            var laneSpacing = Mathf.Max(1.2f, RoadPath.HalfWidthAt(RoadDistance) * 0.5f);
            var sameLane = Mathf.Min(2.1f, laneSpacing * 0.8f);

            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var other = ActiveCars[i];
                if (other == null) { ActiveCars.RemoveAt(i); continue; }
                if (other == this) continue;
                // Oncoming traffic passes; it must never be separated against.
                if (!other.IsWreck && Mathf.Sign(other.Direction) != Mathf.Sign(Direction)) continue;
                if (Mathf.Abs(other.LaneOffset - LaneOffset) > sameLane) continue;

                var delta = other.RoadDistance - RoadDistance;
                if (Mathf.Abs(delta) >= CarLength) continue;

                // Only the car behind yields, and it moves at most a little per frame, so
                // a queue settles instead of two cars trading pushes.
                var behind = Mathf.Sign(delta) * Direction > 0f;
                if (!behind) continue;
                var overlap = CarLength - Mathf.Abs(delta);
                RoadDistance -= Direction * Mathf.Min(overlap, 12f * Time.deltaTime);
                if (!IsWreck) currentSpeedKph = Mathf.Min(currentSpeedKph, other.currentSpeedKph);
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

            // Momentum transfer. Zeroing the speed made the victim stop dead while the
            // player was still doing 120 km/h, so the player drove through the mesh -
            // the "cars clip through each other" bug. A rammed car is shoved forward and
            // slews away; it does not become an instant wall.
            currentSpeedKph = Mathf.Max(currentSpeedKph, impactSpeedKph * 0.88f);

            var sign = Mathf.Abs(lateralPush) < 0.01f ? (variationSeed % 2 == 0 ? 1f : -1f) : Mathf.Sign(lateralPush);
            var variation = 24f + Mathf.Abs(variationSeed % 23);
            wreckYawTarget = sign * variation;
            WreckYaw = sign * variation * 0.25f;
            wreckRoll = sign * 3.5f;
            // 0.65 m cannot separate two ~2 m wide cars; shove it clear of the lane.
            wreckSlideTarget = Mathf.Clamp(laneDrift + sign * 4.5f, -7f, 7f);
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
            var stuck = 0; var wreckBlocked = 0; var carBlocked = 0; var wrecks = 0;
            foreach (var c in ActiveCars)
            {
                if (c == null) continue;
                if (c.IsWreck) { wrecks++; continue; }
                if (c.currentSpeedKph > 5f) continue;
                stuck++;
                TrafficCarController blocker = null; var best = float.PositiveInfinity;
                foreach (var o in ActiveCars)
                {
                    if (o == null || o == c) continue;
                    if (Mathf.Sign(o.Direction) != Mathf.Sign(c.Direction) && !o.IsWreck) continue;
                    if (Mathf.Abs(o.LaneOffset - c.LaneOffset) > 3f) continue;
                    var gap = (o.RoadDistance - c.RoadDistance) * c.Direction;
                    if (gap <= 0f || gap > 40f) continue;
                    if (gap < best) { best = gap; blocker = o; }
                }
                if (blocker != null && blocker.IsWreck) wreckBlocked++;
                else if (blocker != null) carBlocked++;
            }
            return $"stuck={stuck} behindWreck={wreckBlocked} behindCar={carBlocked} " +
                   $"wrecks={wrecks} total={ActiveCars.Count}";
        }

        public static TrafficCarController FindViolatorAhead(float from, float lookAhead)
        {
            TrafficCarController best = null;
            var bestGap = float.PositiveInfinity;
            for (var i = ActiveCars.Count - 1; i >= 0; i--)
            {
                var car = ActiveCars[i];
                if (car == null) { ActiveCars.RemoveAt(i); continue; }
                if (!car.IsViolator) continue;
                var gap = car.RoadDistance - from;
                if (gap < 6f || gap > lookAhead) continue;
                if (gap < bestGap) { bestGap = gap; best = car; }
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
                // Real footprints, not fixed radii: contact happens when the two hulls
                // touch, which is where the visual impact is.
                var reach = player.HalfLength + traffic.HalfLength;
                if (Mathf.Abs(longitudinal) > reach) continue;
                var lateral = traffic.LaneOffset - player.LateralOffset;
                // 0.9 keeps a sliver of squeeze-past tolerance so brushing a mirror is
                // not a full collision; a wreck lying askew presents a wider profile.
                var lateralReach = (player.HalfWidth + traffic.HalfWidth) * (traffic.IsWreck ? 1.25f : 0.9f);
                if (Mathf.Abs(lateral) > lateralReach) continue;

                var speedAtImpact = player.SpeedKph;
                if (player.ApplyTrafficImpact(traffic, longitudinal, lateral) && !traffic.IsWreck)
                    traffic.Crash(lateral, speedAtImpact);
                return;
            }
        }
    }
}
