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
        /// Above this height difference one vehicle is over the other, not into it.
        private const float ClearanceHeight = 1.6f;

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

            for (var pass = 0; pass < Passes; pass++)
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
            if (Mathf.Abs(a.ContactHeight - b.ContactHeight) > ClearanceHeight) return;

            var deltaDist = b.ContactDistance - a.ContactDistance;
            var reach = a.ContactHalfLength + b.ContactHalfLength + ContactSkin;
            if (Mathf.Abs(deltaDist) > reach) return;

            var deltaLat = b.ContactLateral - a.ContactLateral;
            var latReach = a.ContactHalfWidth + b.ContactHalfWidth + ContactSkin;
            if (Mathf.Abs(deltaLat) > latReach) return;

            var overlapLong = reach - Mathf.Abs(deltaDist);
            var overlapLat = latReach - Mathf.Abs(deltaLat);

            // Split the correction by mass so the heavier vehicle barely moves.
            var totalMass = Mathf.Max(0.01f, a.ContactMass + b.ContactMass);
            var shareA = b.ContactMass / totalMass;
            var shareB = a.ContactMass / totalMass;

            if (overlapLat < overlapLong)
            {
                var sign = deltaLat >= 0f ? 1f : -1f;
                a.ApplyContactPush(0f, -sign * overlapLat * shareA);
                b.ApplyContactPush(0f, sign * overlapLat * shareB);
            }
            else
            {
                var sign = deltaDist >= 0f ? 1f : -1f;
                a.ApplyContactPush(-sign * overlapLong * shareA, 0f);
                b.ApplyContactPush(sign * overlapLong * shareB, 0f);
            }
        }
    }
}
