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
            sirenSource.spatialBlend = 0.2f;
            sirenSource.volume = 0.45f;
            sirenSource.pitch = 1.0f;
            sirenSource.clip = CreateProceduralSirenClip();
        }

        private static AudioClip CreateProceduralSirenClip()
        {
            var sampleRate = 44100;
            var duration = 2.4f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];

            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                // Dual-tone wailing siren (650Hz to 950Hz oscillation)
                var freq = Mathf.Lerp(650f, 980f, (Mathf.Sin(t * Mathf.PI * 1.8f) + 1f) * 0.5f);
                var phase = t * freq * 2f * Mathf.PI;
                data[i] = Mathf.Sin(phase) * 0.28f + (Mathf.PingPong(phase, 1f) - 0.5f) * 0.12f;
            }

            var clip = AudioClip.Create("ProceduralPoliceSiren", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
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
            // Spawn 2 barricade vehicles with red/blue emergency flares
            for (var i = -1; i <= 1; i += 2)
            {
                var lane = i * (halfWidth * 0.45f);
                var barObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barObj.name = "Barricade Cruiser";
                barObj.transform.SetParent(root.transform, false);
                barObj.transform.localPosition = new Vector3(lane, 0.6f, 0f);
                barObj.transform.localScale = new Vector3(2.2f, 1.3f, 4.4f);

                var r = barObj.GetComponent<Renderer>();
                r.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.08f, 0.12f, 0.18f)
                };

                // Flashing red/blue strobe light
                var lightGo = new GameObject("Emergency Strobe");
                lightGo.transform.SetParent(barObj.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 14f;
                light.intensity = 4.5f;
                light.color = i > 0 ? Color.red : Color.blue;

                var rb = barObj.AddComponent<Rigidbody>();
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

        private void BuildPoliceMesh()
        {
            // Police body box or mesh
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            body.transform.localScale = new Vector3(2.0f, 1.25f, 4.4f);

            var r = body.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.05f, 0.08f, 0.14f) // Police Dark Navy
            };
            r.material = mat;

            // White doors accent
            var doors = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doors.transform.SetParent(body.transform, false);
            doors.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            doors.transform.localScale = new Vector3(1.02f, 0.75f, 0.55f);
            var dMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = Color.white
            };
            doors.GetComponent<Renderer>().material = dMat;
            Destroy(doors.GetComponent<Collider>());
        }

        private void BuildLightbars()
        {
            var lightbar = new GameObject("Lightbar");
            lightbar.transform.SetParent(transform, false);
            lightbar.transform.localPosition = new Vector3(0f, 1.35f, 0f);

            var redGo = new GameObject("Red Light");
            redGo.transform.SetParent(lightbar.transform, false);
            redGo.transform.localPosition = new Vector3(-0.45f, 0f, 0f);
            redStrobe = redGo.AddComponent<Light>();
            redStrobe.type = LightType.Point;
            redStrobe.range = 10f;
            redStrobe.intensity = 5f;
            redStrobe.color = Color.red;

            var blueGo = new GameObject("Blue Light");
            blueGo.transform.SetParent(lightbar.transform, false);
            blueGo.transform.localPosition = new Vector3(0.45f, 0f, 0f);
            blueStrobe = blueGo.AddComponent<Light>();
            blueStrobe.type = LightType.Point;
            blueStrobe.range = 10f;
            blueStrobe.intensity = 5f;
            blueStrobe.color = Color.blue;
        }

        private void Update()
        {
            if (isWrecked) return;

            // 1. Alternate Emergency Strobes
            strobeTimer += Time.deltaTime * 10f;
            var isRed = Mathf.Sin(strobeTimer) > 0f;
            if (redStrobe != null) redStrobe.enabled = isRed;
            if (blueStrobe != null) blueStrobe.enabled = !isRed;

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

            // Check if player rammed the cop at high speed or if cop side-swiped
            var playerSpeed = targetPlayer != null ? targetPlayer.SpeedKph : 0f;
            if (playerSpeed >= 80f) // Takedown on the cop!
            {
                WreckCop();
            }
            else
            {
                // Cop rams player and inflicts damage
                GameState.ApplyDamage(8f);
                GameState.Show("⚠️ POLICE RAMMED YOU!");
            }
        }

        public void WreckCop()
        {
            if (isWrecked) return;
            isWrecked = true;

            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 1600f;
            rb.AddForce((Vector3.up * 14f + Random.insideUnitSphere * 12f) * rb.mass, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 35000f, ForceMode.Impulse);

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
