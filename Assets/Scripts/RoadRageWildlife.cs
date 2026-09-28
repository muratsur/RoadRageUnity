using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Deer on the B500. The Wildwechsel signs are there for a reason: at dawn and dusk
    /// red deer cross the road out of the forest, one, two or three at a time. A deer
    /// waits at the edge until the player is close, then leaps across in front of them.
    /// Hitting one costs integrity; threading between them scores. Only on the route.
    public sealed class RoadRageWildlife : MonoBehaviour
    {
        private sealed class Deer
        {
            public Transform Body;
            public float Distance;
            public float Lateral;
            public float Delay;
            public bool Running;
            public bool Hit;
            public bool Passed;
            public Vector3 Fling;
            public Vector3 Spin;
            public float Age;
        }

        private const float RunSpeed = 9f;          // m/s, a deer in full flight
        private const float TriggerRange = 58f;     // starts to run when the player is this close

        private readonly List<Deer> herd = new();
        private ArcadeCarController car;
        private float nextCrossing = -1f;
        private int side = 1;

        private void Update()
        {
            if (RoadPath.Route == null)
            {
                Clear();
                return;
            }
            if (car == null)
            {
                car = FindAnyObjectByType<ArcadeCarController>();
                if (car == null) return;
            }
            var landing = RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive;
            if (landing || car.CountdownTimer > 0f)
            {
                Clear();
                nextCrossing = -1f;
                return;
            }
            if (nextCrossing < 0f) nextCrossing = car.RoadDistance + Gap();

            if (herd.Count == 0 && car.RoadDistance > nextCrossing) Spawn();
            if (herd.Count == 0) return;

            var dt = Time.deltaTime;
            var done = true;
            foreach (var deer in herd)
            {
                if (deer.Body == null) continue;
                deer.Age += dt;
                if (deer.Hit)
                {
                    // Thrown by the impact.
                    deer.Fling += Physics.gravity * dt;
                    deer.Body.position += deer.Fling * dt;
                    deer.Body.rotation *= Quaternion.Euler(deer.Spin * dt);
                    if (deer.Body.position.y < RoadPath.Center(deer.Distance).y - 1f) deer.Fling = Vector3.zero;
                    continue;
                }

                if (!deer.Running && deer.Distance - car.RoadDistance < TriggerRange)
                {
                    deer.Delay -= dt;
                    if (deer.Delay <= 0f) deer.Running = true;
                }
                var before = deer.Lateral - car.LateralOffset;
                if (deer.Running) deer.Lateral -= side * RunSpeed * dt;
                var after = deer.Lateral - car.LateralOffset;

                // Bounding: the body rises and falls with each leap.
                var bound = deer.Running ? Mathf.Abs(Mathf.Sin(deer.Age * 7.5f)) * 0.45f : 0f;
                deer.Body.position = RoadPath.Point(deer.Distance, deer.Lateral, bound);
                var right = RoadPath.Right(deer.Distance);
                var heading = new Vector3(-side * right.x, 0f, -side * right.z);
                deer.Body.rotation = Quaternion.LookRotation(heading.normalized) *
                                     Quaternion.Euler(deer.Running ? -8f * Mathf.Cos(deer.Age * 7.5f) : 0f, 0f, 0f);

                var along = deer.Distance - car.RoadDistance;
                if (Mathf.Abs(along) < 2.6f && Mathf.Abs(after) < 1.3f && bound < 0.9f)
                {
                    Strike(deer);
                    continue;
                }
                // Crossed the player's line close in front: a dodge.
                if (!deer.Passed && deer.Running && Mathf.Sign(before) != Mathf.Sign(after) && along > -3f && along < 14f)
                {
                    deer.Passed = true;
                    GameState.Award(400, "🦌 DEER DODGED");
                    RoadRageHaptics.Tick();
                }
                var clear = Mathf.Abs(deer.Lateral) > RoadPath.HalfWidthAt(deer.Distance) + RoadPath.ShoulderWidth + 14f
                            && Mathf.Sign(deer.Lateral) == -side;
                if (!clear) done = false;
            }

            var behind = car.RoadDistance - herd[0].Distance > 70f;
            if (done || behind)
            {
                Clear();
                nextCrossing = car.RoadDistance + Gap();
            }
        }

        /// Crossings come more often at dawn and dusk, when deer are out.
        private float Gap()
        {
            var t = RoadRageBootstrap.Instance != null ? RoadRageBootstrap.Instance.TimeOfDay : DayTime.Midday;
            return t == DayTime.Dusk || t == DayTime.Morning ? Random.Range(1100f, 2400f) : Random.Range(3000f, 6000f);
        }

        private void Spawn()
        {
            var prefab = Resources.Load<GameObject>("Biomes/BlackForest/Meshes/SM_deer");
            if (prefab == null)
            {
                nextCrossing = car.RoadDistance + 5000f;
                return;
            }
            var material = RoadRageBootstrap.Instance != null ? RoadRageBootstrap.Instance.MaterialNamed("Road Life") : null;
            side = Random.value < 0.5f ? -1 : 1;
            var d = car.RoadDistance + Random.Range(125f, 160f);
            var count = 1 + (Random.value < 0.4f ? 1 : 0) + (Random.value < 0.15f ? 1 : 0);
            var edge = RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + 3f;
            for (var i = 0; i < count; i++)
            {
                var body = Instantiate(prefab).transform;
                body.name = "Deer";
                if (material != null)
                    foreach (var r in body.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
                foreach (var c in body.GetComponentsInChildren<Collider>()) Destroy(c);
                herd.Add(new Deer
                {
                    Body = body,
                    Distance = d + i * Random.Range(3f, 6f),
                    Lateral = side * (edge + i * 1.6f),
                    Delay = i * Random.Range(0.25f, 0.6f),
                });
            }
        }

        private void Strike(Deer deer)
        {
            deer.Hit = true;
            var forward = RoadPath.Rotation(deer.Distance) * Vector3.forward;
            deer.Fling = forward * Mathf.Max(4f, car.SpeedKph / 3.6f * 0.55f) + Vector3.up * 5.5f
                         + RoadPath.Right(deer.Distance) * -side * 2f;
            deer.Spin = new Vector3(Random.Range(-420f, 420f), Random.Range(-200f, 200f), Random.Range(-420f, 420f));
            GameState.ApplyDamage(8f);
            GameState.Show("🦌 DEER STRIKE!");
            car.SpeedKph *= 0.8f;
            if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.7f);
            if (RoadRageImpactShakeDirector.Instance != null) RoadRageImpactShakeDirector.Instance.TriggerMediumShake(0.6f);
        }

        private void Clear()
        {
            foreach (var deer in herd)
                if (deer.Body != null) Destroy(deer.Body.gameObject);
            herd.Clear();
        }

        private void OnDisable() => Clear();
    }
}
