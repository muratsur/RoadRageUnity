using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Procedural Asphalt Tire Skidmark & Burning Rubber Smoke Director:
    /// Generates dynamic continuous tire track meshes on the road surface during drifts,
    /// power-slides, high-speed braking, and Aftertouch wrecks with tire smoke VFX and squeal audio.
    /// </summary>
    public sealed class RoadRageSkidmarkDirector : MonoBehaviour
    {
        public static RoadRageSkidmarkDirector Instance { get; private set; }

        private Transform playerCar;
        private ArcadeCarController playerController;

        private Mesh skidMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private const int MaxSkidmarks = 256;
        private readonly List<Vector3> vertices = new(MaxSkidmarks * 4);
        private readonly List<Vector3> normals = new(MaxSkidmarks * 4);
        private readonly List<Vector2> uvs = new(MaxSkidmarks * 4);
        private readonly List<Color> colors = new(MaxSkidmarks * 4);
        private readonly List<int> triangles = new(MaxSkidmarks * 6);

        private int leftLastIndex = -1;
        private int rightLastIndex = -1;

        private ParticleSystem leftSmokeFx;
        private ParticleSystem rightSmokeFx;

        private float driftIntensity;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeSkidmarkRenderer();
            InitializeTireSmoke();
        }

        private void InitializeSkidmarkRenderer()
        {
            var skidObj = new GameObject("ProceduralSkidmarksMesh");
            skidObj.transform.SetParent(transform, false);

            meshFilter = skidObj.AddComponent<MeshFilter>();
            meshRenderer = skidObj.AddComponent<MeshRenderer>();

            skidMesh = new Mesh { name = "SkidmarksMesh" };
            skidMesh.MarkDynamic();
            meshFilter.sharedMesh = skidMesh;

            var skidMat = Resources.Load<Material>("DecalVariantAnchor");
            if (skidMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                skidMat = new Material(shader) { color = new Color(0.04f, 0.04f, 0.04f, 0.85f) };
            }
            meshRenderer.sharedMaterial = skidMat;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void InitializeTireSmoke()
        {
            var smokeMat = Resources.Load<Material>("WeatherParticle") 
                ?? new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));

            leftSmokeFx = CreateSmokeEmitter("Left Tire Smoke", smokeMat);
            rightSmokeFx = CreateSmokeEmitter("Right Tire Smoke", smokeMat);
        }

        private ParticleSystem CreateSmokeEmitter(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.65f;
            main.startSpeed = 3.5f;
            main.startSize = 0.65f;
            main.startColor = new Color(0.85f, 0.85f, 0.90f, 0.45f);
            main.maxParticles = 120;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 30f;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        public void BindPlayer(Transform player)
        {
            playerCar = player;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
                if (leftSmokeFx != null)
                {
                    leftSmokeFx.transform.SetParent(player, false);
                    leftSmokeFx.transform.localPosition = new Vector3(-0.75f, 0.15f, -1.3f);
                }
                if (rightSmokeFx != null)
                {
                    rightSmokeFx.transform.SetParent(player, false);
                    rightSmokeFx.transform.localPosition = new Vector3(0.75f, 0.15f, -1.3f);
                }
            }
        }

        private void LateUpdate()
        {
            if (playerCar == null || playerController == null || GameState.RunOver)
            {
                leftLastIndex = -1;
                rightLastIndex = -1;
                SetSmokeActive(false);
                if (RoadRageAudioBridge.Instance != null)
                    RoadRageAudioBridge.Instance.PlayTireSqueal(0f);
                return;
            }

            // Calculate drift & tire slip intensity
            var latVel = Mathf.Abs(playerController.LateralVelocity);
            var speed = playerController.SpeedKph;
            var is1left = UnityEngine.InputSystem.Keyboard.current != null && (UnityEngine.InputSystem.Keyboard.current.aKey.isPressed || UnityEngine.InputSystem.Keyboard.current.leftArrowKey.isPressed);
            var is1right = UnityEngine.InputSystem.Keyboard.current != null && (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed || UnityEngine.InputSystem.Keyboard.current.rightArrowKey.isPressed);
            var isBraking = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.sKey.isPressed;

            driftIntensity = 0f;
            if (latVel > 1.4f && speed > 25f)
            {
                driftIntensity = Mathf.Clamp01((latVel - 1.4f) / 4.5f);
            }
            if ((is1left || is1right) && speed > 80f)
            {
                driftIntensity = Mathf.Max(driftIntensity, 0.50f);
            }
            if (isBraking && speed > 55f)
            {
                driftIntensity = Mathf.Max(driftIntensity, 0.75f);
            }
            if (GameState.IsAftertouchActive)
            {
                driftIntensity = 1.0f;
            }

            // Audio & Smoke VFX
            var isSkidding = driftIntensity > 0.12f;
            SetSmokeActive(isSkidding);

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayTireSqueal(driftIntensity);
            }

            // Add tire marks
            if (isSkidding)
            {
                var leftTirePos = playerCar.TransformPoint(new Vector3(-0.75f, -0.38f, -1.3f));
                var rightTirePos = playerCar.TransformPoint(new Vector3(0.75f, -0.38f, -1.3f));

                leftLastIndex = AddSkidmarkSegment(leftTirePos, playerCar.right, leftLastIndex, driftIntensity);
                rightLastIndex = AddSkidmarkSegment(rightTirePos, playerCar.right, rightLastIndex, driftIntensity);
            }
            else
            {
                leftLastIndex = -1;
                rightLastIndex = -1;
            }
        }

        private int AddSkidmarkSegment(Vector3 pos, Vector3 right, int lastIndex, float intensity)
        {
            var halfWidth = 0.14f;
            var normal = Vector3.up;
            var p0 = pos - right * halfWidth + normal * 0.02f;
            var p1 = pos + right * halfWidth + normal * 0.02f;

            var alpha = Mathf.Clamp01(intensity * 0.85f);
            var markColor = new Color(0.04f, 0.04f, 0.04f, alpha);

            // Ring buffer capacity check
            if (vertices.Count >= MaxSkidmarks * 4)
            {
                vertices.RemoveRange(0, 4);
                normals.RemoveRange(0, 4);
                uvs.RemoveRange(0, 4);
                colors.RemoveRange(0, 4);
                if (triangles.Count >= 6) triangles.RemoveRange(0, 6);
                for (var i = 0; i < triangles.Count; i++)
                {
                    triangles[i] = Mathf.Max(0, triangles[i] - 4);
                }
                if (lastIndex >= 4) lastIndex -= 4;
                else lastIndex = -1;
            }

            var currentIndex = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            colors.Add(markColor);
            colors.Add(markColor);

            if (lastIndex >= 0 && lastIndex < vertices.Count - 2)
            {
                var dist = Vector3.Distance(pos, (vertices[lastIndex] + vertices[lastIndex + 1]) * 0.5f);
                if (dist < 5.0f && dist > 0.08f)
                {
                    triangles.Add(lastIndex);
                    triangles.Add(lastIndex + 1);
                    triangles.Add(currentIndex);

                    triangles.Add(currentIndex);
                    triangles.Add(lastIndex + 1);
                    triangles.Add(currentIndex + 1);
                }
            }

            UpdateMesh();
            return currentIndex;
        }

        private void UpdateMesh()
        {
            if (skidMesh == null) return;
            skidMesh.Clear();
            skidMesh.SetVertices(vertices);
            skidMesh.SetNormals(normals);
            skidMesh.SetUVs(0, uvs);
            skidMesh.SetColors(colors);
            skidMesh.SetTriangles(triangles, 0);
        }

        private void SetSmokeActive(bool active)
        {
            if (leftSmokeFx != null)
            {
                var em = leftSmokeFx.emission;
                if (em.enabled != active)
                {
                    em.enabled = active;
                    if (active && !leftSmokeFx.isPlaying) leftSmokeFx.Play();
                    else if (!active && leftSmokeFx.isPlaying) leftSmokeFx.Stop();
                }
            }
            if (rightSmokeFx != null)
            {
                var em = rightSmokeFx.emission;
                if (em.enabled != active)
                {
                    em.enabled = active;
                    if (active && !rightSmokeFx.isPlaying) rightSmokeFx.Play();
                    else if (!active && rightSmokeFx.isPlaying) rightSmokeFx.Stop();
                }
            }
        }
    }
}
