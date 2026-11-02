using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Anything that occupies space on the road and can be pushed out of another
    /// vehicle's way.
    ///
    /// The game grew three separate movement systems - traffic, police and the player -
    /// and each one carried its own copy of the same anti-penetration code against its
    /// own idea of how big a car is. Traffic resolved against traffic, police resolved
    /// against police and the player, and nothing ever resolved police against traffic,
    /// which is why cruisers drove straight through the cars they were chasing. The
    /// hulls disagreed too: police used a hardcoded 4.8 m reach while traffic measured
    /// its own mesh.
    ///
    /// One contract, one registry, one pass. A car is a car, whoever is driving it.
    public interface IRoadVehicle
    {
        /// Position along the road centreline, metres.
        float ContactDistance { get; }
        /// Signed offset across the carriageway, metres.
        float ContactLateral { get; }
        /// Half-extents already projected onto the road axes, so a slewed wreck reports
        /// the box it actually sweeps rather than its own body dimensions.
        float ContactHalfLength { get; }
        float ContactHalfWidth { get; }
        /// Height above the road surface. Vehicles clear each other when one is airborne.
        float ContactHeight { get; }
        /// Relative resistance to being shoved. A pushed vehicle absorbs a share of the
        /// correction inversely proportional to this, so a hatchback gives way to a
        /// lorry and the player is never bullied off their line by scenery traffic.
        float ContactMass { get; }
        /// False while something else owns the transform - a crash tumble, a disabled
        /// controller, a wreck being animated out.
        bool ContactActive { get; }

        /// Absorb a correction. Each implementer decides where it goes: traffic writes
        /// to its separation channel so behaviour cannot overwrite it, police and the
        /// player write straight to their lateral offset.
        void ApplyContactPush(float alongRoad, float acrossRoad);
    }

    public static class VehicleContacts
    {
        private static readonly List<IRoadVehicle> Registered = new();
        private static int resolvedFrame = -1;

        /// Relaxation, not a single shot: pushing A off B can put A inside C, and in a
        /// pileup that cascades. Three passes settle a dense cluster.
        private const int Passes = 3;
        /// Small positive gap so hulls rest touching rather than exactly coincident,
        /// which is what reads as clipping when two cars sit side by side.
        private const float ContactSkin = 0.08f;
        private const float PlayerShoveShare = 0.3f;
        /// Above this height difference one vehicle is over the other, not into it.
        private const float ClearanceHeight = 1.6f;
        /// Above this many vehicles the pass drops to a single relaxation.
        /// A five-star pursuit puts ~20 vehicles on the road with the wrecks, and with a
        /// single pass a cruiser pushed out of a wreck into its neighbour was pushed
        /// straight back in: it drove through. 40 vehicles is 780 pairs, still cheap.
        private const int CrowdedSet = 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Registered.Clear();
            resolvedFrame = -1;
        }

        public static void Register(IRoadVehicle vehicle)
        {
            if (vehicle != null && !Registered.Contains(vehicle)) Registered.Add(vehicle);
        }

        public static void Unregister(IRoadVehicle vehicle) => Registered.Remove(vehicle);

        /// Call from LateUpdate. Movement for every vehicle has finished by then, so the
        /// pass resolves against final positions instead of a mix of this frame's and
        /// last frame's. The frame gate means it runs once however many vehicles call it.
        public static void ResolveOncePerFrame()
        {
            if (resolvedFrame == Time.frameCount) return;
            resolvedFrame = Time.frameCount;

            for (var i = Registered.Count - 1; i >= 0; i--)
                if (Registered[i] == null || Registered[i] is Object o && o == null)
                    Registered.RemoveAt(i);

            // Pairs are O(n^2) and the passes multiply it: 28 vehicles is 378 pairs,
            // 1134 tests a frame at three passes. Under a heavy set one pass still
            // separates the common case - a second body arriving in an already-clear
            // gap - and only a dense pileup needs the extra relaxation it gives up.
            var passes = Registered.Count > CrowdedSet ? 1 : Passes;
            for (var pass = 0; pass < passes; pass++)
                for (var a = 0; a < Registered.Count; a++)
                    for (var b = a + 1; b < Registered.Count; b++)
                        Separate(Registered[a], Registered[b]);
        }

        /// Minimum-translation resolution: push apart along whichever road axis is
        /// overlapping least. Correcting both at once shunts a car that is merely
        /// alongside another bodily up the road.
        private static void Separate(IRoadVehicle a, IRoadVehicle b)
        {
            if (!a.ContactActive || !b.ContactActive) return;
            if (SameRig(a, b) || PlayerGoesThrough(a, b)) return;
            if (Mathf.Abs(a.ContactHeight - b.ContactHeight) > ClearanceHeight) return;

            var deltaDist = b.ContactDistance - a.ContactDistance;
            var reach = a.ContactHalfLength + b.ContactHalfLength + ContactSkin;
            if (Mathf.Abs(deltaDist) > reach) return;

            var deltaLat = b.ContactLateral - a.ContactLateral;
            var latReach = a.ContactHalfWidth + b.ContactHalfWidth + ContactSkin;
            if (Mathf.Abs(deltaLat) > latReach) return;

            // Noted here, not in the gameplay test: a folded trailer's box here is wider
            // than its true shape there, so the player could be held by this one forever.
            if (a is ArcadeCarController playerA) NotePlayerAgainst(b, playerA.LateralOffset);
            else if (b is ArcadeCarController playerB) NotePlayerAgainst(a, playerB.LateralOffset);

            var overlapLong = reach - Mathf.Abs(deltaDist);
            var overlapLat = latReach - Mathf.Abs(deltaLat);

            // Split the correction by mass so the heavier vehicle barely moves.
            var totalMass = Mathf.Max(0.01f, a.ContactMass + b.ContactMass);
            var shareA = b.ContactMass / totalMass;
            var shareB = a.ContactMass / totalMass;
            // The player ploughs a wreck aside: it takes all of the correction.
            // Not quite all: a wreck's sideways push has a limit, and past it the player sat inside.
            if (a is ArcadeCarController && b is TrafficCarController wreckB && wreckB.PlayerShoves) { shareA = PlayerShoveShare; shareB = 1f - PlayerShoveShare; }
            else if (b is ArcadeCarController && a is TrafficCarController wreckA && wreckA.PlayerShoves) { shareA = 1f - PlayerShoveShare; shareB = PlayerShoveShare; }

            if (overlapLat < overlapLong)
            {
                var sign = deltaLat >= 0f ? 1f : -1f;
                var pushA = -sign * overlapLat * shareA;
                var pushB = sign * overlapLat * shareB;
                // Sideways only where the road has room for it. A car beside a wreck at
                // the edge was pushed out past the kerb, the edge clamp put it straight
                // back, and it sat inside the wreck: cars drove through a car lying on
                // its side. Held back along the road instead, it stops against it.
                if (FitsOnRoad(a, pushA) && FitsOnRoad(b, pushB))
                {
                    a.ApplyContactPush(0f, pushA);
                    b.ApplyContactPush(0f, pushB);
                    return;
                }
                // A wreck already against the rail: the player slides off it instead of
                // ploughing it along the road for ever.
                if (a is ArcadeCarController && shareA == PlayerShoveShare)
                {
                    a.ApplyContactPush(0f, -sign * overlapLat);
                    return;
                }
                if (b is ArcadeCarController && shareB == PlayerShoveShare)
                {
                    b.ApplyContactPush(0f, sign * overlapLat);
                    return;
                }
            }
            var along = deltaDist >= 0f ? 1f : -1f;
            a.ApplyContactPush(-along * overlapLong * shareA, 0f);
            b.ApplyContactPush(along * overlapLong * shareB, 0f);
        }

        /// A folded rig's cab and trailer are two bodies, and they overlap at the hitch.
        /// The trailer takes no push and follows the cab, so separating them shoved the
        /// cab up the road every frame, for ever: a wrecked truck shot off at thousands
        /// of km/h, leaving its logs behind.
        private static bool SameRig(IRoadVehicle a, IRoadVehicle b) =>
            (a is TrailerHitch ha && ReferenceEquals(ha.Rig, b)) ||
            (b is TrailerHitch hb && ReferenceEquals(hb.Rig, a));

        /// A wreck the player has been stuck against no longer blocks the player.
        private static bool PlayerGoesThrough(IRoadVehicle a, IRoadVehicle b) =>
            (a is ArcadeCarController && WreckLetsPlayerThrough(b)) ||
            (b is ArcadeCarController && WreckLetsPlayerThrough(a));

        private static bool WreckLetsPlayerThrough(IRoadVehicle vehicle) =>
            vehicle is TrailerHitch hitch ? hitch.Rig != null && hitch.Rig.PlayerPassing
            : vehicle is TrafficCarController car && car.PlayerPassing;

        private static void NotePlayerAgainst(IRoadVehicle vehicle, float playerLateral)
        {
            if (vehicle is TrailerHitch hitch) hitch.Rig?.PlayerPressing(playerLateral);
            else if (vehicle is TrafficCarController car) car.PlayerPressing(playerLateral);
        }

        /// Whether a sideways push leaves the hull within the carriageway and shoulder,
        /// where every controller clamps it. Moving back towards the road always fits.
        private static bool FitsOnRoad(IRoadVehicle vehicle, float push)
        {
            if (Mathf.Abs(push) < 0.001f) return true;
            var lateral = vehicle.ContactLateral + push;
            if (Mathf.Abs(lateral) <= Mathf.Abs(vehicle.ContactLateral)) return true;
            var d = vehicle.ContactDistance;
            var edge = Mathf.Max(1f, RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth - vehicle.ContactHalfWidth);
            return Mathf.Abs(lateral) <= edge + 0.02f;
        }
    }
}
