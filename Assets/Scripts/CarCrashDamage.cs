using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// What a crash leaves on a car: the body crushed in where it was hit, panels broken off
    /// onto the road, and smoke, with fire after the hardest hits. Lasts until Repair.
    public sealed class CarCrashDamage : MonoBehaviour
    {
        private const int MaxPieces = 8;
        private const float PieceReach = 0.45f;
        /// Above this a wrecked engine burns.
        private const float FireSeverity = 0.75f;

        private readonly List<(MeshFilter Filter, Mesh Original)> crushed = new();
        private readonly List<GameObject> smoke = new();

        /// `point` is where it was hit, in the world; `severity` runs from 0 to 1.
        public void Apply(Vector3 point, float severity)
        {
            var depth = Mathf.Lerp(0.25f, 0.95f, severity);
            var reach = Mathf.Lerp(1.3f, 2.2f, severity);
            var inward = transform.position + Vector3.up * 0.6f - point;
            inward.y = 0f;
            inward = inward.sqrMagnitude > 0.01f ? inward.normalized : -transform.forward;
            var pieces = Mathf.RoundToInt(Mathf.Lerp(3f, MaxPieces, severity));
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                // The hero truck's wheels spin as whole parts.
                if (filter.name.StartsWith("Wheel_") || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (pieces > 0) BreakOffPieces(filter, renderer, point, reach, pieces);
                pieces = 0;
                Crush(filter, point, inward, reach, depth);
            }
            AddSmoke(point + inward * 0.6f + Vector3.up * 0.6f, severity);
        }

        /// A new run or a revived wreck starts whole.
        public void Repair()
        {
            foreach (var (filter, original) in crushed)
            {
                if (filter == null) continue;
                Destroy(filter.sharedMesh);
                filter.sharedMesh = original;
            }
            crushed.Clear();
            foreach (var plume in smoke)
                if (plume != null) Destroy(plume);
            smoke.Clear();
        }

        private void Crush(MeshFilter filter, Vector3 point, Vector3 inward, float reach, float depth)
        {
            var mesh = filter.sharedMesh;
            var mine = crushed.FindIndex(c => c.Filter == filter) >= 0;
            if (!mine)
            {
                crushed.Add((filter, mesh));
                mesh = Instantiate(mesh);
                filter.sharedMesh = mesh;
            }
            var toWorld = filter.transform.localToWorldMatrix;
            var toLocal = filter.transform.worldToLocalMatrix;
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                var world = toWorld.MultiplyPoint3x4(vertices[i]);
                var flat = world - point;
                flat.y *= 0.5f;
                var near = 1f - flat.magnitude / reach;
                if (near <= 0f) continue;
                var weight = near * near * (3f - 2f * near);
                // Crumples rather than dents: rippled, and a bonnet buckles upward.
                var ripple = 0.65f + 0.35f * Mathf.Sin(world.x * 7f + world.z * 9f) * Mathf.Sin(world.y * 8f);
                var height = Mathf.InverseLerp(transform.position.y + 0.5f, transform.position.y + 1.1f, world.y);
                world += inward * (depth * weight * ripple);
                world += Vector3.up * (depth * 0.3f * weight * ripple * (height - 0.4f));
                vertices[i] = toLocal.MultiplyPoint3x4(world);
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// Real pieces of the car: copies of the body's own triangles around the hit, so a
        /// fragment carries the paint of the car it came from.
        private void BreakOffPieces(MeshFilter filter, MeshRenderer renderer, Vector3 point, float reach, int count)
        {
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            var triangles = mesh.triangles;
            var toWorld = filter.transform.localToWorldMatrix;
            var nearby = new List<int>();
            for (var t = 0; t < triangles.Length; t += 3)
                if (Vector3.Distance(toWorld.MultiplyPoint3x4(vertices[triangles[t]]), point) < reach)
                    nearby.Add(t);
            if (nearby.Count == 0) return;
            for (var k = 0; k < count; k++)
            {
                var seed = toWorld.MultiplyPoint3x4(vertices[triangles[nearby[Random.Range(0, nearby.Count)]]]);
                var pieceVertices = new List<Vector3>();
                var pieceUv = new List<Vector2>();
                var pieceTriangles = new List<int>();
                var size = PieceReach * Random.Range(0.5f, 1.2f);
                foreach (var t in nearby)
                {
                    var a = toWorld.MultiplyPoint3x4(vertices[triangles[t]]);
                    if (Vector3.Distance(a, seed) > size) continue;
                    var v = pieceVertices.Count;
                    for (var c = 0; c < 3; c++)
                    {
                        pieceVertices.Add(toWorld.MultiplyPoint3x4(vertices[triangles[t + c]]) - seed);
                        pieceUv.Add(uv.Length > 0 ? uv[triangles[t + c]] : Vector2.zero);
                    }
                    // Both faces, with their own vertices: a shell seen from behind is invisible.
                    for (var c = 2; c >= 0; c--)
                    {
                        pieceVertices.Add(pieceVertices[v + c]);
                        pieceUv.Add(pieceUv[v + c]);
                    }
                    for (var c = 0; c < 6; c++) pieceTriangles.Add(v + c);
                }
                if (pieceTriangles.Count < 18) continue;
                var part = new GameObject("Crash Debris");
                var pieceMesh = new Mesh { name = "Crash Debris" };
                pieceMesh.SetVertices(pieceVertices);
                pieceMesh.SetUVs(0, pieceUv);
                pieceMesh.SetTriangles(pieceTriangles, 0);
                pieceMesh.RecalculateNormals();
                part.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
                part.transform.position = seed;
                part.AddComponent<CrashDebris>().Launch(pieceMesh);
            }
        }

        private void AddSmoke(Vector3 at, float severity)
        {
            var soft = Resources.Load<Material>("WeatherParticle");
            if (soft == null) return;
            smoke.Add(Plume("Crash Smoke", at, soft, new Color(0.16f, 0.16f, 0.17f, 0.55f), 14f, 2.6f, 1.1f, 3.4f));
            if (severity >= FireSeverity)
                smoke.Add(Plume("Crash Fire", at, soft, new Color(1f, 0.45f, 0.08f, 0.9f), 26f, 0.7f, 0.7f, 2.2f));
        }

        private GameObject Plume(string name, Vector3 at, Material material, Color colour, float rate, float life, float size, float rise)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            holder.transform.position = at;
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = rise;
            main.startSize = size;
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            system.Play();
            return holder;
        }
    }

    /// A broken piece of a car: thrown from the hit, tumbling, then lying on the road until
    /// the player has left it behind.
    public sealed class CrashDebris : MonoBehaviour
    {
        private const float Gravity = 14f;
        private const float ClearBehindPlayer = 60f;
        private const float RoadLift = 0.2f;
        private Vector3 velocity;
        private Vector3 spin;
        private Mesh mesh;

        public void Launch(Mesh own)
        {
            mesh = own;
            var flat = Random.insideUnitCircle * Random.Range(3f, 8f);
            velocity = new Vector3(flat.x, Random.Range(2.5f, 6f), flat.y);
            spin = Random.onUnitSphere * Random.Range(200f, 700f);
        }

        private void Update()
        {
            if (TrafficCarController.PlayerDistance - transform.position.z > ClearBehindPlayer)
            {
                Destroy(gameObject);
                return;
            }
            var ground = RoadPath.CenterY(transform.position.z) + RoadLift;
            if (velocity == Vector3.zero) return;
            velocity.y -= Gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(spin * Time.deltaTime, Space.World);
            if (transform.position.y > ground) return;
            transform.position = new Vector3(transform.position.x, ground, transform.position.z);
            velocity *= 0.45f;
            velocity.y = -velocity.y * 0.3f + Mathf.Abs(velocity.y) * 0.6f;
            spin *= 0.4f;
            if (velocity.sqrMagnitude < 0.5f) velocity = Vector3.zero;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
