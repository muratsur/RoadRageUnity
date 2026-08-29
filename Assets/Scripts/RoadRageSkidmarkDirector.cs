using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Procedural Asphalt Tire Skidmark & Burning Rubber Smoke Director:
    /// Uses high-performance tire trail renderers attached directly to the rear wheels
    /// to generate continuous asphalt tire marks during drifts, power-slides, and crashes.
    /// </summary>
    public sealed class RoadRageSkidmarkDirector : MonoBehaviour
    {
        public static RoadRageSkidmarkDirector Instance { get; private set; }

        private Transform playerCar;
        private ArcadeCarController playerController;

        private TrailRenderer leftTrail;
        private TrailRenderer rightTrail;

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
            InitializeTireSmoke();
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

        private TrailRenderer CreateTireTrail(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 3.2f;
            trail.startWidth = 0.26f;
            trail.endWidth = 0.26f;
            trail.minVertexDistance = 0.25f;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var trailMat = new Material(shader) { color = new Color(0.08f, 0.08f, 0.08f, 0.75f) };
            trail.sharedMaterial = trailMat;

            // Fade alpha over trail lifetime
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new(new Color(0.08f, 0.08f, 0.08f), 0f), new(new Color(0.08f, 0.08f, 0.08f), 1f) },
                new GradientAlphaKey[] { new(0.75f, 0f), new(0.55f, 0.7f), new(0f, 1f) }
            );
            trail.colorGradient = gradient;

            return trail;
        }

        public void BindPlayer(Transform player)
        {
            playerCar = player;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();

                // Setup Trails parented to car rear wheels
                if (leftTrail != null) Destroy(leftTrail.gameObject);
                if (rightTrail != null) Destroy(rightTrail.gameObject);

                leftTrail = CreateTireTrail("Left Tire Trail", player, new Vector3(-0.75f, -0.32f, -1.3f));
                rightTrail = CreateTireTrail("Right Tire Trail", player, new Vector3(0.75f, -0.32f, -1.3f));

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
            if (playerCar == null || playerController == null || GameState.RunOver || 
                (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive) ||
                playerController.CountdownTimer > 0f)
            {
                SetTrailsEmitting(false);
                SetSmokeActive(false);
                if (RoadRageAudioBridge.Instance != null)
                    RoadRageAudioBridge.Instance.PlayTireSqueal(0f);
                return;
            }

            // Calculate drift & tire slip intensity
            var isBraking = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.sKey.isPressed;
            var speed = playerController.SpeedKph;

            driftIntensity = 0f;
            if (isBraking && speed > 70f)
            {
                driftIntensity = 0.55f;
            }
            if (GameState.IsAftertouchActive)
            {
                driftIntensity = 1.0f;
            }

            // If airborne, stop skidding
            if (playerController.IsAirborne)
            {
                driftIntensity = 0f;
            }

            // Audio & Smoke VFX & Trails
            var isSkidding = driftIntensity > 0.40f;
            SetTrailsEmitting(isSkidding);
            SetSmokeActive(isSkidding);

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayTireSqueal(GameState.IsAftertouchActive ? 0.75f : 0f);
            }
        }

        private void SetTrailsEmitting(bool active)
        {
            if (leftTrail != null && leftTrail.emitting != active)
            {
                leftTrail.emitting = active;
            }
            if (rightTrail != null && rightTrail.emitting != active)
            {
                rightTrail.emitting = active;
            }
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
