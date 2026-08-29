using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Police Pursuit & 5-Star Heat Director:
    /// Coordinates escalating pursuit heat, police cruisers with flashing emergency lightbars,
    /// PIT maneuvers, high-speed interception AI, tactical roadblocks, and spike strips.
    /// </summary>
    public sealed class RoadRagePolicePursuitDirector : MonoBehaviour
    {
        public static RoadRagePolicePursuitDirector Instance { get; private set; }

        public int HeatLevel { get; private set; } = 0; // 0 to 5 Stars
        public float HeatProgress { get; private set; } = 0f; // 0.0 to 1.0 towards next star
        public bool IsPursuitActive => HeatLevel > 0;

        private Transform playerCar;
        private ArcadeCarController playerController;
        private Camera mainCamera;

        private readonly List<PoliceVehicleController> activePolice = new();
        private readonly List<GameObject> activeRoadblocks = new();

        private float spawnTimer;
        private float roadblockTimer;
        private float sirenAudioTimer;
        private AudioSource sirenSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            sirenSource = gameObject.AddComponent<AudioSource>();
            sirenSource.loop = true;
            sirenSource.playOnAwake = false;
            sirenSource.spatialBlend = 0.05f;
            sirenSource.volume = 0.35f;
            sirenSource.pitch = 1.0f;
            // Authentic recorded police siren from project audio assets
            sirenSource.clip = Resources.Load<AudioClip>("Audio/SFX/Horns/Sirens/siren_1")
                ?? Resources.Load<AudioClip>("Audio/SFX/Horns/Sirens/siren_2")
                ?? Resources.Load<AudioClip>("Audio/SFX/Horns/Sirens/siren_3");
        }

        public void BindPlayer(Transform player, Camera cam)
        {
            playerCar = player;
            mainCamera = cam;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
            }
        }

        public void AddHeat(float amount)
        {
            if (GameState.RunOver) return;

            HeatProgress += amount;
            while (HeatProgress >= 1f && HeatLevel < 5)
            {
                HeatProgress -= 1f;
                HeatLevel++;
                OnHeatLevelUp();
            }
        }

        private void OnHeatLevelUp()
        {
            GameState.Show($"🚨 HEAT LEVEL {HeatLevel}! POLICE DISPATCHED!");
            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayTakedownStinger();
            }
            if (sirenSource != null && !sirenSource.isPlaying)
            {
                sirenSource.Play();
            }
        }

        private void Update()
        {
            if (playerCar == null || playerController == null || GameState.RunOver)
            {
                if (sirenSource != null && sirenSource.isPlaying) sirenSource.Stop();
                return;
            }

            // 1. Accumulate Heat based on speed and driving infractions
            if (playerController.SpeedKph > 125f)
            {
                AddHeat(Time.deltaTime * 0.045f);
            }
            if (playerController.LateralOffset < -1.5f) // Oncoming lane driving
            {
                AddHeat(Time.deltaTime * 0.065f);
            }

            // Manage siren volume based on closest active cop
            if (activePolice.Count > 0 && sirenSource != null)
            {
                if (!sirenSource.isPlaying) sirenSource.Play();
                var closestDist = float.MaxValue;
                foreach (var cop in activePolice)
                {
                    if (cop != null)
                    {
                        var d = Mathf.Abs(cop.RoadDistance - playerController.RoadDistance);
                        if (d < closestDist) closestDist = d;
                    }
                }
                sirenSource.volume = Mathf.Clamp01(1f - closestDist / 90f) * 0.5f;
            }
            else if (sirenSource != null && sirenSource.isPlaying)
            {
                sirenSource.Stop();
            }

            // 2. Dispatch Police Units
            if (HeatLevel > 0)
            {
                spawnTimer += Time.deltaTime;
                var maxUnits = Mathf.Min(HeatLevel, 3);
                var spawnInterval = Mathf.Max(3.5f, 9f - HeatLevel * 1.2f);

                if (spawnTimer >= spawnInterval && activePolice.Count < maxUnits)
                {
                    spawnTimer = 0f;
                    SpawnPoliceUnit();
                }

                // 3. Spawn Tactical Roadblocks (Heat Level 4+)
                if (HeatLevel >= 4)
                {
                    roadblockTimer += Time.deltaTime;
                    if (roadblockTimer >= 14f)
                    {
                        roadblockTimer = 0f;
                        SpawnRoadblock();
                    }
                }
            }

            // 4. Clean up stale roadblocks
            for (var i = activeRoadblocks.Count - 1; i >= 0; i--)
            {
                var rb = activeRoadblocks[i];
                if (rb == null || playerController.RoadDistance - rb.transform.position.z > 60f)
                {
                    if (rb != null) Destroy(rb);
                    activeRoadblocks.RemoveAt(i);
                }
            }
        }

        private void SpawnPoliceUnit()
        {
            var spawnBehind = Random.value > 0.35f;
            var distOffset = spawnBehind ? -45f : 65f;
            var spawnDist = RoadPath.Wrap(playerController.RoadDistance + distOffset);
            var spawnLane = spawnBehind ? playerController.LateralOffset + (Random.value > 0.5f ? 3.5f : -3.5f) : (Random.value > 0.5f ? 2.5f : -2.5f);

            var copObj = new GameObject($"Police Unit [{HeatLevel} Star]");
            copObj.transform.position = RoadPath.Point(spawnDist, spawnLane, 0.4f);
            copObj.transform.rotation = RoadPath.Rotation(spawnDist);

            var cop = copObj.AddComponent<PoliceVehicleController>();
            cop.Initialize(playerController, HeatLevel, spawnDist, spawnLane);
            activePolice.Add(cop);
        }

        private void SpawnRoadblock()
        {
            var targetDist = RoadPath.Wrap(playerController.RoadDistance + 175f);
            var root = new GameObject("Police Roadblock");
            root.transform.position = RoadPath.Point(targetDist, 0f, 0.4f);
            root.transform.rotation = RoadPath.Rotation(targetDist);

            var halfWidth = RoadPath.HalfWidthAt(targetDist);
            // Spawn 2 modern barricade cruisers with high-visibility emergency LED lights
            for (var i = -1; i <= 1; i += 2)
            {
                var lane = i * (halfWidth * 0.45f);
                var prefab = Resources.Load<GameObject>("Vehicles/SK_Veh_Preset_Sedan_01");
                GameObject barObj;
                if (prefab != null)
                {
                    barObj = Instantiate(prefab, root.transform);
                    barObj.transform.localPosition = new Vector3(lane, 0.45f, 0f);
                    barObj.transform.localRotation = Quaternion.Euler(0f, i > 0 ? 165f : 195f, 0f);
                    barObj.transform.localScale = Vector3.one * 0.96f;
                }
                else
                {
                    barObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    barObj.transform.SetParent(root.transform, false);
                    barObj.transform.localPosition = new Vector3(lane, 0.6f, 0f);
                    barObj.transform.localScale = new Vector3(2.2f, 1.3f, 4.4f);
                }
                barObj.name = "Barricade Cruiser";

                var rb = barObj.GetComponent<Rigidbody>() ?? barObj.AddComponent<Rigidbody>();
                rb.mass = 2800f;
            }

            activeRoadblocks.Add(root);
            GameState.Show("⚠️ POLICE ROADBLOCK AHEAD! FIND THE GAP!");
        }

        public void NotifyPoliceDestroyed(PoliceVehicleController cop)
        {
            activePolice.Remove(cop);
            GameState.Award(750 + HeatLevel * 250, "🚨 COP TAKEDOWN!");
            GameState.Show($"🚨 COP TAKEDOWN! +${750 + HeatLevel * 250}");
        }

        public void ResetPursuit()
        {
            HeatLevel = 0;
            HeatProgress = 0f;
            foreach (var cop in activePolice)
            {
                if (cop != null) Destroy(cop.gameObject);
            }
            activePolice.Clear();
            foreach (var rb in activeRoadblocks)
            {
                if (rb != null) Destroy(rb);
            }
            activeRoadblocks.Clear();
            if (sirenSource != null) sirenSource.Stop();
        }
    }

    /// <summary>
    /// AI controller for high-speed police pursuit cruisers with flashing lightbars and PIT maneuvers.
    /// </summary>
    public sealed class PoliceVehicleController : MonoBehaviour
    {
        public float RoadDistance { get; private set; }
        public float LateralOffset { get; private set; }
        public float SpeedKph { get; private set; } = 95f;

        private ArcadeCarController targetPlayer;
        private int unitHeatLevel;
        private Light redStrobe;
        private Light blueStrobe;
        private float strobeTimer;
        private bool isWrecked;

        public void Initialize(ArcadeCarController player, int heat, float startDist, float startLane)
        {
            targetPlayer = player;
            unitHeatLevel = heat;
            RoadDistance = startDist;
            LateralOffset = startLane;
            SpeedKph = player != null ? player.SpeedKph + 12f : 105f;

            BuildPoliceMesh();
            BuildLightbars();
        }

        private Material redLedMat;
        private Material blueLedMat;
        private Renderer[] redLeds;
        private Renderer[] blueLeds;

        private static void NormalizeVehicleVisual(GameObject visual, float targetLength)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontalLength > 0.01f) visual.transform.localScale *= targetLength / horizontalLength;
        }

        private void BuildPoliceMesh()
        {
            var modelName = unitHeatLevel >= 4 ? "SK_Veh_Preset_Muscle_01" : "SK_Veh_Preset_Sedan_01";
            var prefab = Resources.Load<GameObject>($"Vehicles/{modelName}");
            if (prefab != null)
            {
                var vehicleInstance = Instantiate(prefab, transform);
                vehicleInstance.name = "Police Interceptor Model";
                vehicleInstance.transform.localPosition = Vector3.zero;
                // Synty models use Z-up coordinates in FBX; local rotation must be (0, 0, 90)
                vehicleInstance.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                vehicleInstance.transform.localScale = Vector3.one;

                var renderers = vehicleInstance.GetComponentsInChildren<Renderer>();
                var paintMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.04f, 0.06f, 0.10f) // Dark Police Interceptor Navy
                };
                if (paintMat.HasProperty("_Smoothness")) paintMat.SetFloat("_Smoothness", 0.85f);
                if (paintMat.HasProperty("_Metallic")) paintMat.SetFloat("_Metallic", 0.65f);

                var livery = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_01_A");
                if (livery != null) paintMat.mainTexture = livery;

                foreach (var rend in renderers)
                {
                    rend.material = paintMat;
                }
                foreach (var col in vehicleInstance.GetComponentsInChildren<Collider>()) Destroy(col);
                NormalizeVehicleVisual(vehicleInstance, 4.8f);
            }
            else
            {
                // Fallback procedural cruiser
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(transform, false);
                body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                body.transform.localScale = new Vector3(2.0f, 1.25f, 4.4f);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.04f, 0.06f, 0.10f)
                };
                body.GetComponent<Renderer>().material = mat;
            }

            // Heavy Front Push-Bumper
            var bullbar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bullbar.name = "Police Bullbar";
            bullbar.transform.SetParent(transform, false);
            bullbar.transform.localPosition = new Vector3(0f, 0.55f, 2.3f);
            bullbar.transform.localScale = new Vector3(1.7f, 0.55f, 0.18f);
            var barMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.12f, 0.12f, 0.12f)
            };
            bullbar.GetComponent<Renderer>().material = barMat;
            Destroy(bullbar.GetComponent<Collider>());
        }

        private void BuildLightbars()
        {
            var lightbarRoot = new GameObject("Police LED Lightbar");
            lightbarRoot.transform.SetParent(transform, false);
            lightbarRoot.transform.localPosition = new Vector3(0f, 1.45f, -0.1f);

            var barFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barFrame.name = "Lightbar Frame";
            barFrame.transform.SetParent(lightbarRoot.transform, false);
            barFrame.transform.localScale = new Vector3(1.15f, 0.08f, 0.22f);
            barFrame.GetComponent<Renderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.05f, 0.05f, 0.05f) };
            Destroy(barFrame.GetComponent<Collider>());

            redLedMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            redLedMat.color = Color.red;
            redLedMat.EnableKeyword("_EMISSION");
            redLedMat.SetColor("_EmissionColor", Color.red * 4.5f);

            blueLedMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            blueLedMat.color = Color.blue;
            blueLedMat.EnableKeyword("_EMISSION");
            blueLedMat.SetColor("_EmissionColor", Color.blue * 4.5f);

            var r1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r1.transform.SetParent(lightbarRoot.transform, false);
            r1.transform.localPosition = new Vector3(-0.35f, 0.02f, 0f);
            r1.transform.localScale = new Vector3(0.35f, 0.12f, 0.20f);
            r1.GetComponent<Renderer>().material = redLedMat;
            Destroy(r1.GetComponent<Collider>());

            var b1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b1.transform.SetParent(lightbarRoot.transform, false);
            b1.transform.localPosition = new Vector3(0.35f, 0.02f, 0f);
            b1.transform.localScale = new Vector3(0.35f, 0.12f, 0.20f);
            b1.GetComponent<Renderer>().material = blueLedMat;
            Destroy(b1.GetComponent<Collider>());

            redLeds = new[] { r1.GetComponent<Renderer>() };
            blueLeds = new[] { b1.GetComponent<Renderer>() };
        }

        private bool isWrecked;
        private float wreckSlideDir;
        private float wreckYaw;
        private float targetWreckYaw;
        private float wreckRoll;

        private void Update()
        {
            if (isWrecked)
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, 0f, 45f * Time.deltaTime);
                LateralOffset += wreckSlideDir * 6.5f * Time.deltaTime;
                wreckYaw = Mathf.MoveTowards(wreckYaw, targetWreckYaw, 180f * Time.deltaTime);
                var forwardMove = SpeedKph / 3.6f * Time.deltaTime;
                RoadDistance = RoadPath.Wrap(RoadDistance + forwardMove);
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.4f);
                transform.rotation = RoadPath.Rotation(RoadDistance) * Quaternion.Euler(0f, wreckYaw, wreckRoll);
                return;
            }

            // 1. Alternate High-Intensity LED Emergency Strobes (Shader Emission)
            strobeTimer += Time.deltaTime * 12f;
            var isRed = Mathf.Sin(strobeTimer) > 0f;
            if (redLedMat != null)
            {
                redLedMat.SetColor("_EmissionColor", isRed ? Color.red * 5.5f : Color.black);
            }
            if (blueLedMat != null)
            {
                blueLedMat.SetColor("_EmissionColor", !isRed ? new Color(0.1f, 0.5f, 1f) * 5.5f : Color.black);
            }

            if (targetPlayer == null) return;

            // 2. Pursuit AI Navigation
            var distToPlayer = targetPlayer.RoadDistance - RoadDistance;
            var maxSpeed = 135f + unitHeatLevel * 14f;

            // Match speed and chase player down
            if (distToPlayer > 8f) // Behind player: accelerate
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, maxSpeed, Time.deltaTime * 32f);
            }
            else if (distToPlayer < -12f) // Ahead of player: slow down to block
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, targetPlayer.SpeedKph - 15f, Time.deltaTime * 40f);
            }
            else // Alongside: match speed and execute aggressive PIT maneuver
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, targetPlayer.SpeedKph, Time.deltaTime * 24f);
                var pitDir = Mathf.Sign(targetPlayer.LateralOffset - LateralOffset);
                LateralOffset = Mathf.MoveTowards(LateralOffset, targetPlayer.LateralOffset + pitDir * 0.4f, Time.deltaTime * 5.5f);
            }

            var forwardTravel = SpeedKph / 3.6f * Time.deltaTime;
            RoadDistance = RoadPath.Wrap(RoadDistance + forwardTravel);

            var halfWidth = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset, -halfWidth, halfWidth);

            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.4f);
            transform.rotation = RoadPath.Rotation(RoadDistance);

            // 3. Check for collision with player car
            var playerPos = targetPlayer.transform.position;
            var copPos = transform.position;
            var dist = Vector3.Distance(playerPos, copPos);

            if (dist < 3.2f)
            {
                OnCollideWithPlayer();
            }
        }

        private void OnCollideWithPlayer()
        {
            if (isWrecked) return;

            var playerSpeed = targetPlayer != null ? targetPlayer.SpeedKph : 0f;
            var isBoosting = RoadRageBoostDirector.Instance != null && RoadRageBoostDirector.Instance.IsBoosting;

            if (playerSpeed >= 55f || isBoosting) // Takedown on the cop!
            {
                WreckCop();
            }
            else
            {
                // Elastic bounce-back so cop never clips or drives sideways inside player
                var pushAwayDir = Mathf.Sign(LateralOffset - targetPlayer.LateralOffset);
                if (Mathf.Abs(pushAwayDir) < 0.1f) pushAwayDir = 1f;
                LateralOffset += pushAwayDir * 2.2f;
                SpeedKph = Mathf.Max(30f, SpeedKph - 15f);

                GameState.ApplyDamage(6f);
                GameState.Show("⚠️ POLICE RAMMED YOU!");
                if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.6f);
            }
        }

        public void WreckCop()
        {
            if (isWrecked) return;
            isWrecked = true;

            wreckSlideDir = targetPlayer != null ? Mathf.Sign(LateralOffset - targetPlayer.LateralOffset) : (Random.value > 0.5f ? 1f : -1f);
            if (Mathf.Abs(wreckSlideDir) < 0.1f) wreckSlideDir = 1f;
            targetWreckYaw = wreckSlideDir * Random.Range(70f, 130f);
            wreckRoll = wreckSlideDir * 4f;

            if (RoadRageTakedownDirector.Instance != null)
            {
                var contactPoint = transform.position + Vector3.up * 0.6f;
                RoadRageTakedownDirector.Instance.TriggerTakedown(transform, contactPoint, Vector3.up, SpeedKph);
            }

            if (RoadRagePolicePursuitDirector.Instance != null)
            {
                RoadRagePolicePursuitDirector.Instance.NotifyPoliceDestroyed(this);
            }

            Destroy(gameObject, 4.5f);
        }
    }
}
