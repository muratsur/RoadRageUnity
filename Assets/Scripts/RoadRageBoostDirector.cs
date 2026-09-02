using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Burnout Boost & Infinite Boost Chain Director:
    /// Manages the risk/reward Nitro boost tank, near-miss detection, oncoming lane charge,
    /// high-speed FOV speed warp, exhaust flame VFX, and Burnout chain refills.
    /// </summary>
    public sealed class RoadRageBoostDirector : MonoBehaviour
    {
        public static RoadRageBoostDirector Instance { get; private set; }

        public float BoostAmount { get; private set; } = 40f; // 0 to 100
        public const float MaxBoost = 100f;
        public bool IsBoosting { get; private set; }
        public int BurnoutChain { get; private set; } = 0;
        public bool IsFullBoost => BoostAmount >= MaxBoost;

        private Transform playerCar;
        private ArcadeCarController playerController;
        private Camera mainCamera;
        private float originalFov = 64f;
        private float nearMissCooldown;
        private float continuousBurnTimer;
        private int nearMissesDuringBurn;
        private bool startedBurnAtMax;

        private ParticleSystem leftFlameFx;
        private ParticleSystem rightFlameFx;

        public bool TouchNitroPressed { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeExhaustVfx();
        }

        private ParticleSystem speedStreaksFx;

        private void InitializeExhaustVfx()
        {
            var particleMat = Resources.Load<Material>("WeatherParticle") 
                ?? new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));

            leftFlameFx = CreateFlameEmitter("Left Exhaust Flame", particleMat);
            rightFlameFx = CreateFlameEmitter("Right Exhaust Flame", particleMat);
            speedStreaksFx = CreateSpeedStreaksEmitter("Camera Speed Streaks", particleMat);
        }

        private ParticleSystem CreateFlameEmitter(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.22f;
            main.startSpeed = 22f;
            main.startSize = 0.42f;
            main.startColor = new Color(0.12f, 0.85f, 1f, 0.95f); // High-energy cyan plasma flame
            main.maxParticles = 180;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 55f;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.09f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private ParticleSystem CreateSpeedStreaksEmitter(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.15f;
            main.startSpeed = 45f;
            main.startSize = 0.12f;
            main.startColor = new Color(0.75f, 0.90f, 1f, 0.45f);
            main.maxParticles = 80;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 40f;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3.5f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        public void BindPlayer(Transform player, Camera cam)
        {
            playerCar = player;
            mainCamera = cam;
            if (cam != null)
            {
                originalFov = cam.fieldOfView;
                if (speedStreaksFx != null)
                {
                    speedStreaksFx.transform.SetParent(cam.transform, false);
                    speedStreaksFx.transform.localPosition = new Vector3(0f, 0f, 4.5f);
                    speedStreaksFx.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
                if (leftFlameFx != null)
                {
                    leftFlameFx.transform.SetParent(player, false);
                    leftFlameFx.transform.localPosition = new Vector3(-0.55f, 0.32f, -2.1f);
                    leftFlameFx.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
                if (rightFlameFx != null)
                {
                    rightFlameFx.transform.SetParent(player, false);
                    rightFlameFx.transform.localPosition = new Vector3(0.55f, 0.32f, -2.1f);
                    rightFlameFx.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
        }

        public void AddBoost(float amount, string reason = "")
        {
            if (IsBoosting && BoostAmount >= MaxBoost) return;
            BoostAmount = Mathf.Clamp(BoostAmount + amount, 0f, MaxBoost);
            if (!string.IsNullOrEmpty(reason))
            {
                GameState.Show($"{reason}  +{amount:0}% BOOST");
            }
        }

        private void Update()
        {
            if (playerCar == null || playerController == null || GameState.RunOver || GameState.IsAftertouchActive)
            {
                if (IsBoosting) StopBoosting();
                SetSpeedStreaksActive(false);
                return;
            }

            // 1. Detect Boost Input (Shift, Gamepad Button East / Right Trigger, Touch)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            var boostInput = (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed || kb.spaceKey.isPressed)) ||
                             (pad != null && (pad.buttonEast.isPressed || pad.rightTrigger.isPressed)) ||
                             TouchNitroPressed;

            if (boostInput && BoostAmount > 0f)
            {
                if (!IsBoosting) StartBoosting();
                // Drain boost: burns full tank in ~4.5 seconds
                BoostAmount = Mathf.Max(0f, BoostAmount - Time.deltaTime * 22f);
                continuousBurnTimer += Time.deltaTime;

                if (BoostAmount <= 0f)
                {
                    OnBoostDepleted();
                }
            }
            else if (IsBoosting)
            {
                StopBoosting();
            }

            // 2. Passive Boost Accumulation through Dangerous Driving
            if (!IsBoosting)
            {
                // Oncoming Wrong-Way Driving charges boost!
                if (playerController.LateralOffset < -1.5f && playerController.SpeedKph > 65f)
                {
                    AddBoost(Time.deltaTime * 12.5f);
                }
                // High Speed charges modest boost
                if (playerController.SpeedKph > 140f)
                {
                    AddBoost(Time.deltaTime * 4.5f);
                }
            }

            // 3. Near-Miss Scanner
            DetectNearMisses();

            // 4. Dynamic Camera FOV Speed Warp & Radial Speed Streaks
            var speedBonus = Mathf.Clamp((playerController.SpeedKph - 110f) * 0.06f, 0f, 7f);
            var boostBonus = IsBoosting ? 16f : 0f;
            var targetFov = originalFov + speedBonus + boostBonus;

            if (mainCamera != null)
            {
                mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFov, Time.deltaTime * 7.5f);
            }

            var showStreaks = IsBoosting || playerController.SpeedKph > 185f;
            SetSpeedStreaksActive(showStreaks);
        }

        private void SetSpeedStreaksActive(bool active)
        {
            if (speedStreaksFx == null) return;
            var em = speedStreaksFx.emission;
            if (em.enabled != active)
            {
                em.enabled = active;
                if (active && !speedStreaksFx.isPlaying) speedStreaksFx.Play();
                else if (!active && speedStreaksFx.isPlaying) speedStreaksFx.Stop();
            }
        }

        private void StartBoosting()
        {
            IsBoosting = true;
            startedBurnAtMax = BoostAmount >= 95f;
            continuousBurnTimer = 0f;
            nearMissesDuringBurn = 0;

            if (leftFlameFx != null)
            {
                var em = leftFlameFx.emission;
                em.enabled = true;
                leftFlameFx.Play();
            }
            if (rightFlameFx != null)
            {
                var em = rightFlameFx.emission;
                em.enabled = true;
                rightFlameFx.Play();
            }

            if (RoadRageHapticsDirector.Instance != null)
            {
                RoadRageHapticsDirector.Instance.TriggerLightHaptic(0.25f);
            }

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayNitro();
            }
        }

        private void StopBoosting()
        {
            if (IsBoosting)
            {
                if (RoadRageAudioBridge.Instance != null)
                {
                    RoadRageAudioBridge.Instance.PlayBackfirePop();
                }
            }
            IsBoosting = false;
            if (leftFlameFx != null)
            {
                var em = leftFlameFx.emission;
                em.enabled = false;
                leftFlameFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (rightFlameFx != null)
            {
                var em = rightFlameFx.emission;
                em.enabled = false;
                rightFlameFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (BoostAmount < MaxBoost) BurnoutChain = 0;
        }

        private void OnBoostDepleted()
        {
            // Check for Burnout Chain: if started at 100% and racked up near-misses / takedowns during the burn
            if (startedBurnAtMax && (nearMissesDuringBurn >= 2 || continuousBurnTimer >= 3.8f))
            {
                BurnoutChain++;
                BoostAmount = MaxBoost; // Instant Refill!
                GameState.Award(1200 * BurnoutChain, $"🔥 BURNOUT x{BurnoutChain} CHAIN!");
                GameState.Show($"🔥 BURNOUT x{BurnoutChain} CHAIN! 100% BOOST REFILLED!");
                
                if (RoadRageAudioBridge.Instance != null)
                {
                    RoadRageAudioBridge.Instance.PlayTakedownStinger();
                }
                // Keep boosting!
                startedBurnAtMax = true;
                continuousBurnTimer = 0f;
                nearMissesDuringBurn = 0;
                return;
            }

            StopBoosting();
        }

        private void DetectNearMisses()
        {
            if (nearMissCooldown > 0f)
            {
                nearMissCooldown -= Time.deltaTime;
                return;
            }

            // Queried against the traffic registry rather than through physics.
            //
            // OverlapSphere found nothing here, ever: traffic cars are spawned with all
            // their colliders destroyed and never given one on the root, so there was no
            // collider in the world for the sphere to hit. The near miss - the reward
            // that pays for threading a gap at speed, and the entry point to the whole
            // risk-buys-boost loop this game runs on - has never once fired. Road space
            // is where these positions actually live, and testing there is both exact
            // and cheaper than a physics query.
            if (playerController.SpeedKph <= 75f) return;

            var cars = TrafficCarController.All;
            for (var i = 0; i < cars.Count; i++)
            {
                var traffic = cars[i];
                if (traffic != null && !traffic.IsWreck)
                {
                    var lateralDist = Mathf.Abs(traffic.LaneOffset - playerController.LateralOffset);
                    var longDist = Mathf.Abs(traffic.RoadDistance - playerController.RoadDistance);

                    if (lateralDist < 2.6f && lateralDist > 0.8f && longDist < 4.2f)
                    {
                        nearMissCooldown = 0.55f;
                        AddBoost(20f, "⚡ NEAR MISS!");
                        GameState.Award(150, "⚡ NEAR MISS");
                        GameState.BumpDaily("nearmiss", 1f);
                        if (IsBoosting) nearMissesDuringBurn++;

                        if (RoadRageAudioBridge.Instance != null)
                        {
                            RoadRageAudioBridge.Instance.PlayNearMissChirp();
                        }
                        break;
                    }
                }
            }
        }

        public void RefillBoostMax()
        {
            BoostAmount = MaxBoost;
        }
    }
}
