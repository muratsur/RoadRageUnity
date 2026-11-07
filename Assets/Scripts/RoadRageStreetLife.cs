using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRage.UnityRemake
{
    /// People on the Manhattan pavement beside FDR Drive.
    ///
    /// The corridor is fully dressed - flagstone sidewalk, trees in cast-iron grates,
    /// benches, bus shelters, cafe tables, parked cars - and had nobody standing on any
    /// of it, which is the difference between a film set and a street. This walks a fixed
    /// pool of people along the pavement around the player and recycles each one ahead
    /// once they are behind, so a crowd costs the same however long the run is.
    ///
    /// There is no navmesh and no physics: the pavement is a ribbon at a known lateral
    /// offset from the road centreline, so a pedestrian is a road distance plus an
    /// offset, exactly as a traffic car is. That also means none of them can be hit -
    /// the car's lateral range stops at the kerb.
    ///
    /// The bodies are built at load from the primitive cube: four renderers each (coat,
    /// head, two legs), shared meshes, three small colour palettes. No new art, so no new
    /// licence and nothing new under Resources.
    public sealed class StreetLife : MonoBehaviour
    {
        /// Set by the bootstrap: true only while FDR Drive is the loaded biome.
        public bool On;

        /// Desktop only. The phone tier is failing Gate A already; thirty animated walkers
        /// are not what an unmeasured mobile build needs on top.
        private const int PoolSize = 30;
        private const float SpawnAhead = 175f;
        /// Near edge of the seed band. People well up the pavement are three pixels tall from
        /// the chase camera; the street only reads as inhabited if it starts just ahead of the
        /// bonnet, where somebody is sixty pixels rather than six.
        private const float SpawnNear = 9f;
        /// Recycled once this far behind the player: out of view, but not so early that a
        /// walker vanishing is visible.
        private const float SpawnBehind = 18f;
        /// Matches BuildFdrDrive's kerbEdge (HalfWidth + 1.2), then in from the street
        /// furniture line so nobody walks through a lamp post or a tree.
        private const float KerbOffset = 1.2f;
        private const float WalkBandNear = 2.1f;
        private const float WalkBandFar = 4.4f;
        /// The "Right Paved Sidewalk" ribbon's height above the road centreline.
        private const float SidewalkHeight = 0.14f;
        private const float FootLift = 0.015f;
        private const float HipHeight = 0.86f;
        /// One walker in five stands still - at a shelter, a bench, a shop window.
        private const int IdleEvery = 5;

        private sealed class Walker
        {
            public Transform Root;
            public Transform LegLeft;
            public Transform LegRight;
            public float Distance;
            public float Lateral;
            public float Speed;
            public float Phase;
            public float Yaw;
            public float Height;
            public int Direction;
            public bool Idle;
        }

        private static readonly Color[] CoatColours =
        {
            new Color(0.10f, 0.11f, 0.13f),   // black
            new Color(0.13f, 0.16f, 0.24f),   // navy
            new Color(0.26f, 0.24f, 0.21f),   // camel
            new Color(0.20f, 0.24f, 0.20f),   // olive
            new Color(0.34f, 0.13f, 0.14f),   // burgundy
            new Color(0.42f, 0.43f, 0.45f),   // grey
            new Color(0.52f, 0.30f, 0.12f),   // rust
        };

        private static readonly Color[] TrouserColours =
        {
            new Color(0.10f, 0.11f, 0.14f),   // dark denim
            new Color(0.06f, 0.06f, 0.07f),   // black
            new Color(0.22f, 0.21f, 0.20f),   // charcoal
            new Color(0.30f, 0.27f, 0.20f),   // khaki
        };

        private static readonly Color[] SkinColours =
        {
            new Color(0.72f, 0.55f, 0.42f),
            new Color(0.55f, 0.39f, 0.28f),
            new Color(0.38f, 0.26f, 0.19f),
            new Color(0.83f, 0.66f, 0.53f),
        };

        private readonly List<Walker> walkers = new();
        private Transform root;
        private Material[] coats;
        private Material[] trousers;
        private Material[] skins;
        private Mesh bodyMesh;
        private Mesh headMesh;
        private Mesh legMesh;
        private bool built;
        private bool visible;
        private float lastPlayer = float.NaN;

        private void Update()
        {
            if (!On)
            {
                if (visible && root != null) root.gameObject.SetActive(false);
                visible = false;
                return;
            }
            if (Application.isMobilePlatform) return;

            Build();
            if (!built) return;
            if (!visible)
            {
                root.gameObject.SetActive(true);
                visible = true;
            }

            var player = TrafficCarController.PlayerDistance;
            var step = Time.deltaTime;
            // A biome switch or a -startkm jump moves the road distance by kilometres;
            // everyone is far behind then, so they are re-seeded rather than walked back.
            if (float.IsNaN(lastPlayer) || Mathf.Abs(player - lastPlayer) > 300f)
                foreach (var walker in walkers) Place(walker, player);
            lastPlayer = player;

            for (var i = 0; i < walkers.Count; i++)
            {
                var walker = walkers[i];
                if (!walker.Idle) walker.Distance += walker.Direction * walker.Speed * step;
                if (walker.Distance < player - SpawnBehind || walker.Distance > player + SpawnAhead + 40f)
                {
                    Place(walker, player);
                    if (!walker.Idle) walker.Distance += walker.Direction * walker.Speed * step;
                }
                Pose(walker, step);
            }
        }

        /// Puts one walker ahead of the player, on the pavement, going either way.
        private void Place(Walker walker, float player)
        {
            // Squared random keeps the far pavement populated while weighting the pool toward
            // the stretch the player is about to drive past.
            var t = Random.value * Random.value;
            walker.Distance = player + Mathf.Lerp(SpawnNear, SpawnAhead, t);
            walker.Lateral = Random.Range(WalkBandNear, WalkBandFar);
            walker.Direction = Random.value < 0.5f ? 1 : -1;
            walker.Speed = Random.Range(1.05f, 1.65f);
            walker.Phase = Random.Range(0f, Mathf.PI * 2f);
            walker.Yaw = Random.Range(-14f, 14f);
            walker.Height = Random.Range(0.94f, 1.06f);
            walker.Idle = Random.Range(0, IdleEvery) == 0;
            walker.Root.localScale = new Vector3(walker.Height, walker.Height, walker.Height);
        }

        private static void Pose(Walker walker, float step)
        {
            var d = walker.Distance;
            var lateral = RoadPath.HalfWidthAt(d) + KerbOffset + walker.Lateral;
            var position = RoadPath.Point(d, lateral, SidewalkHeight + FootLift);

            // Facing: a walker going the other way looks back down the road. Idle ones keep
            // the direction they were given and turn a little to the shopfront.
            var facing = RoadPath.Forward(d) * walker.Direction;
            walker.Root.position = position;
            walker.Root.rotation = Quaternion.LookRotation(facing, Vector3.up)
                * Quaternion.Euler(0f, walker.Yaw, 0f);

            if (walker.Idle)
            {
                walker.LegLeft.localRotation = Quaternion.Euler(0f, 0f, 3f);
                walker.LegRight.localRotation = Quaternion.Euler(0f, 0f, -3f);
                return;
            }

            // Two steps per stride; the body rises on each one, which is what stops a
            // walker reading as a sliding statue.
            walker.Phase += walker.Speed * step * 3.6f;
            var swing = Mathf.Sin(walker.Phase) * 26f;
            walker.LegLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
            walker.LegRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            walker.Root.position += Vector3.up * Mathf.Abs(Mathf.Cos(walker.Phase)) * 0.022f;
        }

        private void Build()
        {
            if (built) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (lit == null) return;
            coats = Materials(lit, "Pedestrian Coat", CoatColours, 0f, 0.12f);
            trousers = Materials(lit, "Pedestrian Trousers", TrouserColours, 0f, 0.1f);
            skins = Materials(lit, "Pedestrian Skin", SkinColours, 0f, 0.25f);

            bodyMesh = Combined("Pedestrian Body",
                (new Vector3(0f, 1.15f, 0f), new Vector3(0.42f, 0.58f, 0.25f)),    // torso
                (new Vector3(-0.26f, 1.14f, 0f), new Vector3(0.11f, 0.52f, 0.14f)), // arm
                (new Vector3(0.26f, 1.14f, 0f), new Vector3(0.11f, 0.52f, 0.14f)),  // arm
                (new Vector3(0f, 0.88f, 0f), new Vector3(0.38f, 0.16f, 0.23f)));    // hips
            headMesh = Combined("Pedestrian Head",
                (new Vector3(0f, 1.565f, 0.005f), new Vector3(0.20f, 0.24f, 0.21f)),
                (new Vector3(0f, 1.665f, -0.01f), new Vector3(0.205f, 0.07f, 0.20f)));
            // The leg hangs from its own origin, so rotating the transform swings it from the hip.
            legMesh = Combined("Pedestrian Leg",
                (new Vector3(0f, -0.43f, 0f), new Vector3(0.155f, 0.86f, 0.17f)));

            root = new GameObject("Manhattan Street Life").transform;
            root.SetParent(transform, false);
            for (var i = 0; i < PoolSize; i++) walkers.Add(NewWalker(i));
            built = true;
        }

        private Walker NewWalker(int index)
        {
            var walker = new Walker();
            walker.Root = new GameObject($"Pedestrian {index}").transform;
            walker.Root.SetParent(root, false);

            Part(walker.Root, bodyMesh, coats[index % coats.Length], Vector3.zero,
                "Coat", ShadowCastingMode.On);
            Part(walker.Root, headMesh, skins[(index * 3) % skins.Length], Vector3.zero,
                "Head", ShadowCastingMode.On);
            walker.LegLeft = Part(walker.Root, legMesh, trousers[(index * 5) % trousers.Length],
                new Vector3(-0.10f, HipHeight, 0f), "Leg Left", ShadowCastingMode.Off).transform;
            walker.LegRight = Part(walker.Root, legMesh, trousers[(index * 5 + 2) % trousers.Length],
                new Vector3(0.10f, HipHeight, 0f), "Leg Right", ShadowCastingMode.Off).transform;
            return walker;
        }

        private static Renderer Part(Transform parent, Mesh mesh, Material material,
            Vector3 localPosition, string name, ShadowCastingMode shadows)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static Material[] Materials(Shader shader, string name, Color[] colours,
            float metallic, float smoothness)
        {
            var made = new Material[colours.Length];
            for (var i = 0; i < colours.Length; i++)
            {
                var material = new Material(shader) { name = $"{name} {i}" };
                material.color = colours[i];
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colours[i]);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
                made[i] = material;
            }
            return made;
        }

        /// Boxes merged into one mesh. Building the geometry by hand would mean getting the
        /// winding right on six faces per box; the primitive's own cube mesh has it already.
        private static Mesh Combined(string name, params (Vector3 Centre, Vector3 Size)[] parts)
        {
            var cube = CubeMesh();
            var combines = new CombineInstance[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                combines[i] = new CombineInstance
                {
                    mesh = cube,
                    transform = Matrix4x4.TRS(parts[i].Centre, Quaternion.identity, parts[i].Size),
                };
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(combines, true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh cube;

        private static Mesh CubeMesh()
        {
            if (cube != null) return cube;
            var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temporary.GetComponent<MeshFilter>().sharedMesh;
            // The mesh is a built-in asset and outlives the object that carried it.
            DestroyImmediate(temporary);
            return cube;
        }
    }
}
