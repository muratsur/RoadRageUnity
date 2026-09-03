using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Burnout-style Aftertouch and Crashbreaker Director:
    /// Coordinates slow-motion Impact Time, real-time wreck steering impulses,
    /// kinetic Crashbreaker detonations, and chain-reaction traffic pileups.
    /// </summary>
    public sealed class RoadRageAftertouchDirector : MonoBehaviour
    {
        public static RoadRageAftertouchDirector Instance { get; private set; }

        private Camera mainCamera;
        private Transform playerTransform;
        private Rigidbody playerRb;
        private ArcadeCarController playerController;

        private ParticleSystem explosionFx;
        private ParticleSystem flameTrailFx;
        private ParticleSystem shockwaveFx;

        private float aftertouchTimer;
        private float slowRestTimer;
        private const float MaxAftertouchDuration = 5.5f;
        private readonly HashSet<TrafficCarController> impactedTraffic = new();

        public float TouchAftertouchSteer { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeVfx();
        }

        private void InitializeVfx()
        {
            var particleMat = Resources.Load<Material>("WeatherParticle") 
                ?? new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));

            // 1. Crashbreaker Shockwave Ring
            var shockGo = new GameObject("Crashbreaker Shockwave FX");
            shockGo.transform.SetParent(transform, false);
            shockwaveFx = shockGo.AddComponent<ParticleSystem>();
            var sMain = shockwaveFx.main;
            sMain.startLifetime = 0.55f;
            sMain.startSpeed = 35f;
            sMain.startSize = 0.8f;
            sMain.startColor = new Color(1f, 0.45f, 0.1f, 0.9f);
            sMain.maxParticles = 600;
            sMain.playOnAwake = false;
            var sEmission = shockwaveFx.emission;
            sEmission.enabled = false;
            var sShape = shockwaveFx.shape;
            sShape.shapeType = ParticleSystemShapeType.Circle;
            sShape.radius = 0.8f;
            var sRenderer = shockGo.GetComponent<ParticleSystemRenderer>();
            sRenderer.material = particleMat;

            // 2. Fiery Explosion Cloud
            var expGo = new GameObject("Crashbreaker Explosion FX");
            expGo.transform.SetParent(transform, false);
            explosionFx = expGo.AddComponent<ParticleSystem>();
            var eMain = explosionFx.main;
            eMain.startLifetime = 0.9f;
            eMain.startSpeed = 16f;
            eMain.startSize = 1.4f;
            eMain.startColor = new Color(1f, 0.7f, 0.2f, 1f);
            eMain.gravityModifier = -0.4f;
            eMain.maxParticles = 800;
            eMain.playOnAwake = false;
            var eEmission = explosionFx.emission;
            eEmission.enabled = false;
            var eShape = explosionFx.shape;
            eShape.shapeType = ParticleSystemShapeType.Sphere;
            eShape.radius = 1.2f;
            var eRenderer = expGo.GetComponent<ParticleSystemRenderer>();
            eRenderer.material = particleMat;

            // 3. Wreck Spark/Flame Trail
            var trailGo = new GameObject("Aftertouch Wreck Trail FX");
            trailGo.SetActive(false);
            trailGo.transform.SetParent(transform, false);
            flameTrailFx = trailGo.AddComponent<ParticleSystem>();
            var tMain = flameTrailFx.main;
            tMain.startLifetime = 0.7f;
            tMain.startSpeed = 4f;
            tMain.startSize = 0.5f;
            tMain.startColor = new Color(1f, 0.35f, 0.05f, 0.8f);
            tMain.maxParticles = 400;
            tMain.playOnAwake = false;
            var tEmission = flameTrailFx.emission;
            tEmission.rateOverTime = 55f;
            var tShape = flameTrailFx.shape;
            tShape.shapeType = ParticleSystemShapeType.Sphere;
            tShape.radius = 0.4f;
            var tRenderer = trailGo.GetComponent<ParticleSystemRenderer>();
            tRenderer.material = particleMat;
        }

        public void BindCameraAndPlayer(Camera cam, Transform player)
        {
            mainCamera = cam;
            playerTransform = player;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
                playerRb = player.GetComponent<Rigidbody>();
            }
        }

        /// <summary>
        /// Initiates the Burnout-style Aftertouch slow-motion crash sequence.
        /// </summary>
        public void TriggerAftertouch(Transform playerCar, ArcadeCarController controller, Vector3 initialTumbleVelocity, Vector3 initialTorque)
        {
            if (GameState.IsAftertouchActive || GameState.RunOver) return;

            playerTransform = playerCar;
            playerController = controller;
            GameState.IsAftertouchActive = true;
            GameState.CrashbreakerReady = true;
            GameState.CrashbreakerUsed = false;
            GameState.AftertouchTakedowns = 0;
            GameState.PileupDamage = 12500; // Base vehicle write-off cost
            impactedTraffic.Clear();
            aftertouchTimer = 0f;
            slowRestTimer = 0f;

            // 1. Disable kinematic lane driving
            if (playerController != null) playerController.enabled = false;

            // 2. Convert player to active tumbling rigid body
            if (playerRb == null && playerCar != null)
            {
                playerRb = playerCar.gameObject.AddComponent<Rigidbody>();
                playerRb.mass = 1500f;
                playerRb.linearDamping = 0.35f;
                playerRb.angularDamping = 0.4f;
                playerRb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            if (playerRb != null)
            {
                playerRb.isKinematic = false;
                playerRb.useGravity = true;
                playerRb.linearVelocity = initialTumbleVelocity;
                playerRb.angularVelocity = initialTorque;
            }

            // 3. Enter Impact Time Slow-Motion (0.22x speed)
            Time.timeScale = 0.22f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;

            // 4. Trigger Heavy Crash Audio & Slow-Mo DSP
            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayCrash(1.6f);
                RoadRageAudioBridge.Instance.SetSlowMotionFilter(true);
            }

            // 5. Attach & activate flame trail
            if (flameTrailFx != null && playerCar != null)
            {
                flameTrailFx.transform.SetParent(playerCar, false);
                flameTrailFx.transform.localPosition = Vector3.zero;
                flameTrailFx.Play();
            }

            GameState.Show("💥 AFTERTOUCH! STEER YOUR WRECK!");
        }

        private void FixedUpdate()
        {
            if (!GameState.IsAftertouchActive || playerRb == null) return;

            // 1. Read Aftertouch Steer Input
            var steer = Mathf.Clamp(GameInput.GetSteer() + TouchAftertouchSteer, -1f, 1f);

            // 2. Apply Aftertouch Lateral Steering Impulses
            // Steers the sliding wreck smoothly across highway lanes into traffic
            var roadHeading = RoadPath.Forward(playerController != null ? playerController.RoadDistance : 0f);
            var roadRight = Vector3.Cross(Vector3.up, roadHeading).normalized;

            var steerForce = roadRight * (steer * 9500f * playerRb.mass * Time.fixedDeltaTime);
            playerRb.AddForce(steerForce, ForceMode.Force);

            // Aerodynamic roll torque to keep the tumble dramatic and controllable
            var rollTorque = roadHeading * (-steer * 4200f * playerRb.mass * Time.fixedDeltaTime);
            playerRb.AddTorque(rollTorque, ForceMode.Force);

            // Gentle road-surface suction so the car slides and rolls along the asphalt
            playerRb.AddForce(Vector3.down * (45f * playerRb.mass * Time.fixedDeltaTime), ForceMode.Force);

            // 3. Check for secondary traffic collisions during Aftertouch
            DetectTrafficPileupCollisions();
        }

        private void Update()
        {
            if (!GameState.IsAftertouchActive) return;

            aftertouchTimer += Time.unscaledDeltaTime;

            // 1. Check for Crashbreaker detonation trigger (Space, Gamepad South, or Enter)
            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var detonatePressed = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) ||
                                  (pad != null && pad.buttonSouth.wasPressedThisFrame);

            if (detonatePressed && GameState.CrashbreakerReady)
            {
                DetonateCrashbreaker();
            }

            // 2. Cinematic Crash Cam tracking
            UpdateCrashCam();

            // 3. Monitor wreck velocity to conclude aftertouch
            var speed = playerRb != null ? playerRb.linearVelocity.magnitude : 0f;
            if (speed < 3.2f || aftertouchTimer >= MaxAftertouchDuration)
            {
                slowRestTimer += Time.unscaledDeltaTime;
                if (slowRestTimer >= 1.2f || aftertouchTimer >= MaxAftertouchDuration)
                {
                    EndAftertouchSequence();
                }
            }
            else
            {
                slowRestTimer = 0f;
            }
        }

        /// <summary>
        /// Detonates the Crashbreaker: launches nearby vehicles skyward, creates multi-car pileups.
        /// </summary>
        public void DetonateCrashbreaker()
        {
            if (!GameState.IsAftertouchActive || !GameState.CrashbreakerReady || playerTransform == null) return;

            GameState.CrashbreakerReady = false;
            GameState.CrashbreakerUsed = true;
            var origin = playerTransform.position;

            // 1. Spawn Massive Shockwave & Explosion VFX
            if (shockwaveFx != null)
            {
                shockwaveFx.transform.position = origin;
                shockwaveFx.Emit(85);
            }
            if (explosionFx != null)
            {
                explosionFx.transform.position = origin;
                explosionFx.Emit(120);
            }

            // 2. Play Detonation SFX
            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayCrash(2.2f);
                RoadRageAudioBridge.Instance.PlayTakedownStinger();
            }

            // 3. Launch nearby traffic with radial explosion physics.
            //
            // Found through the traffic registry, not OverlapSphere. Traffic is spawned
            // with every collider destroyed and none on the root, so the sphere had
            // nothing to hit and this blast has never moved a single car.
            var vehiclesBlown = 0;
            var cars = TrafficCarController.All;
            for (var i = 0; i < cars.Count; i++)
            {
                var traffic = cars[i];
                if (traffic != null && !impactedTraffic.Contains(traffic) &&
                    (traffic.transform.position - origin).sqrMagnitude < 22f * 22f)
                {
                    impactedTraffic.Add(traffic);
                    vehiclesBlown++;

                    // Hand it over before touching the rigidbody, or the controller
                    // forces it kinematic again on the next frame and the impulse is lost.
                    traffic.ReleaseToPhysics();
                    var trb = traffic.GetComponent<Rigidbody>();
                    if (trb == null) trb = traffic.gameObject.AddComponent<Rigidbody>();
                    trb.isKinematic = false;
                    trb.mass = 1400f;
                    trb.AddExplosionForce(38000f, origin, 24f, 5.5f, ForceMode.Impulse);
                    trb.AddTorque(Random.insideUnitSphere * 45000f, ForceMode.Impulse);

                    GameState.AftertouchTakedowns++;
                    var damageAdded = Random.Range(12000, 32000);
                    GameState.PileupDamage += damageAdded;
                    GameState.Award(450, "💥 CRASHBREAKER PILEUP");
                }
            }

            // Bonus explosion pop to the player wreck itself
            if (playerRb != null)
            {
                playerRb.AddForce((Vector3.up * 16f + Random.insideUnitSphere * 8f) * playerRb.mass, ForceMode.Impulse);
                playerRb.AddTorque(Random.insideUnitSphere * 35f * playerRb.mass, ForceMode.Impulse);
            }

            GameState.Show($"🔥 CRASHBREAKER! {vehiclesBlown} CARS DETONATED!");
        }

        private void DetectTrafficPileupCollisions()
        {
            if (playerTransform == null) return;

            // Same again: the registry, because there are no traffic colliders to find.
            var cars = TrafficCarController.All;
            for (var i = 0; i < cars.Count; i++)
            {
                var traffic = cars[i];
                if (traffic != null && !impactedTraffic.Contains(traffic) &&
                    (traffic.transform.position - playerTransform.position).sqrMagnitude < 3.2f * 3.2f)
                {
                    impactedTraffic.Add(traffic);
                    // Hand it over before touching the rigidbody, or the controller
                    // forces it kinematic again on the next frame and the impulse is lost.
                    traffic.ReleaseToPhysics();
                    var trb = traffic.GetComponent<Rigidbody>();
                    if (trb == null) trb = traffic.gameObject.AddComponent<Rigidbody>();
                    trb.isKinematic = false;
                    trb.mass = 1400f;
                    
                    var hitDir = (traffic.transform.position - playerTransform.position).normalized;
                    trb.AddForce((hitDir * 18f + Vector3.up * 8f) * trb.mass, ForceMode.Impulse);
                    trb.AddTorque(Random.insideUnitSphere * 25000f, ForceMode.Impulse);

                    GameState.AftertouchTakedowns++;
                    var damageAdded = Random.Range(8500, 24000);
                    GameState.PileupDamage += damageAdded;
                    GameState.Award(350, "💥 AFTERTOUCH TAKEDOWN");
                    
                    if (RoadRageAudioBridge.Instance != null)
                        RoadRageAudioBridge.Instance.PlayCrash(1.1f);
                }
            }
        }

        private void UpdateCrashCam()
        {
            if (mainCamera == null || playerTransform == null) return;

            var targetPos = playerTransform.position;
            var camOffset = new Vector3(0f, 6.2f, -12.5f);
            var desiredPos = targetPos + camOffset;

            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, desiredPos, Time.unscaledDeltaTime * 4.5f);
            mainCamera.transform.LookAt(targetPos + Vector3.up * 1.2f);
        }

        private void EndAftertouchSequence()
        {
            if (!GameState.IsAftertouchActive) return;

            GameState.IsAftertouchActive = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;

            if (flameTrailFx != null) flameTrailFx.Stop();
            if (RoadRageAudioBridge.Instance != null)
                RoadRageAudioBridge.Instance.SetSlowMotionFilter(false);

            GameState.EndRun();

             if (!GameState.IsAftertouchActive) return;
            GameState.IsAftertouchActive = false;
            if (flameTrailFx != null) flameTrailFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    // NEW: stop the wreck trail so it doesn't glow forever
    var trail = GameObject.Find("Aftertouch Wreck Trail FX");
    if (trail != null)
    {
        var ps = trail.GetComponentInChildren<ParticleSystem>(true);
        if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
        }
    }
}
