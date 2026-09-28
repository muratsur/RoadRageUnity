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
            // The recorded siren from Road Rage 3D (audio/siren.mp3), made to loop
            // without a click; the pack sirens are the fallback.
            sirenSource.clip = Resources.Load<AudioClip>("Audio/SirenReal/siren_real")
                ?? Resources.Load<AudioClip>("Audio/SFX/Horns/Sirens/siren_1")
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
                var maxUnits = Mathf.Min(1 + HeatLevel, RoadRageBootstrap.RichDetailBudget ? 5 : 3);
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
                    // Every 14 s stacked roadblocks up the road faster than the player
                    // cleared them - the road knotted into barricades and wrecks.
                    if (roadblockTimer >= 28f && activeRoadblocks.Count == 0)
                    {
                        roadblockTimer = 0f;
                        SpawnRoadblock();
                    }
                }

                // Spike strips from three stars: the tool that answers simply holding
                // the throttle down, which is otherwise the solution to every pursuit.
                if (HeatLevel >= 3)
                {
                    spikeTimer += Time.deltaTime;
                    if (spikeTimer >= 11f)
                    {
                        spikeTimer = 0f;
                        SpawnSpikeStrip();
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

            UpdateSpikeStrips();

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

            // The units drop back and leave once out of sight. Deleting them outright
            // made cruisers vanish from beside the player the moment a bust landed.
            for (var i = activePolice.Count - 1; i >= 0; i--)
                if (activePolice[i] != null) activePolice[i].StandDown();
            activePolice.Clear();

            for (var i = activeSpikes.Count - 1; i >= 0; i--)
                if (activeSpikes[i].Root != null) Destroy(activeSpikes[i].Root);
            activeSpikes.Clear();
            spikeTimer = 0f;

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
            // Always from behind, out of the chase camera's view: a cruiser spawned 65 m
            // ahead appeared out of thin air in the middle of the road.
            var distOffset = -45f - slot * 8f;
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

        /// A live spike strip laid across part of the carriageway.
        ///
        /// Promised in this class's own summary since it was written and never built.
        /// It is the one pursuit tool that punishes the obvious answer to a chase -
        /// holding the throttle down in a straight line - so without it every pursuit
        /// has the same solution.
        private struct SpikeStrip
        {
            public float Distance;
            public float Lane;
            public float HalfWidth;
            public GameObject Root;
            public bool Spent;
        }

        private readonly List<SpikeStrip> activeSpikes = new();
        private float spikeTimer;

        private void SpawnSpikeStrip()
        {
            var targetDist = playerController.RoadDistance + 190f;
            var halfWidth = RoadPath.HalfWidthAt(targetDist);
            // Deliberately never the full carriageway. A hazard with no way past it is
            // not a decision, it is a toll - the player has to be able to read the gap
            // and take it.
            var stripHalf = halfWidth * 0.42f;
            var side = Random.value > 0.5f ? 1f : -1f;
            var lane = side * (halfWidth - stripHalf);

            var root = new GameObject("Police Spike Strip");
            root.transform.position = RoadPath.Point(targetDist, lane, 0.06f);
            root.transform.rotation = RoadPath.Rotation(targetDist);

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Spikes";
            strip.transform.SetParent(root.transform, false);
            strip.transform.localScale = new Vector3(stripHalf * 2f, 0.12f, 1.1f);
            var collider = strip.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = strip.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                { name = "Spike Strip", color = new Color(0.85f, 0.72f, 0.12f) };
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.8f);
                renderer.sharedMaterial = mat;
            }

            activeSpikes.Add(new SpikeStrip
            {
                Distance = targetDist, Lane = lane, HalfWidth = stripHalf, Root = root, Spent = false
            });
            GameState.Show(side > 0f ? "⚠️ SPIKES RIGHT - GO LEFT!" : "⚠️ SPIKES LEFT - GO RIGHT!");
        }

        private void UpdateSpikeStrips()
        {
            for (var i = activeSpikes.Count - 1; i >= 0; i--)
            {
                var spike = activeSpikes[i];
                if (spike.Root == null) { activeSpikes.RemoveAt(i); continue; }

                if (playerController.RoadDistance - spike.Distance > 70f)
                {
                    Destroy(spike.Root);
                    activeSpikes.RemoveAt(i);
                    continue;
                }

                if (spike.Spent) continue;
                if (Mathf.Abs(playerController.RoadDistance - spike.Distance) > 2.2f) continue;
                if (Mathf.Abs(playerController.LateralOffset - spike.Lane) > spike.HalfWidth) continue;

                spike.Spent = true;
                activeSpikes[i] = spike;

                // Costly but never a run-ender on its own: it takes the speed that was
                // keeping you ahead, which is what makes the next few seconds matter.
                playerController.SpeedKph *= 0.45f;
                GameState.ApplyDamage(12f);
                GameState.Show("💥 SPIKED! TYRES SHREDDED");
                if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.7f);
                if (RoadRageImpactShakeDirector.Instance != null)
                    RoadRageImpactShakeDirector.Instance.TriggerMediumShake(0.8f);
            }
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

                // Not `GetComponent() ?? AddComponent()`: a missing component is Unity's
                // fake null, which `??` does not see, so no body was ever added and
                // setting its mass threw MissingComponentException. Kinematic: a parked
                // barricade, not a loose car for the physics to shove about.
                var rb = barObj.GetComponent<Rigidbody>();
                if (rb == null) rb = barObj.AddComponent<Rigidbody>();
                rb.mass = 2800f;
                rb.isKinematic = true;
            }

            activeRoadblocks.Add(root);
            GameState.Show("⚠️ POLICE ROADBLOCK AHEAD! FIND THE GAP!");
        }

        public void NotifyPoliceDestroyed(PoliceVehicleController cop)
        {
            activePolice.Remove(cop);
            var value = 750 + HeatLevel * 250;
            GameState.Award(value, "🚨 COP TAKEDOWN!");
            // Wrecking a cruiser should pay into the pursuit it belongs to and feed the
            // boost that made it possible - that loop of risk paying for more speed is
            // the whole reason to take one on rather than simply outrun it.
            PursuitBounty += value * 0.5f;
            if (RoadRageBoostDirector.Instance != null)
                RoadRageBoostDirector.Instance.AddBoost(45f, "COP TAKEDOWN");
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
        /// Projected through the wreck's spin: a cruiser slewed across the road
        /// sweeps its length sideways. Reported unrotated, a spun wreck's body stuck
        /// out of its hull and was drawn through every car beside it.
        public float ContactHalfLength
        {
            get
            {
                var yaw = wreckYaw * Mathf.Deg2Rad;
                return Mathf.Abs(hullHalfLength * Mathf.Cos(yaw)) + Mathf.Abs(hullHalfWidth * Mathf.Sin(yaw));
            }
        }

        public float ContactHalfWidth
        {
            get
            {
                var yaw = wreckYaw * Mathf.Deg2Rad;
                return Mathf.Abs(hullHalfLength * Mathf.Sin(yaw)) + Mathf.Abs(hullHalfWidth * Mathf.Cos(yaw));
            }
        }

        public float ContactHeight => 0f;
        /// A little heavier than civilian traffic: an interceptor shoulders a hatchback
        /// out of the way rather than being deflected off the player's tail by it.
        public float ContactMass => hullHalfLength * hullHalfWidth * 1.35f;
        /// Wrecks stay solid: a crashed cruiser switched out of the contact pass was a
        /// car-shaped hole the player and traffic drove straight through.
        public bool ContactActive => isActiveAndEnabled;

        public void ApplyContactPush(float alongRoad, float acrossRoad)
        {
            RoadDistance += alongRoad;
            LateralOffset += acrossRoad;
            if (!isWrecked) return;
            // A sliding wreck that meets another car loses the motion that drove it
            // in, rather than pressing on through it frame after frame.
            if (Mathf.Abs(acrossRoad) > 0.005f && Mathf.Sign(acrossRoad) != Mathf.Sign(wreckLateralSpeed))
            {
                wreckLateralSpeed *= 0.25f;
                wreckYawRate *= 0.6f;
            }
            if (alongRoad < -0.005f) SpeedKph *= 0.85f;
        }

        /// Measured off the spawned mesh, like every other vehicle. The 4.8 m / 2.4 m
        /// constants this replaces were a guess that matched no car in the game.
        private float hullHalfLength = 2.4f;
        private float hullHalfWidth = 1.2f;
        /// Slightly wider than the separation skin, so contact registers on the frame
        /// the hulls meet rather than never.
        private const float ContactMargin = 0.35f;

        /// Every cruiser on the road, live, standing down or wrecked. Traffic brakes
        /// for the wrecks: it only knew its own cars, so a crashed cruiser left across
        /// a lane was driven straight into and shoved through by the contact pass.
        public static readonly List<PoliceVehicleController> OnRoad = new();
        public bool IsWrecked => isWrecked;

        private void OnEnable()
        {
            VehicleContacts.Register(this);
            if (!OnRoad.Contains(this)) OnRoad.Add(this);
        }

        private void OnDisable()
        {
            VehicleContacts.Unregister(this);
            OnRoad.Remove(this);
        }

        private bool standingDown;
        private float standDownAge;

        /// The pursuit is over: lights off, ease off the gas and drop back, then leave
        /// once well behind or far off.
        public void StandDown()
        {
            if (isWrecked || standingDown) return;
            standingDown = true;
            if (redLedMat != null) redLedMat.SetColor("_EmissionColor", Color.black);
            if (blueLedMat != null) blueLedMat.SetColor("_EmissionColor", Color.black);
            if (redStrobe != null) redStrobe.intensity = 0f;
            if (blueStrobe != null) blueStrobe.intensity = 0f;
        }

        /// Nothing may move the cruiser between the contact pass and placing it. This
        /// used to re-clamp the lateral offset here, after the pass had pushed the
        /// cruiser clear; on a narrow road the clamp put it straight back inside the car
        /// it had just been separated from, so it was drawn clipping every frame. Update
        /// already clamps before the pass runs.
        private void LateUpdate()
        {
            VehicleContacts.ResolveOncePerFrame();
            if (!isWrecked) CheckTrafficImpact();
            // The contact pass can shove a cruiser sideways; the road edge still holds.
            LateralOffset = ClampToRoadEdge(LateralOffset);
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.4f);
        }
        // ------------------------------------------------------------------------

        /// A cruiser that piles into traffic wrecks, like anything else on this road.
        ///
        /// Until it joined the shared contact registry a cruiser could only be stopped
        /// by the player hitting it, so the traffic it was weaving through at 140 km/h
        /// was scenery to it. Making it mortal to the world is what turns a pursuit into
        /// something the player can fight with the road rather than only outrun: brake
        /// hard, let them commit to a gap that closes, and the highway does the work.
        private void CheckTrafficImpact()
        {
            var cars = TrafficCarController.All;
            for (var i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null || car.IsAirborne) continue;

                var alongRoad = Mathf.Abs(car.RoadDistance - RoadDistance);
                if (alongRoad > hullHalfLength + car.LongitudinalExtent + ContactMargin) continue;
                var acrossRoad = Mathf.Abs(car.LaneOffset - LateralOffset);
                if (acrossRoad > hullHalfWidth + car.LateralExtent + ContactMargin) continue;

                // Closing speed decides it. Nudging a car at matched speed is a scrape;
                // arriving 40 km/h faster than it is a wreck.
                var closing = Mathf.Abs(SpeedKph - car.SpeedKph);
                if (car.IsWreck || closing > 40f)
                {
                    GameState.Show("🚨 CRUISER WIPED OUT!");
                    if (!car.IsWreck) car.Crash(acrossRoad, SpeedKph);
                    WreckCop();
                    return;
                }
                SpeedKph = Mathf.Min(SpeedKph, car.SpeedKph + 10f);
            }
        }

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

        /// On the B500 the cruisers are German: blue-silver Polizei cars with blue
        /// lights only, no red. Other biomes keep the American black-and-red.
        private static bool German => RoadPath.Route != null;

        private static readonly Color PoliceBlueA = new(0.10f, 0.45f, 1f);
        private static readonly Color PoliceBlueB = new(0.25f, 0.55f, 1f);

        private static void NormalizeVehicleVisual(GameObject visual, float targetLength)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontalLength > 0.01f) visual.transform.localScale *= targetLength / horizontalLength;
        }

        /// Height of the roof above the cruiser's origin: where the lightbar sits.
        private float roofHeight = 1.45f;

        private void BuildPoliceMesh()
        {
            if (BuildPackCruiser()) return;
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
                    // Dark interceptor navy; silver on the B500, where the blue is the band.
                    color = German ? new Color(0.70f, 0.72f, 0.76f) : new Color(0.04f, 0.06f, 0.10f)
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
                // Centre the model on the cruiser. The pack's pivots are not at the middle
                // of the car, and unlike traffic (whose NormalizeVehicleVisual recentres)
                // this was only scaled - so the visible cruiser sat a metre or two ahead of
                // or behind the hull the contact pass separates, and was drawn inside the
                // car in front however correctly the hulls were kept apart.
                var modelBounds = default(Bounds);
                var found = false;
                foreach (var r in vehicleInstance.GetComponentsInChildren<Renderer>())
                {
                    if (!found) { modelBounds = r.bounds; found = true; }
                    else modelBounds.Encapsulate(r.bounds);
                }
                if (found)
                {
                    var centre = transform.InverseTransformPoint(modelBounds.center);
                    vehicleInstance.transform.localPosition -= new Vector3(centre.x, 0f, centre.z);
                }
                if (German) PolizeiLivery.Apply(transform, vehicleInstance);
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

        private static bool packCruiserReported;
        private static readonly Dictionary<Material, Material> urpCopies = new();

        /// The cruiser body from Realistic Mobile Car #26, where Road Rage > Link Police
        /// Car Pack has linked it. The car is built under an inactive holder so the
        /// pack's own driving scripts, rigidbody and wheel colliders never wake up;
        /// they are stripped and the car is only a body on the pursuit's own motion.
        private bool BuildPackCruiser()
        {
            var prefab = PoliceCarPack.LinkedCar;
            if (prefab == null) return false;

            // Built and measured with the cruiser square to the world: it is spawned
            // already turned to the road, and world-space bounds measured on a curve
            // came out skewed (a 1.9 m car measured 3.6 m wide).
            var heading = transform.rotation;
            transform.rotation = Quaternion.identity;
            var holder = new GameObject("Police Interceptor Model");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var car = Instantiate(prefab, holder.transform);
            car.transform.localPosition = Vector3.zero;
            car.transform.localRotation = Quaternion.identity;
            foreach (var joint in car.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint);
            foreach (var script in car.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
            foreach (var collider in car.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var body in car.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
            foreach (var source in car.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(source);
            foreach (var cam in car.GetComponentsInChildren<Camera>(true)) DestroyImmediate(cam.gameObject);
            foreach (var l in car.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
            // Nothing left that can run; renderers only report bounds while active.
            holder.SetActive(true);

            var renderers = car.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Destroy(holder);
                transform.rotation = heading;
                return false;
            }
            var painted = new Dictionary<Material, Material>();
            var paint = German ? new Color(0.72f, 0.74f, 0.78f) : new Color(0.05f, 0.07f, 0.11f);
            foreach (var r in renderers)
            {
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = ToUrp(materials[i]);
                    // The body paint becomes the force's colour: silver on the B500
                    // (the blue is the band), dark navy elsewhere.
                    var matName = materials[i] != null ? materials[i].name.ToLowerInvariant() : "";
                    if (matName.Contains("body") || matName.Contains("paint"))
                    {
                        // One painted copy per source material, shared by every part.
                        if (!painted.TryGetValue(materials[i], out var copy))
                        {
                            copy = new Material(materials[i]) { name = materials[i].name + " (Police)" };
                            copy.SetColor("_BaseColor", paint);
                            painted[materials[i]] = copy;
                        }
                        materials[i] = copy;
                    }
                }
                r.sharedMaterials = materials;
            }

            // Measured on the bodywork only: the pack's light glows and flares stand
            // well out from the car (it measured 3.6 m wide with them), and the hull
            // the contact pass uses has to be the car you can see.
            var bodywork = System.Array.FindAll(renderers, part => !IsLightPart(part));
            if (bodywork.Length > 0) renderers = bodywork;
            // Long side along the cruiser's z, taken from the body mesh itself: the
            // pack's car need not sit square inside its prefab, and a car turned a
            // few degrees drove crabwise down the road.
            AlignLongAxis(holder.transform, car.transform, renderers);
            var b = LocalBounds(holder.transform, renderers);
            var scale = 4.8f / Mathf.Max(0.01f, b.size.z);
            car.transform.localScale *= scale;
            b = LocalBounds(holder.transform, renderers);
            // Centred on the hull, wheels on the road.
            car.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = LocalBounds(holder.transform, renderers);

            roofHeight = b.max.y + 0.02f;
            hullHalfLength = b.size.z * 0.5f;
            hullHalfWidth = Mathf.Max(0.8f, b.size.x * 0.5f);
            // The livery measures the car the same way, so it too is laid on square.
            if (German) PolizeiLivery.Apply(transform, holder);
            transform.rotation = heading;

            if (!packCruiserReported)
            {
                packCruiserReported = true;
                var all = new HashSet<string>();
                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials) if (m != null) all.Add(m.name);
                Debug.Log($"RR_POLICE cruiser from '{prefab.name}': {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m " +
                          $"(scaled x{scale:0.00}), roof {roofHeight:0.00} m. Materials: {string.Join(", ", all)}. " +
                          $"Painted: {(painted.Count > 0 ? string.Join(", ", System.Linq.Enumerable.Select(painted.Values, m => m.name)) : "none (no body/paint material)")}");
            }
            return true;
        }

        /// Turns the car so the long axis of its biggest mesh (the body) runs along the
        /// holder's z. Front and back are the pack's own: the turn is the smallest one
        /// that squares the body up.
        private static void AlignLongAxis(Transform holder, Transform car, Renderer[] renderers)
        {
            MeshFilter body = null;
            var biggest = 0f;
            foreach (var r in renderers)
            {
                if (!r.TryGetComponent<MeshFilter>(out var f) || f.sharedMesh == null) continue;
                var size = Vector3.Scale(f.sharedMesh.bounds.size, f.transform.lossyScale);
                var volume = Mathf.Abs(size.x * size.y * size.z);
                if (volume > biggest) { biggest = volume; body = f; }
            }
            if (body == null) return;
            var meshSize = body.sharedMesh.bounds.size;
            // The long horizontal axis of the mesh: whichever of its three is longest
            // once the up axis is set aside.
            var candidates = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var best = Vector3.forward;
            var bestLength = -1f;
            foreach (var axis in candidates)
            {
                var inHolder = holder.InverseTransformDirection(body.transform.TransformDirection(axis));
                if (Mathf.Abs(inHolder.y) > 0.7f) continue;   // that one points up
                var length = Vector3.Scale(meshSize, axis).magnitude;
                if (length > bestLength) { bestLength = length; best = inHolder; }
            }
            best.y = 0f;
            if (best.sqrMagnitude < 1e-4f) return;
            var yaw = Mathf.Atan2(best.x, best.z) * Mathf.Rad2Deg;
            if (yaw > 90f) yaw -= 180f;
            if (yaw < -90f) yaw += 180f;
            car.localRotation = Quaternion.Euler(0f, -yaw, 0f) * car.localRotation;
        }

        private static bool IsLightPart(Renderer r)
        {
            var n = r.name.ToLowerInvariant();
            if (n.Contains("light") || n.Contains("glow") || n.Contains("flare") || n.Contains("shadow")) return true;
            foreach (var m in r.sharedMaterials)
            {
                var mn = m != null ? m.name.ToLowerInvariant() : "";
                if (mn.Contains("light") || mn.Contains("glow") || mn.Contains("flare") || mn.Contains("shadow")) return true;
            }
            return false;
        }

        private static Bounds LocalBounds(Transform frame, Renderer[] renderers)
        {
            var found = false;
            var bounds = default(Bounds);
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var world = r.bounds;
                var c = world.center;
                var e = world.extents;
                for (var i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    var local = frame.InverseTransformPoint(corner);
                    if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return bounds;
        }

        /// A URP Lit copy of a material written for the built-in pipeline (magenta in
        /// URP), carrying over its textures, colour, metal and gloss. Glass stays see-
        /// through. URP materials are returned as they are.
        private static Material ToUrp(Material source)
        {
            if (source == null || source.shader == null) return source;
            var shaderName = source.shader.name;
            if (shaderName.StartsWith("Universal Render Pipeline/") || shaderName.StartsWith("Shader Graphs/"))
                return source;
            if (urpCopies.TryGetValue(source, out var cached)) return cached;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return source;
            var copy = new Material(lit) { name = source.name };
            var tex = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex")
                : source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (tex != null) copy.SetTexture("_BaseMap", tex);
            var colour = source.HasProperty("_Color") ? source.GetColor("_Color")
                : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            copy.SetColor("_BaseColor", colour);
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                copy.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                copy.EnableKeyword("_NORMALMAP");
            }
            if (source.HasProperty("_MetallicGlossMap") && source.GetTexture("_MetallicGlossMap") != null)
            {
                copy.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
                copy.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (source.HasProperty("_Metallic")) copy.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Glossiness")) copy.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (source.IsKeywordEnabled("_EMISSION") && source.HasProperty("_EmissionColor"))
            {
                copy.EnableKeyword("_EMISSION");
                copy.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                if (source.HasProperty("_EmissionMap")) copy.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
            }
            var lower = source.name.ToLowerInvariant();
            if (lower.Contains("glass") || lower.Contains("window") || source.renderQueue >= 3000)
            {
                copy.SetFloat("_Surface", 1f);
                copy.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                copy.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                copy.SetFloat("_ZWrite", 0f);
                copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                copy.renderQueue = 3000;
                if (colour.a > 0.95f) copy.SetColor("_BaseColor", new Color(colour.r, colour.g, colour.b, 0.4f));
                copy.SetFloat("_Smoothness", 0.92f);
            }
            urpCopies[source] = copy;
            return copy;
        }

        private void BuildLightbars()
        {
            var lightbarRoot = new GameObject("Police LED Lightbar");
            lightbarRoot.transform.SetParent(transform, false);
            lightbarRoot.transform.localPosition = new Vector3(0f, roofHeight, -0.1f);

            var barFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barFrame.name = "Lightbar Frame";
            barFrame.transform.SetParent(lightbarRoot.transform, false);
            barFrame.transform.localScale = new Vector3(1.15f, 0.08f, 0.22f);
            barFrame.GetComponent<Renderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.05f, 0.05f, 0.05f) };
            Destroy(barFrame.GetComponent<Collider>());

            var leftColour = German ? PoliceBlueB : Color.red;
            redLedMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            redLedMat.color = leftColour;
            redLedMat.EnableKeyword("_EMISSION");
            redLedMat.SetColor("_EmissionColor", leftColour * 4.5f);

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

            // The two Light fields have been declared since this class was written and
            // never created, so a cruiser cast no light at all - the lightbar was two
            // emissive cubes and nothing else. On a wet night street the flashing red
            // and blue thrown onto the road is most of what a pursuit looks like.
            // Ten realtime point lights across five cruisers is not affordable on a
            // device that already cannot produce a frame. Where there is no budget the
            // lens emission still pulses, which reads at distance; only the cast light
            // is dropped.
            if (RoadRageBootstrap.RichDetailBudget)
            {
                redStrobe = MakeStrobe(lightbarRoot.transform, new Vector3(-0.35f, 0.1f, 0f), leftColour);
                blueStrobe = MakeStrobe(lightbarRoot.transform, new Vector3(0.35f, 0.1f, 0f), new Color(0.15f, 0.45f, 1f));
            }
            if (German) PolizeiLivery.AddRoofSign(lightbarRoot.transform);
        }

        private static Light MakeStrobe(Transform parent, Vector3 localPosition, Color colour)
        {
            var holder = new GameObject("Strobe");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = 18f;
            light.intensity = 0f;
            // No shadows: up to five cruisers carry two of these each, and a shadow-
            // casting strobe apiece is not worth what it costs.
            light.shadows = LightShadows.None;
            return light;
        }

        /// Keeps the hull on the road and shoulder, whatever pushes it. A wreck slid
        /// sideways at 6.5 m/s with nothing stopping it, straight through the guard
        /// rail and into the trees. Measured across the road with the wreck's spin, so
        /// a cruiser slewed side-on stops with its nose at the rail, not its centre.
        private float ClampToRoadEdge(float lateral)
        {
            var yaw = wreckYaw * Mathf.Deg2Rad;
            var across = Mathf.Abs(hullHalfLength * Mathf.Sin(yaw)) + Mathf.Abs(hullHalfWidth * Mathf.Cos(yaw));
            var edge = Mathf.Max(0.5f, RoadPath.HalfWidthAt(RoadDistance) + RoadPath.ShoulderWidth - across);
            return Mathf.Clamp(lateral, -edge, edge);
        }

        private float wreckSlideDir;
        private float wreckYaw;
        private float wreckRoll;
        private float wreckLateralSpeed;
        private float wreckYawRate;
        private float wreckAge;

        /// Sliding tyres on asphalt, roughly 0.75 g: km/h lost per second.
        private const float WreckFriction = 26f;

        private void Update()
        {
            if (isWrecked)
            {
                // A real slide: the momentum of the hit carries it forward and sideways,
                // friction bleeds both off, the spin slows as it stops, and the guard
                // rail stops it dead instead of letting it through.
                var dt = Time.deltaTime;
                wreckAge += dt;
                SpeedKph = Mathf.MoveTowards(SpeedKph, 0f, WreckFriction * dt);
                wreckLateralSpeed = Mathf.MoveTowards(wreckLateralSpeed, 0f, WreckFriction / 3.6f * dt);
                var spinFade = Mathf.Clamp01((SpeedKph + Mathf.Abs(wreckLateralSpeed) * 3.6f) / 40f);
                wreckYawRate = Mathf.MoveTowards(wreckYawRate, 0f, 160f * dt) * Mathf.Lerp(0.9f, 1f, spinFade);
                wreckYaw += wreckYawRate * dt;
                wreckRoll = Mathf.Lerp(wreckRoll, 0f, dt * 2.5f);
                var sliding = LateralOffset + wreckLateralSpeed * dt;
                LateralOffset = ClampToRoadEdge(sliding);
                if (!Mathf.Approximately(sliding, LateralOffset))
                {
                    // Into the rail: a bounce, a scrape, and the spin knocked out of it.
                    wreckLateralSpeed *= -0.2f;
                    wreckYawRate *= 0.5f;
                    SpeedKph *= 0.97f;
                }
                RoadDistance = RoadPath.Wrap(RoadDistance + SpeedKph / 3.6f * dt);
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.4f);
                transform.rotation = RoadPath.Rotation(RoadDistance) * Quaternion.Euler(0f, wreckYaw, wreckRoll);
                // The wreck stays where it came to rest until it is out of sight behind
                // the player, instead of vanishing in front of them.
                var behind = targetPlayer != null ? targetPlayer.RoadDistance - RoadDistance : 0f;
                if (wreckAge > 3f && (behind > 60f || wreckAge > 40f)) Destroy(gameObject);
                return;
            }

            if (standingDown)
            {
                var dt = Time.deltaTime;
                standDownAge += dt;
                var cruise = targetPlayer != null ? targetPlayer.SpeedKph * 0.55f : 50f;
                SpeedKph = Mathf.MoveTowards(SpeedKph, cruise, 30f * dt);
                RoadDistance = RoadPath.Wrap(RoadDistance + SpeedKph / 3.6f * dt);
                transform.rotation = RoadPath.Rotation(RoadDistance);
                var behindPlayer = targetPlayer != null ? targetPlayer.RoadDistance - RoadDistance : 999f;
                if (behindPlayer > 70f || Mathf.Abs(behindPlayer) > 300f || standDownAge > 30f) Destroy(gameObject);
                return;
            }

            // 1. Alternate High-Intensity LED Emergency Strobes (Shader Emission)
            strobeTimer += Time.deltaTime * 12f;
            var isRed = Mathf.Sin(strobeTimer) > 0f;
            if (redLedMat != null)
            {
                redLedMat.SetColor("_EmissionColor", isRed ? (German ? PoliceBlueB : Color.red) * 5.5f : Color.black);
            }
            if (blueLedMat != null)
            {
                blueLedMat.SetColor("_EmissionColor", !isRed ? new Color(0.1f, 0.5f, 1f) * 5.5f : Color.black);
            }
            // Same phase as the emission, so the cast light and the glowing lens agree.
            if (redStrobe != null) redStrobe.intensity = isRed ? 5.5f : 0f;
            if (blueStrobe != null) blueStrobe.intensity = isRed ? 0f : 5.5f;

            if (targetPlayer == null) return;

            // 2. Tactical Pursuit AI Navigation based on Formation Slot
            float targetLane;
            float targetDistDelta;

            // A flanker sits alongside with real clearance: both half-widths plus a gap.
            // The old fixed 3.4 m offset was often off the edge of a narrow road, so the
            // lane clamp pinned the flanker about a metre from the player's centre -
            // inside the player's car. Where its own side has no room it takes the other
            // side, and only where neither does (a narrow road with the player in the
            // middle) does it drop in close behind - still in the chase camera's view.
            var laneLimit = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            var flankOffset = targetPlayer.HalfWidth + hullHalfWidth + 0.7f;
            switch (SlotIndex % 3)
            {
                case 0: // Left Flank Interceptor
                case 1: // Right Flank Interceptor
                    var flankSide = SlotIndex % 3 == 0 ? -1f : 1f;
                    targetLane = targetPlayer.LateralOffset + flankSide * flankOffset;
                    targetDistDelta = 0.5f;
                    if (Mathf.Abs(targetLane) > laneLimit)
                        targetLane = targetPlayer.LateralOffset - flankSide * flankOffset;
                    if (Mathf.Abs(targetLane) > laneLimit)
                    {
                        targetLane = targetPlayer.LateralOffset;
                        targetDistDelta = SlotIndex % 3 == 0 ? -9f : -15f;
                    }
                    break;
                default: // Rear Pursuer / Rammer
                    targetLane = targetPlayer.LateralOffset;
                    targetDistDelta = -7.5f;
                    break;
            }

            var distToTarget = (targetPlayer.RoadDistance + targetDistDelta) - RoadDistance;
            // Units spawn behind, out of view, so they have to be able to catch up: a
            // fixed 152 km/h top speed at one star was slower than the player's car, the
            // cruiser never arrived and the pursuit timed out as "lost them" unseen.
            // While well behind they run at the player's speed plus a closing margin.
            var maxSpeed = 138f + unitHeatLevel * 14f;
            if (distToTarget > 20f) maxSpeed = Mathf.Max(maxSpeed, targetPlayer.SpeedKph + 45f);
            if (distToTarget > 5f) // Behind target position: accelerate
            {
                SpeedKph = Mathf.MoveTowards(SpeedKph, maxSpeed, Time.deltaTime * (distToTarget > 20f ? 60f : 36f));
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
                WreckCop(byPlayer: true);
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
                RoadRageHaptics.Medium();
                if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.6f);
            }
        }

        /// byPlayer: the player took it out. Only then does it play the takedown -
        /// hit-stop, 0.32x slow motion, the $5,000 bonus. A cruiser that piles into
        /// traffic or is caught in a tanker blast used to trigger all of that too,
        /// so every police crash anywhere slowed the whole game down and paid out
        /// for something the player did not do.
        public void WreckCop(bool byPlayer = false)
        {
            if (isWrecked) return;
            isWrecked = true;
            // A wreck goes dark. Left strobing, a crashed cruiser sliding across the
            // road read as one still driving, badly.
            if (redLedMat != null) redLedMat.SetColor("_EmissionColor", Color.black);
            if (blueLedMat != null) blueLedMat.SetColor("_EmissionColor", Color.black);
            if (redStrobe != null) redStrobe.intensity = 0f;
            if (blueStrobe != null) blueStrobe.intensity = 0f;

            wreckSlideDir = targetPlayer != null ? Mathf.Sign(LateralOffset - targetPlayer.LateralOffset) : (Random.value > 0.5f ? 1f : -1f);
            if (Mathf.Abs(wreckSlideDir) < 0.1f) wreckSlideDir = 1f;
            // The hit's energy: faster crashes throw it further and spin it harder.
            var impact = Mathf.Clamp(SpeedKph, 40f, 200f);
            wreckLateralSpeed = wreckSlideDir * Mathf.Lerp(2.5f, 8f, (impact - 40f) / 160f);
            wreckYawRate = wreckSlideDir * Random.Range(0.8f, 1.2f) * Mathf.Lerp(90f, 300f, (impact - 40f) / 160f);
            wreckRoll = wreckSlideDir * Mathf.Lerp(4f, 9f, (impact - 40f) / 160f);

            var contactPoint = transform.position + Vector3.up * 0.6f;
            if (byPlayer && RoadRageTakedownDirector.Instance != null)
            {
                RoadRageTakedownDirector.Instance.TriggerTakedown(transform, contactPoint, Vector3.up, SpeedKph);
            }
            else
            {
                CrashEffects.Active?.PlayAt(contactPoint);
                if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCrash(0.8f);
            }

            if (RoadRagePolicePursuitDirector.Instance != null)
            {
                RoadRagePolicePursuitDirector.Instance.NotifyPoliceDestroyed(this);
            }

        }
    }
    /// German police markings for a cruiser: POLIZEI in white on a blue band along
    /// both sides and across the back, and a lit POLIZEI sign on the roof light bar.
    /// Flat quads over the pack's single-material car (Assets/Resources/Police,
    /// Tools/Blender/build_polizei_livery.py).
    internal static class PolizeiLivery
    {
        private static Material band;
        private static Material panel;
        private static bool loaded;

        private static bool Load()
        {
            if (loaded) return band != null;
            loaded = true;
            var tex = Resources.Load<Texture2D>("Police/T_polizei");
            if (tex == null)
            {
                Debug.LogWarning("Missing Police/T_polizei - cruisers carry no POLIZEI markings.");
                return false;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            // The texture's top half is the band, the bottom half the lit sign.
            band = new Material(shader) { name = "Polizei Band", mainTexture = tex };
            band.mainTextureScale = new Vector2(1f, 0.5f);
            band.mainTextureOffset = new Vector2(0f, 0.5f);
            if (band.HasProperty("_Smoothness")) band.SetFloat("_Smoothness", 0.6f);
            panel = new Material(shader) { name = "Polizei Sign", mainTexture = tex };
            panel.mainTextureScale = new Vector2(1f, 0.5f);
            panel.mainTextureOffset = Vector2.zero;
            panel.EnableKeyword("_EMISSION");
            panel.SetTexture("_EmissionMap", tex);
            panel.SetTextureScale("_EmissionMap", new Vector2(1f, 0.5f));
            panel.SetColor("_EmissionColor", Color.white * 1.6f);
            panel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return true;
        }

        public static void Apply(Transform car, GameObject model)
        {
            if (!Load() || !LocalBounds(car, model, out var b)) return;
            var bandHeight = Mathf.Clamp(b.size.y * 0.17f, 0.16f, 0.3f);
            var y = b.min.y + b.size.y * 0.40f;
            // Along the doors: the length's middle, a little back from the mirrors.
            var z = b.center.z - b.size.z * 0.06f;
            var length = b.size.z * 0.5f;
            var halfWidth = SideAt(car, model, y, z - length * 0.5f, z + length * 0.5f, b);
            for (var side = -1; side <= 1; side += 2)
                Quad(car, band, new Vector3(b.center.x + side * (halfWidth + 0.012f), y, z),
                    Quaternion.LookRotation(new Vector3(-side, 0f, 0f)), length, bandHeight);
            Quad(car, band, new Vector3(b.center.x, b.min.y + b.size.y * 0.47f, b.min.z - 0.012f),
                Quaternion.identity, b.size.x * 0.62f, bandHeight * 0.8f);
        }

        public static void AddRoofSign(Transform lightbar)
        {
            if (!Load()) return;
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = "Polizei Sign Housing";
            housing.transform.SetParent(lightbar, false);
            housing.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            housing.transform.localScale = new Vector3(0.78f, 0.16f, 0.12f);
            housing.GetComponent<Renderer>().sharedMaterial = new Material(band.shader) { color = new Color(0.04f, 0.04f, 0.05f) };
            Object.Destroy(housing.GetComponent<Collider>());
            Quad(lightbar, panel, new Vector3(0f, 0.14f, -0.062f), Quaternion.identity, 0.74f, 0.14f);
            Quad(lightbar, panel, new Vector3(0f, 0.14f, 0.062f), Quaternion.Euler(0f, 180f, 0f), 0.74f, 0.14f);
        }

        /// A quad faces -Z, so `rotation` turns its -Z to the outside.
        private static void Quad(Transform parent, Material material, Vector3 localPosition, Quaternion localRotation,
            float width, float height)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = material.name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = localRotation;
            quad.transform.localScale = new Vector3(width, height, 1f);
            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(quad.GetComponent<Collider>());
        }

        /// The model's bounds in the car's own frame, whatever way the car faces now.
        private static bool LocalBounds(Transform car, GameObject model, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var lb = r.localBounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = car.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return found;
        }

        /// Half-width of the body at the band, measured from the mesh where it is
        /// readable - the bounds include the wing mirrors, which would leave the band
        /// floating off the doors. Falls back to just inside the bounds.
        private static float SideAt(Transform car, GameObject model, float y, float z0, float z1, Bounds b)
        {
            var best = 0f;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var p = car.InverseTransformPoint(r.transform.TransformPoint(vertices[i]));
                    if (p.z < z0 || p.z > z1 || Mathf.Abs(p.y - y) > 0.15f) continue;
                    best = Mathf.Max(best, Mathf.Abs(p.x - b.center.x));
                }
            }
            return best > 0.3f ? best : b.extents.x * 0.94f;
        }
    }
}
