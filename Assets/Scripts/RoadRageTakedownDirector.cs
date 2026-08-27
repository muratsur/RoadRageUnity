using System.Collections;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Burnout-style Takedown Director:
    /// Orchestrates hit-stop freeze frames, slow-motion rival wreck cameras,
    /// explosive vehicle physics torque, spark bursts, and arcade rewards.
    /// </summary>
    public class RoadRageTakedownDirector : MonoBehaviour
    {
        public static RoadRageTakedownDirector Instance { get; private set; }

        public bool IsTakedownActive { get; private set; }
        public Transform CurrentVictim { get; private set; }

        private Camera mainCamera;
        private Transform playerTransform;
        private ParticleSystem sparkFx;
        private ParticleSystem debrisFx;

        private Vector3 originalCamOffset = new Vector3(0f, 2.85f, -6.8f);
        private float takedownTimer = 0f;
        private const float TakedownDuration = 1.4f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InitializeVfx();
        }

        private void InitializeVfx()
        {
            // Procedural Spark Particle System
            var sparkGo = new GameObject("Takedown Spark FX");
            sparkGo.transform.SetParent(transform, false);
            sparkFx = sparkGo.AddComponent<ParticleSystem>();
            var main = sparkFx.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 18f;
            main.startSize = 0.18f;
            main.startColor = new Color(1f, 0.85f, 0.35f, 1f);
            main.maxParticles = 500;
            main.playOnAwake = false;

            var emission = sparkFx.emission;
            emission.enabled = false;

            var shape = sparkFx.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var sparkRenderer = sparkGo.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            var sparkMat = Resources.Load<Material>("WeatherParticle") ?? new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            sparkRenderer.material = sparkMat;

            // Procedural Debris System
            var debrisGo = new GameObject("Takedown Debris FX");
            debrisGo.transform.SetParent(transform, false);
            debrisFx = debrisGo.AddComponent<ParticleSystem>();
            var dMain = debrisFx.main;
            dMain.startLifetime = 1.2f;
            dMain.startSpeed = 14f;
            dMain.startSize = 0.35f;
            dMain.startColor = new Color(0.2f, 0.2f, 0.22f, 1f);
            dMain.gravityModifier = 2.5f;
            dMain.maxParticles = 300;
            dMain.playOnAwake = false;

            var dEmission = debrisFx.emission;
            dEmission.enabled = false;

            var dShape = debrisFx.shape;
            dShape.shapeType = ParticleSystemShapeType.Cone;
            dShape.angle = 45f;
            dShape.radius = 0.4f;

            var dRenderer = debrisGo.GetComponent<ParticleSystemRenderer>();
            dRenderer.material = sparkMat;
        }

        public void BindCameraAndPlayer(Camera cam, Transform player)
        {
            mainCamera = cam;
            playerTransform = player;
        }

        /// <summary>
        /// Initiates a Burnout-style Takedown sequence on the target vehicle.
        /// </summary>
        public void TriggerTakedown(Transform victim, Vector3 impactPoint, Vector3 impactNormal, float playerSpeedKph)
        {
            if (victim == null || IsTakedownActive) return;

            StartCoroutine(TakedownSequence(victim, impactPoint, impactNormal, playerSpeedKph));
        }

        private IEnumerator TakedownSequence(Transform victim, Vector3 impactPoint, Vector3 impactNormal, float playerSpeedKph)
        {
            IsTakedownActive = true;
            CurrentVictim = victim;

            // 1. Play Heavy Crash Audio & Audio Filter
            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayCrash(1.2f);
                RoadRageAudioBridge.Instance.PlayTakedownStinger();
                RoadRageAudioBridge.Instance.SetSlowMotionFilter(true);
            }

            // 2. Spawn Kinetic Spark & Debris Bursts
            if (sparkFx != null)
            {
                sparkFx.transform.position = impactPoint;
                sparkFx.Emit(60);
            }
            if (debrisFx != null)
            {
                debrisFx.transform.position = impactPoint;
                debrisFx.transform.forward = impactNormal;
                debrisFx.Emit(35);
            }

            // 3. Apply Explosive Physics to Victim
            var rb = victim.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = victim.gameObject.AddComponent<Rigidbody>();
                rb.mass = 1350f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            rb.isKinematic = false;
            var launchForce = (impactNormal * 12f + Vector3.up * 14f + victim.forward * (playerSpeedKph * 0.15f)) * rb.mass;
            rb.AddForce(launchForce, ForceMode.Impulse);
            rb.AddTorque(new Vector3(Random.Range(-25f, 25f), Random.Range(15f, 35f), Random.Range(-45f, 45f)) * rb.mass, ForceMode.Impulse);

            // 4. Hit-Stop Impact Freeze (100ms kinetic punch)
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(0.09f);

            // 5. Enter Slow-Motion Takedown (`0.32x`)
            Time.timeScale = 0.32f;
            takedownTimer = 0f;

            // 6. Award Arcade Bonuses
            GameState.Takedowns++;
            GameState.Cash += 5000;
            GameState.Score += 25000;
            GameState.Combo++;
            GameState.ComboTimer = 6f;
            GameState.Message = "TAKEDOWN! +$5,000";

            // Refill player nitro
            if (playerTransform != null)
            {
                var controller = playerTransform.GetComponent<ArcadeCarController>();
                if (controller != null) controller.RefillNitro();
            }

            // 7. Track victim during slow-mo
            while (takedownTimer < TakedownDuration)
            {
                takedownTimer += Time.unscaledDeltaTime;
                yield return null;
            }

            // 8. Snap back to normal gameplay speed
            var restoreTimer = 0f;
            while (restoreTimer < 0.25f)
            {
                restoreTimer += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.Lerp(0.32f, 1.0f, restoreTimer / 0.25f);
                yield return null;
            }
            Time.timeScale = 1.0f;

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.SetSlowMotionFilter(false);
            }

            IsTakedownActive = false;
            CurrentVictim = null;
        }

        /// <summary>
        /// Computes dynamic cinematic camera position and look-at target during takedown.
        /// </summary>
        public bool TryGetTakedownCameraPose(out Vector3 cameraPos, out Quaternion cameraRot)
        {
            cameraPos = Vector3.zero;
            cameraRot = Quaternion.identity;

            if (!IsTakedownActive || CurrentVictim == null) return false;

            var targetPos = CurrentVictim.position;
            var progress = takedownTimer / TakedownDuration;

            // Dynamic low-angle orbital sweep around the tumbling car
            var angle = progress * 135f;
            var offset = Quaternion.Euler(18f, angle, 0f) * new Vector3(0f, 2.2f, -6.5f);
            cameraPos = targetPos + offset;

            // Dutch angle tilt for high drama
            var lookDir = (targetPos + Vector3.up * 0.8f - cameraPos).normalized;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                var baseRot = Quaternion.LookRotation(lookDir, Vector3.up);
                var rollTilt = Mathf.Sin(progress * Mathf.PI) * 9f;
                cameraRot = baseRot * Quaternion.Euler(0f, 0f, rollTilt);
            }
            return true;
        }
    }
}

