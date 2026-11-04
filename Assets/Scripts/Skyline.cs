using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRage.UnityRemake
{
    /// The real mountains beyond the drawn ground (it ends 1.2 km out), from
    /// Tools/Terrain/bake_skyline.py: per point of road, the highest real angle in each
    /// distance band all round. Each band is one silhouette strip a little past the
    /// ground, nearest band in front, so every ridge stands at the angle it really does
    /// from that point of the road. Lives under the horizon follower, which keeps it
    /// centred on the camera.
    public sealed class Skyline : MonoBehaviour
    {
        private const float Eye = 2f;
        private const float BottomAngle = -8f;
        /// Strip distance per band, nearest first: past the ground, inside the 1400 m far clip.
        private static readonly float[] Radii = { 1210f, 1260f, 1310f };

        private short[] angles;
        private byte[] distances;
        private byte[] heights;
        private int rows, azimuths, bands;
        private float step;
        private RoadRoute route;
        private float clearFog;
        private Color land;
        private float treeline;
        private bool stepped;
        /// Columns of vertices round the ring: one per azimuth and the first again to close it, or
        /// in steps two per azimuth, at either edge of its block.
        private int Columns => stepped ? azimuths * 2 : azimuths + 1;
        private Mesh mesh;
        private Vector3[] vertices;
        private Color[] colours;
        private Material material;
        private static readonly int HazeBoost = Shader.PropertyToID("_HazeBoost");

        /// Null when the file or material is missing: the biome then simply has no far mountains.
        /// stepped: each azimuth a flat-topped block, for a city's towers; smooth slopes between
        /// azimuths read as mountains.
        public static Skyline Build(Transform parent, string resourcePath, RoadRoute route, float clearFog, Color land, float treeline,
            bool stepped = false)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            var source = Resources.Load<Material>("Biomes/Mountains/M_skyline");
            if (asset == null || source == null || route == null) return null;
            var bytes = asset.bytes;
            if (bytes.Length < 20 || bytes[0] != 'R' || bytes[1] != 'R' || bytes[2] != 'S' || bytes[3] != 'K') return null;
            var rows = System.BitConverter.ToInt32(bytes, 4);
            var step = System.BitConverter.ToSingle(bytes, 8);
            var azimuths = System.BitConverter.ToInt32(bytes, 12);
            var bands = System.BitConverter.ToInt32(bytes, 16);
            var header = 20 + (bands + 1) * 4;
            var count = rows * bands * azimuths;
            if (rows < 2 || step <= 0f || azimuths < 8 || bands != Radii.Length || bytes.Length < header + count * 4) return null;

            var go = new GameObject("Real Skyline");
            go.transform.SetParent(parent, false);
            var sky = go.AddComponent<Skyline>();
            sky.rows = rows;
            sky.step = step;
            sky.azimuths = azimuths;
            sky.bands = bands;
            sky.route = route;
            sky.clearFog = clearFog;
            sky.land = land;
            sky.treeline = treeline;
            sky.stepped = stepped;
            sky.angles = new short[count];
            sky.distances = new byte[count];
            sky.heights = new byte[count];
            for (var i = 0; i < count; i++)
            {
                var at = header + i * 4;
                sky.angles[i] = System.BitConverter.ToInt16(bytes, at);
                sky.distances[i] = bytes[at + 2];
                sky.heights[i] = bytes[at + 3];
            }

            var columns = sky.Columns;
            sky.vertices = new Vector3[bands * columns * 2];
            sky.colours = new Color[sky.vertices.Length];
            var triangles = new int[bands * (columns - 1) * 6];
            var t = 0;
            for (var b = 0; b < bands; b++)
            for (var q = 0; q < columns - 1; q++)
            {
                var v = (b * columns + q) * 2;
                triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 1;
                triangles[t++] = v + 1; triangles[t++] = v + 2; triangles[t++] = v + 3;
            }
            sky.mesh = new Mesh { name = "Real Skyline" };
            sky.mesh.MarkDynamic();
            sky.mesh.vertices = sky.vertices;
            sky.mesh.colors = sky.colours;
            sky.mesh.triangles = triangles;
            // Rebuilt round the camera every frame; never cull it.
            sky.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
            go.AddComponent<MeshFilter>().sharedMesh = sky.mesh;
            sky.material = new Material(source) { name = "Real Skyline" };
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sky.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return sky;
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var d = camera.transform.position.z;
            // There and back: the return pass mirrors the outward one along the road (the
            // same side stays the same side), so ahead and behind swap.
            var back = Mathf.Repeat(d, 2f * route.Length) > route.Length;
            var f = Mathf.Clamp(route.Fold(d) / step, 0f, rows - 1.001f);
            var r = (int)f;
            var tr = f - r;
            var forward = RoadPath.Forward(d);
            var heading = Mathf.Atan2(forward.x, forward.z);
            var baseY = RoadPath.CenterY(d) + Eye;
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var columns = Columns;
            var bottomSin = Mathf.Sin(BottomAngle * Mathf.Deg2Rad);
            var bottomCos = Mathf.Cos(BottomAngle * Mathf.Deg2Rad);

            for (var b = 0; b < bands; b++)
            for (var c = 0; c < columns; c++)
            {
                var q = stepped ? c / 2 : c % azimuths;
                var baked = back ? ((azimuths / 2 - q) % azimuths + azimuths) % azimuths : q;
                var i0 = (r * bands + b) * azimuths + baked;
                var i1 = i0 + bands * azimuths;
                var angle = Mathf.Lerp(angles[i0], angles[i1], tr) * 0.01f * Mathf.Deg2Rad;
                var distance = Mathf.Lerp(distances[i0], distances[i1], tr) * 100f;
                var height = Mathf.Lerp(heights[i0], heights[i1], tr) * 16f;

                var yaw = heading + (q + (stepped ? (c % 2 == 0 ? -0.5f : 0.5f) : 0f)) * (2f * Mathf.PI / azimuths);
                var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var radius = Radii[b];
                var v = (b * columns + c) * 2;
                vertices[v] = dir * (radius * Mathf.Cos(angle)) + Vector3.up * (baseY + radius * Mathf.Sin(angle));
                vertices[v + 1] = dir * (radius * bottomCos) + Vector3.up * (baseY + radius * bottomSin);

                var ground = GroundColour(height);
                if (linear) ground = ground.linear;
                // The air between hides the ground more with distance.
                ground.a = 1f - Mathf.Exp(-distance / 14000f);
                colours[v] = ground;
                colours[v + 1] = new Color(ground.r * 0.7f, ground.g * 0.7f, ground.b * 0.7f, ground.a);
            }
            mesh.vertices = vertices;
            mesh.colors = colours;
            material.SetFloat(HazeBoost, Mathf.Clamp(RenderSettings.fogDensity / clearFog, 1f, 6f));
        }

        private void OnDestroy()
        {
            Destroy(mesh);
            Destroy(material);
        }

        /// Sea, the land's own colour, bare rock from treeline (rock to treeline + 400 m),
        /// snow from treeline + 650 m. A treeline above the highest peak leaves it all land.
        private Color GroundColour(float height)
        {
            var water = new Color(0.16f, 0.27f, 0.36f);
            var rock = new Color(0.36f, 0.37f, 0.38f);
            var snow = new Color(0.86f, 0.89f, 0.93f);
            if (height < 3f) return water;
            if (height < treeline + 600f) return Color.Lerp(land, rock, Mathf.InverseLerp(treeline, treeline + 400f, height));
            return Color.Lerp(rock, snow, Mathf.InverseLerp(treeline + 650f, treeline + 1000f, height));
        }
    }
}
