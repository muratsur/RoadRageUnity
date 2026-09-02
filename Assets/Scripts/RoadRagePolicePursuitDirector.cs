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
        public IReadOnlyList<PoliceVehicleController> ActivePolice => activePolice;
        private readonly List<GameObject> activeRoadblocks = new();

        /// Bounty earned so far in this pursuit. Paid out on escape, lost on a bust -
        /// which is what makes staying in a pursuit a decision rather than an accident.
        public float PursuitBounty { get; private set; }
        /// Seconds the player has been clear of every cruiser, and seconds they have
        /// been pinned. The two ends of the pursuit.
        private float evadeTimer;
        private float bustTimer;
        private float pursuitSeconds;
        /// Units sent this pursuit. With an empty list the nearest-cruiser distance is
        /// infinite, so the evade timer ran before anything had been dispatched and the
        /// pursuit could end before a single cruiser existed - sirens, then nothing.
        private int unitsDispatched;

        /// 0-1 towards shaking them, and towards being taken. Surfaced so the HUD can
        /// show the player which way a pursuit is going; without that both endings
        /// arrive unannounced and a pursuit reads as noise.
        public float EvadeProgress => HeatLevel > 0 && unitsDispatched > 0
            ? Mathf.Clamp01(evadeTimer / EvadeSecondsFor(HeatLevel)) : 0f;
        public float BustProgress => Mathf.Clamp01(bustTimer / BustSeconds);

        /// No cruiser within this and the player is running clear.
        private const float EvadeRadius = 135f;
        /// Pinned means slow with a cruiser in contact range.
        private const float BustSpeedKph = 38f;
        private const float BustRadius = 14f;
        private const float BustSeconds = 3.5f;
        /// Cooldown lengthens with heat, so shaking five stars is real work and shaking
        /// one is not. Two stars is a few seconds of clear road; five is most of a minute.
        private static float EvadeSecondsFor(int heat) => 5f + heat * 4.5f;

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
            HeatProgress += amount;
            if (HeatProgress >= 1f && HeatLevel < 5)
            {
                HeatLevel++;
                HeatProgress = 0f;
                GameState.Show($"🚨 HEAT LEVEL INCREASED: LEVEL {HeatLevel}!");
                if (RoadRageAudioBridge.Instance != null)
                {
                    RoadRageAudioBridge.Instance.PlayTakedownStinger();
                }
            }
        }

        private void Update()
        {
            if (playerController == null) return;

            // 1. Audio Siren Dynamic Volume Modulation
            if (activePolice.Count > 0)
            {
                if (sirenSource != null && !sirenSource.isPlaying)
                {
                    sirenSource.Play();
                }

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
                // Heat 1 sent one cruiser and capped there, which is trivially outrun -
                // the pursuit was over before it registered. Escalation has to be felt.
                var maxUnits = Mathf.Min(1 + HeatLevel, 5);
                var spawnInterval = Mathf.Max(2.5f, 8f - HeatLevel * 1.2f);
                // The first unit of a pursuit does not wait out the interval.
                if (unitsDispatched == 0) spawnTimer = spawnInterval;

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

            // 4. The pursuit's two endings.
            //
            // Until now heat only ever went up, cruisers never left, and there was no
            // busted state - so there was nothing the player could actually do about the
            // police, win or lose. A pursuit needs an exit at both ends to be a
            // mechanic: outrun them and get paid, or get pinned and lose the purse.
            if (HeatLevel > 0)
            {
                pursuitSeconds += Time.deltaTime;
                PursuitBounty += Time.deltaTime * (35f + HeatLevel * 55f);
                // Staying in a pursuit escalates it. Takedowns were the only source of
                // heat, so a chase never grew on its own and every pursuit stayed at the
                // star it started on - the escalation the whole system is built around
                // simply never happened.
                AddHeat(Time.deltaTime / 22f);
                UpdatePursuitOutcome();
            }

            // 5. Clean up stale roadblocks
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

        private void UpdatePursuitOutcome()
        {
            var nearest = float.MaxValue;
            var pinning = 0;
            for (var i = activePolice.Count - 1; i >= 0; i--)
            {
                var cop = activePolice[i];
                if (cop == null) { activePolice.RemoveAt(i); continue; }
                var gap = Mathf.Abs(cop.RoadDistance - playerController.RoadDistance);
                nearest = Mathf.Min(nearest, gap);
                if (gap < BustRadius) pinning++;
            }

            // Escape: clear of every unit for long enough that they have lost the trail.
            // Only once something has actually been sent.
            evadeTimer = unitsDispatched > 0 && nearest > EvadeRadius ? evadeTimer + Time.deltaTime : 0f;
            if (evadeTimer >= EvadeSecondsFor(HeatLevel))
            {
                EndPursuit(escaped: true);
                return;
            }

            // Bust: pinned slow with a unit on you. Being slow is only fatal while they
            // are alongside, so braking to dodge traffic is not punished by itself.
            var pinned = pinning > 0 && playerController.SpeedKph < BustSpeedKph;
            bustTimer = pinned ? bustTimer + Time.deltaTime : Mathf.MoveTowards(bustTimer, 0f, Time.deltaTime * 2f);
            if (bustTimer >= BustSeconds) EndPursuit(escaped: false);
        }

        private void EndPursuit(bool escaped)
        {
            // A long pursuit pays more than the sum of its seconds. Surviving five stars
            // for a minute should feel different from shaking one star immediately, and
            // a flat rate makes those the same thing.
            var endurance = 1f + Mathf.Clamp01(pursuitSeconds / 90f) * 0.75f;
            var payout = Mathf.RoundToInt(PursuitBounty * endurance);
            if (escaped)
            {
                GameState.Award(payout, $"🚔 LOST THEM  +{payout}");
                if (RoadRageBoostDirector.Instance != null)
                    RoadRageBoostDirector.Instance.AddBoost(60f, "CLEAN GETAWAY");
            }
            else
            {
                GameState.Show($"🚨 BUSTED  -{payout}");
                GameState.Score = Mathf.Max(0, GameState.Score - payout);
                GameState.ApplyDamage(22f);
                GameState.Combo = 0;
            }

            for (var i = activePolice.Count - 1; i >= 0; i--)
                if (activePolice[i] != null) Destroy(activePolice[i].gameObject);
            activePolice.Clear();

            HeatLevel = 0;
            HeatProgress = 0f;
            PursuitBounty = 0f;
            unitsDispatched = 0;
            pursuitSeconds = 0f;
            evadeTimer = 0f;
            bustTimer = 0f;
            spawnTimer = 0f;
        }

        private void SpawnPoliceUnit()
        {
            var slot = activePolice.Count;
            var spawnBehind = Random.value > 0.35f;
            var distOffset = spawnBehind ? -45f - slot * 8f : 65f + slot * 12f;
            var spawnDist = RoadPath.Wrap(playerController.RoadDistance + distOffset);
            var laneSign = (slot % 2 == 0) ? -1f : 1f;
            var halfW = RoadPath.HalfWidthAt(spawnDist);
            var spawnLane = Mathf.Clamp(playerController.LateralOffset + laneSign * (3.4f + slot * 0.6f), -halfW + 1.8f, halfW - 1.8f);

            var copObj = new GameObject($"Police Unit [{HeatLevel} Star - Slot {slot}]");
            copObj.transform.position = RoadPath.Point(spawnDist, spawnLane, 0.4f);
            copObj.transform.rotation = RoadPath.Rotation(spawnDist);

            var cop = copObj.AddComponent<PoliceVehicleController>();
            cop.Initialize(playerController, HeatLevel, spawnDist, spawnLane, slot);
            activePolice.Add(cop);
            unitsDispatched++;
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
    public sealed class PoliceVehicleController : MonoBehaviour, IRoadVehicle
    {
        public float RoadDistance { get; internal set; }
        public float LateralOffset { get; internal set; }
        public float SpeedKph { get; internal set; } = 95f;
        public int SlotIndex { get; private set; }

        // --- IRoadVehicle -------------------------------------------------------
        // Cruisers used to resolve against each other and against the player, but never
        // against traffic - so a pursuit unit drove clean through the cars it was
        // chasing. Joining the shared registry is the fix; the duplicated resolution
        // below it is gone.
        public float ContactDistance => RoadDistance;
        public float ContactLateral => LateralOffset;
        public float ContactHalfLength => hullHalfLength;
        public float ContactHalfWidth => hullHalfWidth;
        public float ContactHeight => 0f;
        /// A little heavier than civilian traffic: an interceptor shoulders a hatchback
        /// out of the way rather than being deflected off the player's tail by it.
        public float ContactMass => hullHalfLength * hullHalfWidth * 1.35f;
        public bool ContactActive => isActiveAndEnabled && !isWrecked;

        public void ApplyContactPush(float alongRoad, float acrossRoad)
        {
            RoadDistance += alongRoad;
            LateralOffset += acrossRoad;
        }

        /// Measured off the spawned mesh, like every other vehicle. The 4.8 m / 2.4 m
        /// constants this replaces were a guess that matched no car in the game.
        private float hullHalfLength = 2.4f;
        private float hullHalfWidth = 1.2f;
        /// Slightly wider than the separation skin, so contact registers on the frame
        /// the hulls meet rather than never.
        private const float ContactMargin = 0.35f;

        private void OnEnable() => VehicleContacts.Register(this);
        private void OnDisable() => VehicleContacts.Unregister(this);

        private void LateUpdate()
        {
            VehicleContacts.ResolveOncePerFrame();
            if (isWrecked) return;
            var halfWidth = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset, -halfWidth, halfWidth);
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.4f);
        }
        // ------------------------------------------------------------------------

        private ArcadeCarController targetPlayer;
        private int unitHeatLevel;
        private Light redStrobe;
        private Light blueStrobe;
        private float strobeTimer;
        private bool isWrecked;

        public void Initialize(ArcadeCarController player, int heat, float startDist, float startLane, int slot = 0)
        {
            targetPlayer = player;
            unitHeatLevel = heat;
            RoadDistance = startDist;
            LateralOffset = startLane;
            SlotIndex = slot;
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
                // Hull follows the mesh that was just normalised, so the cruiser
                // collides as the car you can see rather than as a fixed guess.
                hullHalfLength = 4.8f * 0.5f;
                hullHalfWidth = Mathf.Max(0.8f, hullHalfLength * 0.42f);
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

            // 2. Tactical Pursuit AI Navigation based on Formation Slot
            var maxSpeed = 138f + unitHeatLevel * 14f;
            float targetLane;
            float targetDistDelta;

            switch (SlotIndex % 3)
            {
                case 0: // Left Flank Interceptor
                    targetLane = targetPlayer.LateralOffset - 3.4f;
                    targetDistDelta = 0.5f;
                    break;
                case 1: // Right Flank Interceptor
                    targetLane = targetPlayer.LateralOffset + 3.4f;
                    targetDistDelta = 0.5f;
                    break;
                default: // Rear Pursuer / Rammer
                    targetLane = targetPlayer.LateralOffset;
                    targetDistDelta = -7.5f;
                    break;
            }

            var distToTarget = (targetPlayer.RoadDistance + targetDistDelta) - RoadDistance;
            if (distToTarget > 5f) // Behind target position: accelerate
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, maxSpeed, Time.deltaTime * 36f);
            }
            else if (distToTarget < -7f) // Ahead of target position: slow down
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, targetPlayer.SpeedKph - 18f, Time.deltaTime * 42f);
            }
            else // In position: match player speed and execute tactical pressure
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, targetPlayer.SpeedKph + (distToTarget * 2.5f), Time.deltaTime * 28f);
            }

            // Steer smoothly towards assigned tactical formation lane
            LateralOffset = Mathf.MoveTowards(LateralOffset, targetLane, Time.deltaTime * 6.5f);

            var forwardTravel = SpeedKph / 3.6f * Time.deltaTime;
            RoadDistance = RoadPath.Wrap(RoadDistance + forwardTravel);

            var halfWidth = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset, -halfWidth, halfWidth);

            // Anti-penetration - against other cruisers, the player AND traffic - is
            // handled by the shared vehicle pass in LateUpdate, after everything has
            // moved. The two hand-rolled resolvers that used to live here only knew
            // about police and the player, which is why cruisers drove through traffic.

            transform.rotation = RoadPath.Rotation(RoadDistance);

            // 5. Check for collision with player car.
            //
            // Measured in road space rather than from the two transforms. Placement
            // moved to LateUpdate when the cruiser joined the shared contact pass, so
            // by the time this runs transform.position still holds last frame's value -
            // at pursuit speed that is most of a metre of error on a 3.2 m test.
            // RoadDistance and LateralOffset are current on both vehicles here.
            // Tested against the hulls, not a fixed 3.2 m radius. The shared contact
            // pass holds these two 4.68 m apart longitudinally - the sum of their
            // half-lengths plus the skin - so a 3.2 m test could never once be true and
            // the cruiser simply drove alongside forever. That is the "police comes and
            // drives by me": it was physically unable to reach.
            var alongRoad = Mathf.Abs(RoadDistance - targetPlayer.RoadDistance);
            var acrossRoad = Mathf.Abs(LateralOffset - targetPlayer.LateralOffset);
            if (alongRoad < hullHalfLength + targetPlayer.HalfLength + ContactMargin &&
                acrossRoad < hullHalfWidth + targetPlayer.HalfWidth + ContactMargin)
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
