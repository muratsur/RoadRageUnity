using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRage.UnityRemake
{
    /// A long-nose logging rig built from primitives: tractor, stake trailer and a
    /// strapped stack of logs. Nothing here comes from a pack, so it is committed and
    /// always available. Local frame matches the other traffic bodies: +Z forward,
    /// centred on the origin along the road, wheels on y = 0.
    public static class LogTruckBuilder
    {
        public const float Length = 17.4f;
        private const float Front = Length * 0.5f;
        private const float Rear = -Length * 0.5f;

        private const float LogLength = 10.4f;
        private const float LogCentreZ = -3.3f;
        /// The fifth wheel: where the trailer turns on the tractor.
        public const float HitchZ = 1.35f;
        private const float DeckTop = 1.42f;

        private static readonly Dictionary<string, Material> Cache = new();
        private static Mesh logMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            logMesh = null;
        }

        /// Builds the rig under `root`, and returns the cargo component that spills
        /// the logs when the truck is wrecked.
        public static LogTruckCargo Build(Transform root, Color paint, Material glass)
        {
            var body = new GameObject("Log Truck Visual").transform;
            body.SetParent(root, false);

            var cab = Mat($"Log Truck Paint {ColorUtility.ToHtmlStringRGB(paint)}", paint, 0.35f, 0.62f);
            var chrome = Mat("Log Truck Chrome", new Color(0.78f, 0.80f, 0.82f), 0.95f, 0.85f);
            var frame = Mat("Log Truck Frame", new Color(0.13f, 0.13f, 0.14f), 0.4f, 0.35f);
            var tyre = Mat("Log Truck Tyre", new Color(0.06f, 0.06f, 0.065f), 0f, 0.2f);
            var amber = Mat("Log Truck Amber", new Color(1f, 0.62f, 0.08f), 0f, 0.6f, emissive: 1.4f);
            var tail = Mat("Log Truck Tail", new Color(0.85f, 0.05f, 0.03f), 0f, 0.6f, emissive: 1.1f);
            var head = Mat("Log Truck Headlight", new Color(1f, 0.96f, 0.85f), 0f, 0.9f, emissive: 1.6f);
            if (glass == null) glass = Mat("Log Truck Glass", new Color(0.10f, 0.14f, 0.17f), 0.2f, 0.95f);

            var tractor = new GameObject("Tractor").transform;
            tractor.SetParent(body, false);
            BuildTractor(tractor, cab, chrome, frame, tyre, amber, head, glass);

            // The trailer and its load hang off the fifth wheel, so they can fold
            // round it. Parts keep their rig coordinates under an offset child.
            var trailer = AddHitch(root, body, HitchZ, HitchZ - Rear);
            BuildTrailer(trailer, frame, chrome, tyre, tail);
            return AddLoad(root, trailer, DeckTop, LogCentreZ, LogLength);
        }

        /// The trailer's pivot at `hitchZ` on the rig, under `parent`, and the hitch
        /// that folds it when the rig is wrecked. Returns the frame everything on the
        /// trailer goes under: it turns with the pivot, but its coordinates are the
        /// rig's own, so parts are placed as if the trailer were not jointed.
        public static Transform AddHitch(Transform root, Transform parent, float hitchZ, float trailerLength)
        {
            var pivot = new GameObject("Trailer Hitch").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0f, 0f, hitchZ);
            root.gameObject.AddComponent<TrailerHitch>().Bind(pivot, hitchZ, trailerLength);
            var trailer = new GameObject("Trailer").transform;
            trailer.SetParent(pivot, false);
            trailer.localPosition = new Vector3(0f, 0f, -hitchZ);
            return trailer;
        }

        /// Puts a strapped load of logs on a truck body built elsewhere - a Rodin model -
        /// under `parent` (the root, or a trailer that folds), in the rig's frame:
        /// `deckTop` is the bed's height, `centreZ` the middle of the load along the
        /// rig, `logLength` how long the logs are.
        public static LogTruckCargo AddLoad(Transform root, Transform parent, float deckTop, float centreZ, float logLength)
        {
            var strap = Mat("Log Truck Strap", new Color(0.92f, 0.30f, 0.08f), 0f, 0.3f);
            var cargoRoot = new GameObject("Log Load").transform;
            cargoRoot.SetParent(parent, false);
            var cargo = root.gameObject.AddComponent<LogTruckCargo>();
            BuildLoad(cargoRoot, strap, cargo, deckTop, centreZ, logLength);
            cargo.Bind(cargoRoot);
            return cargo;
        }

        private static void BuildTractor(Transform p, Material cab, Material chrome, Material frame, Material tyre,
            Material amber, Material head, Material glass)
        {
            // Chassis rails run under the whole tractor.
            Box(p, "Tractor Frame", new Vector3(0f, 0.78f, 4.6f), new Vector3(1.05f, 0.28f, 8.1f), frame);

            // Long conventional nose.
            Box(p, "Bumper", new Vector3(0f, 0.62f, Front - 0.14f), new Vector3(2.55f, 0.36f, 0.28f), chrome);
            Box(p, "Hood", new Vector3(0f, 1.62f, 7.15f), new Vector3(1.55f, 1.12f, 2.9f), cab);
            Box(p, "Hood Crown", new Vector3(0f, 2.22f, 7.0f), new Vector3(1.35f, 0.12f, 2.6f), cab);
            Box(p, "Grille", new Vector3(0f, 1.58f, Front - 0.29f), new Vector3(1.32f, 1.08f, 0.1f), chrome);
            for (var i = 0; i < 5; i++)
                Box(p, "Grille Bar", new Vector3(0f, 1.18f + i * 0.2f, Front - 0.24f), new Vector3(1.2f, 0.05f, 0.04f), frame);
            foreach (var side in new[] { -1f, 1f })
            {
                Box(p, "Fender", new Vector3(side * 1.02f, 1.28f, 7.25f), new Vector3(0.5f, 0.42f, 1.9f), cab);
                Box(p, "Headlight", new Vector3(side * 1.0f, 1.2f, Front - 0.38f), new Vector3(0.36f, 0.2f, 0.1f), head);
                Box(p, "Hood Vent", new Vector3(side * 0.79f, 1.7f, 6.9f), new Vector3(0.04f, 0.42f, 0.9f), chrome);
            }

            // Cab and sleeper.
            Box(p, "Cab", new Vector3(0f, 2.42f, 4.95f), new Vector3(2.4f, 2.1f, 1.9f), cab);
            Box(p, "Sleeper", new Vector3(0f, 2.55f, 3.2f), new Vector3(2.4f, 2.35f, 1.65f), cab);
            var screen = Box(p, "Windshield", new Vector3(0f, 2.95f, 5.92f), new Vector3(2.2f, 0.82f, 0.06f), glass);
            screen.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            foreach (var side in new[] { -1f, 1f })
            {
                Box(p, "Side Window", new Vector3(side * 1.215f, 2.95f, 5.05f), new Vector3(0.04f, 0.72f, 1.2f), glass);
                Box(p, "Mirror Arm", new Vector3(side * 1.42f, 2.72f, 5.8f), new Vector3(0.45f, 0.05f, 0.05f), chrome);
                Box(p, "Mirror", new Vector3(side * 1.62f, 2.72f, 5.8f), new Vector3(0.08f, 0.62f, 0.26f), chrome);
                // Twin stacks behind the cab - most of the silhouette at a distance.
                Cylinder(p, "Exhaust Stack", new Vector3(side * 1.32f, 3.1f, 3.95f), 0.13f, 3.3f, chrome);
                Cylinder(p, "Stack Heat Shield", new Vector3(side * 1.32f, 2.5f, 3.95f), 0.16f, 1.1f, frame);
                // Chrome saddle tanks and the step below the door.
                CylinderZ(p, "Fuel Tank", new Vector3(side * 1.2f, 0.95f, 4.55f), 0.38f, 1.45f, chrome);
                Box(p, "Cab Step", new Vector3(side * 1.18f, 0.52f, 5.55f), new Vector3(0.5f, 0.08f, 0.55f), chrome);
            }
            for (var i = -2; i <= 2; i++)
                Box(p, "Cab Marker", new Vector3(i * 0.3f, 3.51f, 5.72f), new Vector3(0.16f, 0.07f, 0.08f), amber);

            // Steer axle and a tandem drive.
            Wheels(p, 7.2f, 1.1f, 0.52f, 0.34f, tyre, chrome, frame);
            Wheels(p, 1.95f, 1.0f, 0.52f, 0.62f, tyre, chrome, frame);
            Wheels(p, 0.7f, 1.0f, 0.52f, 0.62f, tyre, chrome, frame);
            Box(p, "Fifth Wheel", new Vector3(0f, 1.14f, 1.35f), new Vector3(1.4f, 0.12f, 1.3f), frame);
        }

        private static void BuildTrailer(Transform p, Material frame, Material chrome, Material tyre, Material tail)
        {
            // Clear of the sleeper (its back is at 2.375), so the rack is seen.
            const float trailerFront = 2.2f;
            var trailerLength = trailerFront - Rear;
            var centre = (trailerFront + Rear) * 0.5f;
            Box(p, "Trailer Deck", new Vector3(0f, DeckTop - 0.14f, centre), new Vector3(2.45f, 0.26f, trailerLength), frame);
            Box(p, "Trailer Spine", new Vector3(0f, 1.0f, centre), new Vector3(0.7f, 0.36f, trailerLength), frame);

            // Headache rack: the ladder frame that stops a load coming through the cab.
            var rackZ = trailerFront - 0.1f;
            const float rackTop = 3.75f;
            foreach (var x in new[] { -1.15f, 1.15f })
                Box(p, "Rack Post", new Vector3(x, (DeckTop + rackTop) * 0.5f, rackZ), new Vector3(0.14f, rackTop - DeckTop, 0.14f), frame);
            for (var i = 0; i < 4; i++)
                Box(p, "Rack Rung", new Vector3(0f, DeckTop + 0.3f + i * 0.62f, rackZ), new Vector3(2.3f, 0.08f, 0.08f), frame);
            for (var i = -1; i <= 1; i++)
                Box(p, "Rack Upright", new Vector3(i * 0.55f, (DeckTop + rackTop) * 0.5f, rackZ), new Vector3(0.06f, rackTop - DeckTop, 0.06f), frame);

            // Bunk stakes along both sides.
            foreach (var z in new[] { 0.9f, -1.9f, -4.7f, -7.4f })
            {
                Box(p, "Bunk", new Vector3(0f, DeckTop + 0.05f, z), new Vector3(2.5f, 0.12f, 0.2f), frame);
                foreach (var x in new[] { -1.2f, 1.2f })
                    Box(p, "Stake", new Vector3(x, DeckTop + 1.05f, z), new Vector3(0.12f, 2.1f, 0.12f), frame);
            }

            Box(p, "Landing Gear", new Vector3(0f, 0.62f, 0.2f), new Vector3(1.8f, 0.9f, 0.14f), frame);
            foreach (var z in new[] { -4.65f, -5.75f, -6.85f, -7.95f })
                Wheels(p, z, 1.0f, 0.5f, 0.58f, tyre, chrome, frame);
            Box(p, "Rear Bumper", new Vector3(0f, 0.72f, Rear + 0.1f), new Vector3(2.3f, 0.16f, 0.14f), chrome);
            foreach (var x in new[] { -0.95f, 0.95f })
            {
                Box(p, "Tail Light", new Vector3(x, 1.12f, Rear + 0.03f), new Vector3(0.36f, 0.14f, 0.06f), tail);
                Box(p, "Mud Flap", new Vector3(x, 0.55f, -8.6f), new Vector3(0.62f, 0.72f, 0.04f), frame);
            }
        }

        /// Three courses of logs, four, four and three, with straps over the top.
        private static void BuildLoad(Transform cargoRoot, Material strap, LogTruckCargo cargo,
            float deckTop, float centreZ, float logLength)
        {
            var bark = BarkMaterial();
            var grain = EndGrainMaterial();
            var mesh = LogMesh();
            var courses = new[] { 4, 4, 3 };
            var y = deckTop + 0.12f;
            var seed = 11;
            for (var course = 0; course < courses.Length; course++)
            {
                var count = courses[course];
                var radius = 0.31f;
                for (var i = 0; i < count; i++)
                {
                    seed = seed * 1103515245 + 12345;
                    var jitter = ((seed >> 8) & 0xff) / 255f;
                    var r = Mathf.Lerp(0.27f, 0.33f, jitter);
                    var x = (i - (count - 1) * 0.5f) * 0.56f;
                    var length = logLength + Mathf.Lerp(-0.35f, 0.25f, 1f - jitter);
                    var log = new GameObject("Log");
                    log.transform.SetParent(cargoRoot, false);
                    log.transform.localPosition = new Vector3(x, y + radius, centreZ + (jitter - 0.5f) * 0.3f);
                    log.transform.localRotation = Quaternion.Euler(0f, 0f, jitter * 360f);
                    log.transform.localScale = new Vector3(r, r, length);
                    log.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = log.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = new[] { bark, grain };
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    cargo.AddLog(log.transform, r, length * 0.5f);
                }
                y += 0.56f;
            }

            var top = y + 0.08f;
            // Four straps spread along the load, wherever the load sits.
            foreach (var along in new[] { 0.31f, 0.07f, -0.17f, -0.37f })
            {
                var z = centreZ + along * logLength;
                Box(cargoRoot, "Strap", new Vector3(0f, top, z), new Vector3(2.4f, 0.03f, 0.09f), strap);
                foreach (var x in new[] { -1.22f, 1.22f })
                    Box(cargoRoot, "Strap", new Vector3(x, (deckTop + top) * 0.5f, z), new Vector3(0.03f, top - deckTop, 0.09f), strap);
            }
        }

        private static void Wheels(Transform p, float z, float halfTrack, float radius, float width,
            Material tyre, Material hub, Material frame)
        {
            Box(p, "Axle", new Vector3(0f, radius, z), new Vector3(halfTrack * 2f, 0.14f, 0.14f), frame);
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * (halfTrack + width * 0.5f - 0.1f);
                CylinderX(p, "Tyre", new Vector3(x, radius, z), radius, width, tyre);
                CylinderX(p, "Hub", new Vector3(x + side * (width * 0.5f + 0.005f), radius, z), radius * 0.55f, 0.03f, hub);
            }
        }

        // ---- primitives --------------------------------------------------------

        private static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
            Shape(PrimitiveType.Cube, parent, name, position, Quaternion.identity, size, material);

        private static Transform Cylinder(Transform parent, string name, Vector3 position, float radius, float height, Material material) =>
            Shape(PrimitiveType.Cylinder, parent, name, position, Quaternion.identity,
                new Vector3(radius * 2f, height * 0.5f, radius * 2f), material);

        private static Transform CylinderX(Transform parent, string name, Vector3 position, float radius, float width, Material material) =>
            Shape(PrimitiveType.Cylinder, parent, name, position, Quaternion.Euler(0f, 0f, 90f),
                new Vector3(radius * 2f, width * 0.5f, radius * 2f), material);

        private static Transform CylinderZ(Transform parent, string name, Vector3 position, float radius, float length, Material material) =>
            Shape(PrimitiveType.Cylinder, parent, name, position, Quaternion.Euler(90f, 0f, 0f),
                new Vector3(radius * 2f, length * 0.5f, radius * 2f), material);

        /// Collider removed at once: a stray collider on a traffic body reaches the
        /// directors' raycasts and the contact registry.
        private static Transform Shape(PrimitiveType type, Transform parent, string name, Vector3 position,
            Quaternion rotation, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.name = name;
            var t = item.transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = scale;
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return t;
        }

        // ---- materials ---------------------------------------------------------

        private static Material Mat(string name, Color color, float metallic, float smoothness, float emissive = 0f)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color, enableInstancing = true };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emissive > 0f && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emissive);
            }
            Cache[name] = material;
            return material;
        }

        private static Material Textured(string name, Texture2D texture, float smoothness)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = Mat(name, Color.white, 0f, smoothness);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            return material;
        }

        /// Grey-brown bark with long vertical fissures and patches of the orange inner
        /// bark showing through, generated so no texture has to ship.
        private static Material BarkMaterial()
        {
            if (Cache.TryGetValue("Log Bark", out var cached) && cached != null) return cached;
            const int w = 128, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Log Bark", wrapMode = TextureWrapMode.Repeat };
            var dark = new Color(0.22f, 0.19f, 0.16f);
            var grey = new Color(0.47f, 0.44f, 0.39f);
            var inner = new Color(0.50f, 0.33f, 0.20f);
            var pixels = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var u = x / (float)w;
                var v = y / (float)h;
                // Fissures: noise stretched along the log, so the cracks run lengthwise.
                var fissure = Mathf.PerlinNoise(u * 22f, v * 2.5f);
                var patch = Mathf.PerlinNoise(u * 5f + 13f, v * 7f + 3f);
                var fine = Mathf.PerlinNoise(u * 60f, v * 30f);
                var c = Color.Lerp(dark, grey, Mathf.SmoothStep(0.35f, 0.62f, fissure));
                // Inner bark only where a strip has come away: rare, and never fully bright.
                if (patch > 0.72f) c = Color.Lerp(c, inner, Mathf.Clamp01((patch - 0.72f) * 5f) * 0.6f);
                c *= 0.85f + fine * 0.3f;
                c.a = 1f;
                pixels[y * w + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return Textured("Log Bark", tex, 0.12f);
        }

        /// Pale sapwood with growth rings and a dark bark rim.
        private static Material EndGrainMaterial()
        {
            if (Cache.TryGetValue("Log End Grain", out var cached) && cached != null) return cached;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Log End Grain", wrapMode = TextureWrapMode.Clamp };
            var wood = new Color(0.86f, 0.70f, 0.48f);
            var ring = new Color(0.68f, 0.50f, 0.30f);
            var rim = new Color(0.30f, 0.24f, 0.19f);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x / (float)(size - 1) * 2f - 1f;
                var dy = y / (float)(size - 1) * 2f - 1f;
                var angle = Mathf.Atan2(dy, dx);
                var d = Mathf.Sqrt(dx * dx + dy * dy) * (1f + Mathf.Sin(angle * 3f) * 0.025f);
                var rings = Mathf.Pow(Mathf.Abs(Mathf.Sin(d * 42f)), 6f);
                var c = Color.Lerp(wood, ring, rings * 0.8f);
                c = Color.Lerp(c, ring, Mathf.Clamp01(1f - d * 6f) * 0.5f);
                if (d > 0.9f) c = rim;
                c.a = 1f;
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return Textured("Log End Grain", tex, 0.2f);
        }

        /// Unit log: radius 1 across X/Y, length 1 along Z, centred. Submesh 0 is the
        /// bark, submesh 1 the two end faces, so one renderer draws a whole log.
        private static Mesh LogMesh()
        {
            if (logMesh != null) return logMesh;
            const int segments = 14;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var side = new List<int>();
            var caps = new List<int>();

            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var u = i / (float)segments * 2f;
                vertices.Add(new Vector3(n.x, n.y, -0.5f)); normals.Add(n); uvs.Add(new Vector2(u, 0f));
                vertices.Add(new Vector3(n.x, n.y, 0.5f)); normals.Add(n); uvs.Add(new Vector2(u, 3f));
            }
            for (var i = 0; i < segments; i++)
            {
                var b = i * 2;
                side.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }

            foreach (var end in new[] { -0.5f, 0.5f })
            {
                var normal = new Vector3(0f, 0f, Mathf.Sign(end));
                var centre = vertices.Count;
                vertices.Add(new Vector3(0f, 0f, end)); normals.Add(normal); uvs.Add(new Vector2(0.5f, 0.5f));
                for (var i = 0; i <= segments; i++)
                {
                    var a = i / (float)segments * Mathf.PI * 2f;
                    var c = Mathf.Cos(a);
                    var s = Mathf.Sin(a);
                    vertices.Add(new Vector3(c, s, end)); normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f));
                }
                for (var i = 0; i < segments; i++)
                {
                    var v0 = centre + 1 + i;
                    if (end > 0f) caps.AddRange(new[] { centre, v0, v0 + 1 });
                    else caps.AddRange(new[] { centre, v0 + 1, v0 });
                }
            }

            logMesh = new Mesh { name = "Log" };
            logMesh.SetVertices(vertices);
            logMesh.SetNormals(normals);
            logMesh.SetUVs(0, uvs);
            logMesh.subMeshCount = 2;
            logMesh.SetTriangles(side, 0);
            logMesh.SetTriangles(caps, 1);
            logMesh.RecalculateBounds();
            logMesh.RecalculateTangents();
            return logMesh;
        }
    }

    /// The load on a log truck. When the truck is wrecked the logs come off: each one
    /// is handed to a FallenLog that throws it down the road in road space. The road
    /// has no colliders, so Unity physics would drop them straight through it.
    public sealed class LogTruckCargo : MonoBehaviour
    {
        private struct Slot
        {
            public Transform Log;
            public float Radius;
            public float HalfLength;
        }

        private readonly List<Slot> slots = new();
        private Transform load;
        public bool Spilled { get; private set; }

        public void AddLog(Transform log, float radius, float halfLength) =>
            slots.Add(new Slot { Log = log, Radius = radius, HalfLength = halfLength });

        public void Bind(Transform loadRoot) => load = loadRoot;

        /// Throws the load off. `lateralPush` is the side the truck was hit from, and
        /// the logs go the other way; `byPlayer` decides whether cars they take out pay.
        public void Spill(TrafficCarController truck, float lateralPush, float impactSpeedKph, bool byPlayer)
        {
            if (Spilled || load == null) return;
            Spilled = true;

            var carrier = truck.transform.parent;
            var truckSpeed = truck.SpeedKph / 3.6f * truck.Direction;
            var hit = Mathf.Clamp01((impactSpeedKph - 20f) / 140f);
            var away = Mathf.Abs(lateralPush) < 0.01f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(lateralPush);
            foreach (var slot in slots)
            {
                if (slot.Log == null) continue;
                var copy = Instantiate(slot.Log.gameObject, slot.Log.position, slot.Log.rotation, carrier);
                copy.transform.localScale = slot.Log.lossyScale;
                copy.name = "Spilled Log";
                var fallen = copy.AddComponent<FallenLog>();
                // Upper courses fly further; the whole load keeps most of the truck's
                // speed, plus a shove along the road from the hit.
                var height = slot.Log.position.y - truck.transform.position.y;
                var velocity = new Vector3(
                    away * Random.Range(1.5f, 4.5f) * (0.6f + hit),
                    Random.Range(1.5f, 3.5f) + height * 0.8f * hit,
                    truckSpeed * Random.Range(0.55f, 0.85f) + Random.Range(-2f, 3f) * hit * truck.Direction);
                fallen.Launch(truck, slot.Radius, slot.HalfLength, velocity, byPlayer);
            }
            load.gameObject.SetActive(false);

            if (byPlayer)
            {
                GameState.Show("🪵 LOG SPILL!");
                if (RoadRageImpactShakeDirector.Instance != null)
                    RoadRageImpactShakeDirector.Instance.TriggerLightShake(0.6f);
            }
            Debug.Log($"RR_EVENT logspill at {truck.RoadDistance:0}m logs={slots.Count} byPlayer={byPlayer}");
        }

        /// A wreck revived up the road drives again with a fresh load.
        public void Restock()
        {
            Spilled = false;
            if (load != null) load.gameObject.SetActive(true);
        }
    }

    /// A trailer folding on its fifth wheel when the rig is wrecked. The crash kicks it
    /// one way; while the wreck still rolls the fold keeps opening, as a jackknife does,
    /// until it meets the cab; stopped, it stays where it ended. The trailer's sideways
    /// reach is handed to the controller, so the contact pass sees the folded trailer.
    public sealed class TrailerHitch : MonoBehaviour
    {
        /// About where a trailer's front corner meets the back of the cab.
        private const float MaxFold = 70f;

        private Transform pivot;
        private TrafficCarController car;
        private float trailerLength;
        private float fold;
        private float foldRate;

        /// The fifth wheel's place along the rig, in the rig's frame.
        public float HitchZ { get; private set; }
        public float Fold => fold;

        public void Bind(Transform trailerPivot, float hitchZ, float length)
        {
            pivot = trailerPivot;
            HitchZ = hitchZ;
            trailerLength = Mathf.Max(1f, length);
        }

        /// Degrees a second; positive swings the trailer's tail to the rig's left.
        public void Kick(float degreesPerSecond) => foldRate += degreesPerSecond;

        /// A revived rig drives off straight.
        public void Straighten()
        {
            fold = 0f;
            foldRate = 0f;
            Apply();
        }

        private void Update()
        {
            if (fold == 0f && foldRate == 0f) return;
            if (car == null) car = GetComponent<TrafficCarController>();
            var dt = Time.deltaTime;
            var rolling = car != null ? Mathf.Clamp01(car.SpeedKph / 40f) : 0f;
            // Rolling, the trailer's own momentum keeps the fold opening; stopped, the
            // tyres scrub it dead.
            foldRate += Mathf.Sign(fold) * 45f * rolling * dt;
            foldRate = Mathf.MoveTowards(foldRate, 0f, (rolling > 0.1f ? 35f : 240f) * dt);
            fold += foldRate * dt;
            if (Mathf.Abs(fold) > MaxFold)
            {
                fold = Mathf.Sign(fold) * MaxFold;
                foldRate = -foldRate * 0.15f;
            }
            fold = FitBetweenRails(fold);
            Apply();
        }

        /// Half the trailer's width, and a little clearance from the rail.
        private const float TrailerHalfWidth = 1.4f;

        /// The largest fold, up to `wanted`, that keeps the trailer's tail inside the
        /// rails. Beyond the rail the ground falls away, and a trailer swung out over
        /// it hung in the air. Meeting the rail stops the swing dead.
        private float FitBetweenRails(float wanted)
        {
            if (car == null || TailInside(wanted)) return wanted;
            var inside = 0f;
            var outside = wanted;
            for (var i = 0; i < 10; i++)
            {
                var mid = (inside + outside) * 0.5f;
                if (TailInside(mid)) inside = mid;
                else outside = mid;
            }
            foldRate = 0f;
            return inside;
        }

        private bool TailInside(float foldDegrees)
        {
            // The tail in the rig's frame, then turned by the rig's own wreck yaw.
            var f = foldDegrees * Mathf.Deg2Rad;
            var x = -trailerLength * Mathf.Sin(f);
            var z = HitchZ - trailerLength * Mathf.Cos(f);
            var yaw = car.WreckYaw * Mathf.Deg2Rad;
            var across = x * Mathf.Cos(yaw) + z * Mathf.Sin(yaw);
            var lateral = car.LaneOffset + car.Direction * across;
            var edge = RoadPath.HalfWidthAt(car.RoadDistance) + RoadPath.ShoulderWidth - TrailerHalfWidth;
            return Mathf.Abs(lateral) <= edge;
        }

        private void Apply()
        {
            if (pivot != null) pivot.localRotation = Quaternion.Euler(0f, fold, 0f);
            if (car == null) car = GetComponent<TrafficCarController>();
            if (car != null) car.TrailerSwing = 0.5f * trailerLength * Mathf.Abs(Mathf.Sin(fold * Mathf.Deg2Rad));
        }
    }

    /// One log off a spilled load, simulated in road coordinates: distance along the
    /// road, offset across it, and height above it. It bounces, slides and rolls to a
    /// stop, bumps the player over it, and wrecks traffic that drives into it.
    public sealed class FallenLog : MonoBehaviour
    {
        private const float Gravity = 13f;
        private const float Lifetime = 70f;
        private const float ClearBehindPlayer = 70f;

        private TrafficCarController source;
        private bool byPlayer;
        private float radius;
        private float halfLength;

        private float distance;
        private float lateral;
        private float height;
        private Vector3 velocity; // x across, y up, z along the road, m/s
        private float yaw;       // relative to the road, degrees
        private float yawRate;
        private float pitch;
        private float pitchRate;
        private float spin;      // about the log's own axis
        private float spinRate;
        private float age;
        private float nextPlayerHit;
        private readonly HashSet<TrafficCarController> struck = new();

        private static ArcadeCarController player;

        public void Launch(TrafficCarController truck, float logRadius, float logHalfLength, Vector3 launch, bool player)
        {
            source = truck;
            byPlayer = player;
            radius = logRadius;
            halfLength = logHalfLength;

            var p = transform.position;
            distance = p.z;
            var centre = RoadPath.Center(distance);
            lateral = Vector3.Dot(p - centre, RoadPath.Right(distance));
            height = Mathf.Max(radius, p.y - centre.y);
            var forward = RoadPath.Forward(distance);
            var axis = transform.forward;
            axis.y = 0f;
            yaw = Vector3.SignedAngle(forward, axis.sqrMagnitude > 0.001f ? axis : forward, Vector3.up);
            velocity = launch;
            yawRate = Random.Range(-70f, 70f);
            pitchRate = Random.Range(-40f, 40f);
            spinRate = Random.Range(-360f, 360f);
            spin = Random.Range(0f, 360f);
            Place();
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            age += dt;
            var playerDistance = TrafficCarController.PlayerDistance;
            // Gone once driven past. The time limit only clears logs out of sight - it
            // used to apply anywhere, and logs vanished in front of a player who had
            // stopped to look at them.
            var behind = playerDistance - distance;
            var outOfSight = behind > 15f || -behind > 400f;
            if (behind > ClearBehindPlayer || (age > Lifetime && outOfSight))
            {
                Destroy(gameObject);
                return;
            }

            Simulate(dt);
            HitPlayer();
            HitTraffic();
            Place();
        }

        private void Simulate(float dt)
        {
            velocity.y -= Gravity * dt;
            distance += velocity.z * dt;
            lateral += velocity.x * dt;
            height += velocity.y * dt;
            yaw += yawRate * dt;
            pitch += pitchRate * dt;

            var grounded = false;
            // Lying flat its lowest point is its radius; tilted, the low end digs in.
            var floor = radius + Mathf.Abs(Mathf.Sin(pitch * Mathf.Deg2Rad)) * halfLength * 0.2f;
            if (height <= floor)
            {
                height = floor;
                if (velocity.y < -2.5f)
                {
                    velocity.y = -velocity.y * 0.28f;
                    yawRate += Random.Range(-60f, 60f);
                    spinRate += Random.Range(-200f, 200f);
                }
                else
                {
                    velocity.y = 0f;
                    grounded = true;
                }
                // Each contact knocks the tilt out of it; it ends up lying flat.
                pitchRate = -pitch * 6f;
            }

            if (grounded)
            {
                // Sliding friction along its length, rolling resistance across it: a
                // log rolls sideways far more readily than it slides endways.
                var along = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
                var flat = new Vector2(velocity.x, velocity.z);
                var axial = Vector2.Dot(flat, along);
                var roll = flat - along * axial;
                axial *= Mathf.Exp(-dt * 2.4f);
                roll *= Mathf.Exp(-dt * 0.9f);
                flat = along * axial + roll;
                velocity.x = flat.x;
                velocity.z = flat.y;
                var rollSign = Mathf.Sign(along.y * roll.x - along.x * roll.y);
                spinRate = Mathf.Lerp(spinRate, -rollSign * roll.magnitude / Mathf.Max(0.1f, radius) * Mathf.Rad2Deg, dt * 8f);
                yawRate *= Mathf.Exp(-dt * 3f);
                pitch = Mathf.Lerp(pitch, 0f, dt * 6f);
                if (flat.sqrMagnitude < 0.04f) { velocity.x = 0f; velocity.z = 0f; }
            }
            spin += spinRate * dt;

            // The barrier stops it at the road edge - all of it: clamped by its middle,
            // a log lying askew ended across the rail with one end over the drop.
            var edge = Mathf.Max(radius, RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth - AcrossExtent);
            if (Mathf.Abs(lateral) > edge)
            {
                lateral = Mathf.Sign(lateral) * edge;
                if (velocity.x * lateral > 0f) velocity.x = -velocity.x * 0.25f;
            }
        }

        private void Place()
        {
            transform.position = RoadPath.Point(distance, lateral, height);
            transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(0f, 0f, spin);
        }

        private float AlongExtent
        {
            get
            {
                var r = yaw * Mathf.Deg2Rad;
                return Mathf.Abs(halfLength * Mathf.Cos(r)) + Mathf.Abs(radius * Mathf.Sin(r));
            }
        }

        private float AcrossExtent
        {
            get
            {
                var r = yaw * Mathf.Deg2Rad;
                return Mathf.Abs(halfLength * Mathf.Sin(r)) + Mathf.Abs(radius * Mathf.Cos(r));
            }
        }

        /// Driving into a log bounces the car over it and scrubs speed; the log is
        /// kicked on up the road.
        private void HitPlayer()
        {
            if (Time.time < nextPlayerHit || height > 1.4f) return;
            if (player == null) player = FindFirstObjectByType<ArcadeCarController>();
            if (player == null || !player.isActiveAndEnabled || player.ClearsTraffic) return;

            var along = player.RoadDistance - distance;
            var across = player.LateralOffset - lateral;
            if (Mathf.Abs(along) > player.HalfLength + AlongExtent) return;
            if (Mathf.Abs(across) > player.HalfWidth + AcrossExtent) return;

            nextPlayerHit = Time.time + 0.6f;
            var speed = player.SpeedKph;
            if (speed < 15f) return;
            player.LaunchAirtime(Mathf.Lerp(3.2f, 6.5f, Mathf.Clamp01(speed / 200f)));
            player.SpeedKph = speed * 0.9f;
            GameState.ApplyDamage(2.5f);
            GameState.Show("🪵 LOG!");
            velocity += new Vector3(Mathf.Sign(-across) * Random.Range(1f, 3f), Random.Range(2f, 4f), speed / 3.6f * 0.35f);
            yawRate += Random.Range(-120f, 120f);
            pitchRate += Random.Range(-90f, 90f);
            if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.35f);
        }

        /// Traffic that drives into a log is wrecked by it - once per car per log.
        private void HitTraffic()
        {
            if (height > 1.4f) return;
            var cars = TrafficCarController.All;
            for (var i = cars.Count - 1; i >= 0; i--)
            {
                var car = cars[i];
                if (car == null || car == source || car.IsWreck || struck.Contains(car)) continue;
                var along = car.RoadDistance - distance;
                if (Mathf.Abs(along) > car.LongitudinalExtent + AlongExtent) continue;
                var across = car.LaneOffset - lateral;
                if (Mathf.Abs(across) > car.LateralExtent + AcrossExtent) continue;

                struck.Add(car);
                var wasViolator = car.IsViolator;
                car.Crash(across, Mathf.Max(car.SpeedKph, velocity.magnitude * 3.6f));
                velocity *= 0.5f;
                yawRate += Random.Range(-90f, 90f);
                if (byPlayer)
                {
                    GameState.Takedowns++;
                    GameState.Award(wasViolator ? 900 : 600, "🪵 LOG TAKEDOWN!");
                }
            }
        }
    }
}
