using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    /// Keeps only the street lights nearest the player switched on.
    ///
    /// The renderer is Forward with an additional-lights-per-object limit of 4, so beyond
    /// a handful URP is choosing which lights apply to each surface every frame - and as
    /// the car moves that choice changes, which is a light popping on a wall. A live world
    /// is eight 150 m chunks with lamps down both sides, so leaving every one of them
    /// enabled is both the popping and a pile of culling work for lights a kilometre away.
    ///
    /// So the pool sorts by distance a few times a second and enables a fixed number. The
    /// budget is small deliberately: with a per-object limit of 4, more than about a dozen
    /// in play buys nothing a driver can see.
    /// Selects the render pipeline asset that matches the frame budget.
    ///
    /// GraphicsSettings holds exactly one default pipeline, so until now every quality level
    /// rendered with the same URP asset: MSAA 4x and SSAO on the phone as well as on the
    /// desktop. Gate A measured under 1 FPS on that phone in a scene already diagnosed as
    /// alpha-test overdraw - that is a fill-rate budget problem, and MSAA 4x at 2460x1080 is
    /// four samples per pixel of it. Nothing about the biomes, the moods or the materials
    /// changes; only how many times each pixel is shaded.
    ///
    /// QualitySettings.renderPipeline is the supported per-level override and it takes the
    /// level's asset over GraphicsSettings.defaultRenderPipeline. Because it resolves before
    /// the first frame, the choice cannot live in Awake - the world would already have started
    /// building under the desktop asset - so it is a RuntimeInitializeOnLoadMethod ordered
    /// BeforeSceneLoad, which runs before the bootstrap's own AfterSceneLoad hook.
    ///
    /// The assets are found by path, not by a serialised reference. Nothing in this file holds
    /// a serialised reference to anything: the whole world is built in code, which is the same
    /// reason Resources.Load is used everywhere else here.
    public static class QualityPipeline
    {
        /// Tiers, cheapest first. -quality= forces one, and is the A/B switch this is for.
        private const int Mobile = 0;
        private const int Balanced = 1;
        private const int Full = 2;

        private const string MobilePath = "Assets/Resources/Settings/RoadRageURP_Mobile.asset";
        private const string BalancedPath = "Assets/Resources/Settings/RoadRageURP_MSAA2.asset";
        private const string FullPath = "Assets/Resources/Settings/RoadRageURP.asset";

        /// Set by -quality=mobile|balanced|full. Null means "resolve from the platform".
        private static int? tierOverride;
        /// Also settable from the editor, which cannot pass -quality= on the command line.
        /// Same shape as the low-detail override: the pref persists so it survives entering
        /// play mode, and it has to be cleared explicitly afterwards or every later reading is
        /// off the shipping path.
        private static string tierNameOverride;
#if UNITY_EDITOR
        private const string TierPrefKey = "RoadRage.QualityTier";
#endif

        public static int Tier { get; private set; } = Mobile;
        public static string TierName => Tier switch { Mobile => "mobile", Balanced => "balanced", _ => "full" };
        /// Path the selected asset was loaded from, or null when the default is in use.
        public static string AppliedPath { get; private set; }

        /// Level assignments. A sweep of the four levels visits three distinct configurations
        /// - levels 0 and 1 are both the mobile tier, because the budget they share is the
        /// same one and a fourth asset would only be a fourth thing to keep in sync. The full
        /// tier takes level 3 because that is what the project already ran at
        /// (m_CurrentQuality: 3) and what the city passes force on the fly.
        public static int LevelForTier(int tier) => tier switch { Mobile => 0, Balanced => 2, _ => 3 };

        /// Re-applies the pipeline after something set the quality level behind this class's
        /// back. QualitySettings.SetQualityLevel discards the per-level renderPipeline
        /// override, so the asset has to be reassigned or the level renders with whatever
        /// GraphicsSettings defaults to - which is the bug this whole class exists to fix.
        public static void Rearm()
        {
            Tier = ResolveTier();
            AppliedPath = Select(Tier);
        }

        /// The platform floors the tier. Two of this project's own city passes call
        /// SetQualityLevel(3) mid-run, so without the floor a phone entering Brooklyn would
        /// silently start rendering MSAA 4x with SSAO - the two costs Gate A is most likely to
        /// die on, switched on by a code path that has nothing to do with graphics budget.
        /// On desktop the level decides, which is what makes a plain level sweep a usable A/B.
        /// -quality= still overrides both, including on a device, because measuring the phone
        /// at desktop settings is the entire point of the sweep.
        private static int ResolveTier()
        {
            if (tierOverride.HasValue) return tierOverride.Value;
            return Application.isMobilePlatform ? Mobile : TierForLevel(QualitySettings.GetQualityLevel());
        }

        /// Inverse of LevelForTier for the levels that were chosen outside this class.
        private static int TierForLevel(int level) => level switch
        {
            <= 1 => Mobile,
            2 => Balanced,
            _ => Full,
        };

        /// Forces a tier from the editor or from code. Re-enter play mode for the switch to
        /// take effect: the pipeline is chosen before the scene loads, so a running world
        /// cannot move to another one without rebuilding.
        public static void SetTier(string name)
        {
            tierNameOverride = name;
#if UNITY_EDITOR
            if (name == null) UnityEditor.EditorPrefs.DeleteKey(TierPrefKey);
            else UnityEditor.EditorPrefs.SetString(TierPrefKey, name);
#endif
            Debug.Log($"RR_TIER forced to {name ?? "platform default"} " +
                      "(re-enter play mode for the pipeline to change)");
        }

        public static void ClearTierOverride() => SetTier(null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            tierOverride = ParseTier(ResolveTierName());
            Tier = ResolveTier();
            AppliedPath = Select(Tier);

            Debug.Log($"RR_TIER {TierName} -> {(AppliedPath ?? "GraphicsSettings default")} " +
                      $"(platform={(Application.isMobilePlatform ? "mobile" : "desktop")}" +
                      $"{(tierOverride.HasValue ? ", forced" : "")})");
        }

        /// Precedence: an in-session override, then the editor pref, then -quality=. The pref
        /// outranks the command line because the editor cannot set one - a pref the command
        /// line outranked would be untestable from the editor, which is where it is set.
        private static string ResolveTierName()
        {
#if UNITY_EDITOR
            if (tierNameOverride == null
                && UnityEditor.EditorPrefs.HasKey(TierPrefKey))
                tierNameOverride = UnityEditor.EditorPrefs.GetString(TierPrefKey);
#endif
            return tierNameOverride ?? RoadRageBootstrap.CommandLineValue("-quality=");
        }

        private static int? ParseTier(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "mobile" => Mobile,
            "balanced" => Balanced,
            "full" => Full,
            _ => null,
        };

        private static string Select(int tier)
        {
            var path = tier switch { Mobile => MobilePath, Balanced => BalancedPath, _ => FullPath };
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var asset = LoadPipeline(name);
            if (asset != null)
            {
                QualitySettings.renderPipeline = asset;
                return path;
            }

            // Falling back is not benign, and this line proved it on 2026-09-18: the editor
            // logged it as a warning, it scrolled past in a console full of info lines, and
            // every tier quietly rendered the same pipeline for the rest of the session -
            // which is exactly the bug the per-tier split exists to fix, and on a phone it
            // means MSAA 4x with SSAO. A silent fallback that disables the feature is the
            // anti-pattern PRODUCTION-GATES section 8 keeps recording, so it is an error now
            // and it names what it actually found.
            Debug.LogError($"RR_TIER {path} not found under Resources. Every quality level will " +
                           "render GraphicsSettings.defaultRenderPipeline instead: MSAA and SSAO " +
                           "will NOT differ per tier. Pipelines found under Resources/Settings: " +
                           $"{string.Join(", ", FoundPipelines())}");
            QualitySettings.renderPipeline = null;
            return null;
        }

        /// Direct path first, then a name match over the folder.
        ///
        /// The second lookup exists because a Resources path is a runtime string and cannot be
        /// validated by the compiler or by SymbolCheck: get it wrong and the asset is simply
        /// not there, which is a silent no-op rather than an error. Matching on the asset's own
        /// name tolerates a path that resolves differently than expected.
        private static UniversalRenderPipelineAsset LoadPipeline(string name)
        {
            var direct = Resources.Load<UniversalRenderPipelineAsset>($"Settings/{name}");
            if (direct != null) return direct;
            foreach (var candidate in Resources.LoadAll<UniversalRenderPipelineAsset>("Settings"))
                if (candidate != null && candidate.name == name) return candidate;
            return null;
        }

        /// Names what is actually reachable, so a failed lookup reports the state of the
        /// folder instead of only the path that missed.
        private static string FoundPipelines()
        {
            var all = Resources.LoadAll<UniversalRenderPipelineAsset>("Settings");
            if (all == null || all.Length == 0)
                return "(none - is Assets/Resources/Settings imported?)";
            var names = new List<string>();
            foreach (var asset in all) if (asset != null) names.Add(asset.name);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }
    }

    internal static class LocalLights
    {
        private const float SweepSeconds = 0.25f;

        private static readonly List<Light> pool = new();
        private static float nextSweep;

        /// Statics outlive a domain reload while the GameObjects they point at do not.
        /// Same reset every other static in this file needs, for the same reason.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pool.Clear();
            nextSweep = 0f;
        }

        public static void Register(Light light)
        {
            if (light != null) pool.Add(light);
        }

        public static int Live { get; private set; }

        public static void Focus(Vector3 focus, int budget)
        {
            if (Time.unscaledTime < nextSweep) return;
            nextSweep = Time.unscaledTime + SweepSeconds;

            // Chunk unload destroys these, so the pool is full of holes by design.
            pool.RemoveAll(light => light == null);

            pool.Sort((a, b) =>
                (a.transform.position - focus).sqrMagnitude
                .CompareTo((b.transform.position - focus).sqrMagnitude));

            Live = Mathf.Min(budget, pool.Count);
            for (var i = 0; i < pool.Count; i++) pool[i].enabled = i < budget;
        }
    }

    public sealed class RoadRageBootstrap : MonoBehaviour
    {
        private const float RoadWidth = RoadPath.Width;
        private const float WorldLength = RoadPath.Length;

        private sealed class MaterialDict
        {
            private readonly Dictionary<string, Material> inner = new();
            private readonly RoadRageBootstrap owner;

            public MaterialDict(RoadRageBootstrap owner) => this.owner = owner;

            /// Keys already reported missing. Building one chunk performs hundreds of
            /// lookups, so warning per lookup buried the console under thousands of
            /// identical lines - which is how the condition below went unnoticed while
            /// it was actively replacing the world's materials.
            private readonly HashSet<string> reported = new();

            public Material this[string key]
            {
                get
                {
                    if (inner.TryGetValue(key, out var mat) && mat != null) return mat;
                    if (reported.Add(key))
                        Debug.LogWarning($"[RoadRage] Material '{key}' was not in the dictionary; " +
                                         "substituting a flat fallback. It will not alpha-clip, tile " +
                                         "or take a surface map, so foliage and glass render as solid.");
                    var fallback = owner.MakeMaterial(key, new Color(0.6f, 0.5f, 0.4f));
                    inner[key] = fallback;
                    return fallback;
                }
                set => inner[key] = value;
            }

            public int Count => inner.Count;
            public bool ContainsKey(string key) => inner.ContainsKey(key);
            public bool TryGetValue(string key, out Material mat) => inner.TryGetValue(key, out mat);
            public void Clear()
            {
                inner.Clear();
                reported.Clear();
            }
        }

        private readonly MaterialDict materials;
        public RoadRageBootstrap() => materials = new MaterialDict(this);
        private ReflectionProbe reflectionProbe;
        private Transform car;
		public static string requestedBiome;
        /// Indices the picker and journey currently expose.
        // CANAL TOWN (10) is out: one street of the same few houses repeating was not
        // worth a biome slot. Its builder stays for -biome=canal.
        private static readonly int[] ActiveBiomes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        private static readonly string[] Biomes =
        {
            "GREENWOOD", "SNOW STATION", "SEWER TUNNEL", "TIRE DISTRICT",
            "ALIEN BIOMASS", "NEON CITY", "RED CANYON", "HONG KONG", "MANHATTAN",
            "HOLLYWOOD HILLS", "CANAL TOWN", "VOLCANO PASS", "SALT FLATS", "STORM COAST"
        };
        private static readonly string[] ComingSoon = { "VOLCANO PASS", "SALT FLATS", "STORM COAST" };
        private bool pickerSeen;
		private string biomeName;
		private WeatherKind activeWeather;
		private WeatherSystem weatherSystem;
		private float startDistance;
		public WeatherKind Weather => activeWeather;

		private DayTime dayTime = DayTime.Midday;

		/// Weather, and on the B500 the time of day, for the HUD readout.
		public string ConditionsLabel => RoadPath.Route != null
			? $"{DayTimeLabel(dayTime)}  |  {WeatherSystem.Label(activeWeather)}"
			: WeatherSystem.Label(activeWeather);

		private static string DayTimeLabel(DayTime t) => t switch
		{
			DayTime.Morning => "🌄 MORNING",
			DayTime.Evening => "🌇 EVENING",
			DayTime.Dusk => "🌆 DUSK",
			_ => "☀️ MIDDAY",
		};

		private static WeatherKind? ParseWeather(string value)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return value.ToLowerInvariant() switch
			{
				"rain" => WeatherKind.Rain,
				"storm" => WeatherKind.Storm,
				"snow" => WeatherKind.Snow,
				"clear" => WeatherKind.Clear,
				"fog" => WeatherKind.Fog,
				_ => null,
			};
	}
		public string BiomeName => biomeName;
		public Transform PlayerCar => car;
		public static IReadOnlyList<string> PlayableBiomes =>
			System.Array.ConvertAll(ActiveBiomes, i => Biomes[i]);
		public static IReadOnlyList<string> LockedBiomes => ComingSoon;
		public bool PickerOpen { get; private set; }

        /// Recompiling while play mode is running reloads the domain: statics reset, but
        /// the GameObjects they pointed at survive. A plain auto-property therefore came
        /// back null with the bootstrap still sitting in the scene, and everything reading
        /// Instance silently got nothing. Re-find it instead of trusting the static.
        private static RoadRageBootstrap instance;
        public static RoadRageBootstrap Instance
        {
            get
            {
                if (instance == null) instance = FindAnyObjectByType<RoadRageBootstrap>();
                return instance;
            }
            private set => instance = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureWorld()
        {
            if (FindAnyObjectByType<RoadRageBootstrap>() != null) return;
            new GameObject("Road Rage Unity Bootstrap").AddComponent<RoadRageBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }
            Instance = this;

            // QualityPipeline.Apply chose a tier, not a quality level, and the two can
            // disagree: in the editor the saved level is whatever was last selected, so Play
            // could start on the full pipeline with the mobile shadow budget, or the reverse.
            // SetQualityLevel also resets the per-level pipeline override, so the level has to
            // be settled before anything reads the budget - hence here, at the top of Awake,
            // before ApplyPlatformQuality is reached through BuildLighting.
            var wantedLevel = QualityPipeline.LevelForTier(QualityPipeline.Tier);
            if (QualitySettings.GetQualityLevel() != wantedLevel)
            {
                QualitySettings.SetQualityLevel(wantedLevel, true);
                QualityPipeline.Rearm();
                Debug.Log($"RR_TIER quality level {wantedLevel} ({QualityPipeline.TierName}) forced to " +
                          $"match the chosen pipeline; pipeline={QualityPipeline.AppliedPath ?? "default"}");
            }

            foreach (var oldHud in FindObjectsByType<RoadRageHUD>(FindObjectsInactive.Include))
            {
                DestroyImmediate(oldHud);
            }

			// English formatting everywhere, whatever the machine's language: on a German
			// or Turkish Windows the clock read "1:23,4", distances "3,4 km" and the
			// thousands in scores and cash came out with dots.
			var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
			System.Globalization.CultureInfo.DefaultThreadCurrentCulture = english;
			System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = english;
			System.Globalization.CultureInfo.CurrentCulture = english;
			System.Globalization.CultureInfo.CurrentUICulture = english;
			biomeName = ResolveBiome();
			Time.timeScale = 1f;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            GameState.Load();
            GameState.RollDailyMissions();
            GameState.ResetRun();
            GameState.BeginRun();
            // -car=N forces a vehicle for verification captures without owning it. Routed
            // through the ephemeral ForcedCar override so the driven car changes for the
            // capture while the real save's SelectedCar/OwnedCars are never touched or
            // persisted (the old code added the car to OwnedCars, which a later Save baked in).
            // ForcedCar is a static that can survive a domain reload being disabled, so it is
            // cleared unconditionally first and only re-set when -car= is actually present.
            GameState.ForcedCar = null;
            if (int.TryParse(CommandLineValue("-car="), out var forcedCar))
            {
                GameState.ForcedCar = Mathf.Clamp(forcedCar, 0, GameState.Cars.Length - 1);
            }
            // -weather=rain|storm|snow|clear forces the roll for verification captures.
            var biomeIndex = System.Array.IndexOf(Biomes, biomeName);
            activeWeather = ParseWeather(CommandLineValue("-weather="))
                            ?? WeatherSystem.Roll(Mathf.Max(0, biomeIndex));

            // The picked biome becomes the journey's first zone; the run then travels on
            // through the rest of the order rather than looping this one.
            journeyStart = Mathf.Max(0, System.Array.IndexOf(JourneyOrder,
                Mathf.Max(0, System.Array.IndexOf(Biomes, biomeName))));
            RoadPath.HalfWidthProvider = HalfWidthAtDistance;
            RoadPath.CurveScaleProvider = CurveScaleAtDistance;
            RoadPath.ElevationScaleProvider = ElevationScaleAtDistance;
            ApplyBiomeRoute(biomeIndex);
            ProfileChunks = HasCommandLineFlag("-profile");
            if (HasCommandLineFlag("-selftest")) gameObject.AddComponent<LoopSelfTest>();
            NoCanopy = HasCommandLineFlag("-nocanopy");
            if (HasCommandLineFlag("-lowdetail")) ForceLowDetailBudget(true);
            LogSky = HasCommandLineFlag("-skylog");
            if (LogSky) StartCoroutine(SkyAudit());
            // Both of these are verification switches, in the same spirit as -nocanopy: the
            // two effects added on 2026-09-18 are the only ones in the frame with a
            // measurable whole-screen cost, so each has to be removable on its own to be
            // measured at all.
            BloomDisabled = HasCommandLineFlag("-nobloom");
            ReflectionsEnabled = !HasCommandLineFlag("-noreflections");
            ShadowsDisabled = HasCommandLineFlag("-noshadows");
            ChaseCamera.LogCamera = HasCommandLineFlag("-camlog");
            var cinematic = HasCommandLineFlag("-cinematic");
            ArcadeCarController.CinematicPilot = cinematic;
            RoadRageHUD.HideForCapture = HasCommandLineFlag("-cleanshot") || cinematic;

            float.TryParse(CommandLineValue("-startkm="),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var startKm);
            var hadStartOverride = false;
            foreach (var argument in System.Environment.GetCommandLineArgs())
                if (argument.StartsWith("-startkm=", System.StringComparison.OrdinalIgnoreCase))
                {
                    hadStartOverride = true;
                    break;
                }
            startDistance = Mathf.Max(0f, startKm * 1000f);
            ApplyCityRenderPreset(hadStartOverride);

            // Strip any stray point/spot lights or halo/flare objects from imported scenes
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                l.flare = null;
                if (l.type != LightType.Directional) DestroyImmediate(l.gameObject);
            }
            foreach (var c in FindObjectsByType<Component>(FindObjectsInactive.Include))
            {
                if (c != null && (c.GetType().Name == "FlareLayer" || c.GetType().Name.Contains("LensFlare") || c.GetType().Name.Contains("Halo")))
                {
                    DestroyImmediate(c);
                }
            }
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;

            BuildMaterials();
            BuildLighting();
            // The horizon ridge and sky dome are the only part of the world that is not
            // chunk-streamed, and they used to be built solely from ReloadBiome - so a
            // cold start into Greenwood had no ridge at all: the forest ended at a wall
            // of trees with sky above it. It has to be built on the first frame too.
            EnsureGlobalHorizonSky(biomeIndex);
            UpdateStreaming(startDistance);
            BuildCar();
            if (biomeName == Biomes[8]) DesaturateManhattanCar();
            BuildTraffic();
            BuildCamera();
            CrashEffects.Create(materials["White Paint"]);
            weatherSystem = gameObject.AddComponent<WeatherSystem>();
            var particleMaterial = Resources.Load<Material>("WeatherParticle");
            if (particleMaterial == null)
            {
                Debug.LogWarning("Missing WeatherParticle material; weather will not render");
            }
            else
            {
                weatherSystem.Configure(activeWeather, car, particleMaterial);
            }
            gameObject.AddComponent<RoadRageB500Stages>();
            if (RoadRageMusic.Instance == null) gameObject.AddComponent<RoadRageMusic>();
            RollRunConditions();
			gameObject.AddComponent<RoadRageHUD>().Initialize(car.GetComponent<ArcadeCarController>(), this);
			if (HasCommandLineFlag("-picker"))
				OpenPicker();
			var screenshotPath = CommandLineValue("-shot=");
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				gameObject.AddComponent<BiomeScreenshot>().Initialize(screenshotPath);
				// Verification captures must show gameplay, not the landing showcase
				// orbit, so a -shot= run skips the START RUN gate automatically.
				if (RoadRageLandingDirector.Instance != null)
					RoadRageLandingDirector.Instance.LaunchRun();
			}
        }

		internal static string CommandLineValue(string prefix)
		{
			foreach (var argument in System.Environment.GetCommandLineArgs())
				if (argument.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
					return argument.Substring(prefix.Length);
			return null;
		}

		internal static bool HasCommandLineFlag(string flag)
		{
			foreach (var argument in System.Environment.GetCommandLineArgs())
				if (string.Equals(argument, flag, System.StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		private void ApplyCityRenderPreset(bool hadStartOverride)
		{
			var preset = CommandLineValue("-preset=");
			if (string.IsNullOrWhiteSpace(preset)) return;
			preset = preset.Trim().ToLowerInvariant();

			if (biomeName == Biomes[7] && preset == "brooklyn-shot")
			{
				activeWeather = WeatherKind.Rain;
				if (!hadStartOverride) startDistance = 0f;
			}
			else if (biomeName == Biomes[8] && preset == "manhattan-shot")
			{
				activeWeather = WeatherKind.Clear;
				if (!hadStartOverride) startDistance = 0f;
			}
		}

        private string ResolveBiome()
        {
           
            if (!string.IsNullOrEmpty(requestedBiome))
                return requestedBiome;

            // Explicit -biome= outranks a saved picker choice so a verification capture
            // cannot be hijacked by whatever biome was last picked interactively.
            var requested = CommandLineValue("-biome=");
            if (string.IsNullOrEmpty(requested))
            {
                var saved = PlayerPrefs.GetString("ROAD_RAGE_BIOME", "");
                // A biome saved before it left the picker starts Greenwood instead.
                if (!string.IsNullOrEmpty(saved) &&
                    System.Array.IndexOf(ActiveBiomes, System.Array.IndexOf(Biomes, saved)) >= 0)
                    return saved;
                return Biomes[0];
            }

            pickerSeen = true;
            var value = requested.ToLowerInvariant();

            if (value.Contains("greenwood")) return Biomes[0];
            if (value.Contains("snow")) return Biomes[1];
            if (value.Contains("sewer")) return Biomes[2];
            if (value.Contains("tire") || value.Contains("garage")) return Biomes[3];
            if (value.Contains("alien") || value.Contains("biomass")) return Biomes[4];
            if (value.Contains("neon") || value.Contains("city")) return Biomes[5];
            if (value.Contains("canyon") || value.Contains("desert")) return Biomes[6];
            if (value.Contains("brooklyn") || value.Contains("kowloon") || value.Contains("hong")) return Biomes[7];
            if (value.Contains("manhattan") || value.Contains("cyber") || value.Contains("sprawl")) return Biomes[8];
            if (value.Contains("hollywood") || value.Contains("hills")) return Biomes[9];
            if (value.Contains("canal") || value.Contains("asia")) return Biomes[10];
            if (value.Contains("midnight") || value.Contains("dock")) return Biomes[10];
            if (value.Contains("volcano") || value.Contains("pass")) return Biomes[11];
            if (value.Contains("salt") || value.Contains("flat")) return Biomes[12];
            if (value.Contains("storm") || value.Contains("coast")) return Biomes[13];

            return Biomes[0];  // DEFAULT
        }

        public void NextBiome()
        {
            if (ActiveBiomes.Length == 0)
                return;

            var currentBiomeIndex = System.Array.IndexOf(Biomes, biomeName);
            var currentPlayableIndex =
                System.Array.IndexOf(ActiveBiomes, currentBiomeIndex);

            var nextPlayableIndex =
                (currentPlayableIndex + 1) % ActiveBiomes.Length;

            SelectBiome(Biomes[ActiveBiomes[nextPlayableIndex]]);
        }


        public void SelectBiome(string nextBiome)
		{
			// ReloadBiome closes the picker after rebuilding, so gameplay stays paused
			// (time scale 0) through the rebuild instead of resuming mid-teardown.
			ReloadBiome(nextBiome);
		}

        public void ReloadBiome(string nextBiome)
        {
            Debug.Log($"[BIOME] Reloading biome to: {nextBiome}");
            requestedBiome = nextBiome;
            biomeName = nextBiome;
            PlayerPrefs.SetString("ROAD_RAGE_BIOME", nextBiome);
            PlayerPrefs.Save();

            // 1. Destroy all active streamed chunks immediately hiding them
            foreach (var pair in liveChunks)
            {
                if (pair.Value != null)
                {
                    pair.Value.name = "OldChunk_Disposed";
                    pair.Value.SetActive(false);
                    Destroy(pair.Value);
                }
            }
            liveChunks.Clear();
            stale.Clear();

            // 2. Destroy old Sun and Post-Processing Volumes
            if (sunLight != null)
            {
                sunLight.gameObject.name = "OldSun_Disposed";
                sunLight.gameObject.SetActive(false);
                Destroy(sunLight.gameObject);
            }
            var oldVolumes = FindObjectsByType<Volume>(FindObjectsInactive.Include);
            foreach (var vol in oldVolumes)
            {
                vol.gameObject.name = "OldVol_Disposed";
                vol.gameObject.SetActive(false);
                Destroy(vol.gameObject);
            }

            // 2. Clear old ramps & traffic

            // 3. Destroy old traffic & leaked root objects
            var oldTraffic = GameObject.Find("Living Highway Traffic");
            if (oldTraffic != null)
            {
                oldTraffic.name = "OldTraffic_Disposed";
                oldTraffic.SetActive(false);
                Destroy(oldTraffic);
            }

            foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                if (go == null || go == gameObject || go.transform.parent != null) continue;
                var n = go.name;
                if (n.Contains("Garage") || n.Contains("Biomass") || n.Contains("Accident") || n.Contains("Chunk"))
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            }

            // 5. Rebuild materials dictionary for new biome
            materials.Clear();
            BuildMaterials();

            // 6. Update journeyStart and active weather
            var biomeIndex = System.Array.IndexOf(Biomes, biomeName);
            journeyStart = Mathf.Max(0, System.Array.IndexOf(JourneyOrder, Mathf.Max(0, biomeIndex)));
            activeWeather = WeatherSystem.Roll(Mathf.Max(0, biomeIndex));
            ApplyBiomeRoute(biomeIndex);
            lastPlaceIndex = -1;

            // 7. Rebuild lighting for new biome
            BuildLighting();
            // Teardown of the previous biome's ridge lives inside EnsureGlobalHorizonSky.
            // It used to happen here with Destroy(), which Unity defers to the end of the
            // frame: the reference was still a live object one line later, so the
            // `if (globalHorizonSky != null) return;` guard swallowed that rebuild. The
            // next call saw a destroyed object, which Unity's == reports as null, and built
            // again - so the ridge was silently missing on every other reload, Greenwood
            // included, and which reloads were affected depended on how the session went.
            EnsureGlobalHorizonSky(biomeIndex);

            // 8. Reset player car & pursuit
            if (RoadRagePolicePursuitDirector.Instance != null)
                RoadRagePolicePursuitDirector.Instance.ResetPursuit();
            GameState.Integrity = GameState.MaxIntegrity;
            if (car != null)
            {
                var controller = car.GetComponent<ArcadeCarController>();
                // Same centred-vs-right-lane rule as the initial spawn: single-lane
                // biomes (Greenwood, Red Canyon, Hollywood) start on the centreline.
                var reloadLaneCount = LaneCountFor(BiomeIndexAt(startDistance));
                var reloadLateral = reloadLaneCount == 1 ? -1.2f : -2.25f;
                if (controller != null)
                {
                    controller.RoadDistance = startDistance + 5f;
                    controller.LateralOffset = reloadLateral;
                    controller.SpeedKph = 0f;
                    controller.TouchThrottle = 0f;
                    controller.TouchSteer = 0f;
                    controller.CountdownTimer = 3.2f;
                }
                car.position = RoadPath.Point(startDistance + 5f, reloadLateral, 0.4f);
                car.rotation = RoadPath.Rotation(startDistance + 5f);
                var rb = car.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                BuildCar();
            }
            if (biomeName == Biomes[8]) DesaturateManhattanCar();

            // 9. Rebuild traffic & camera
            BuildTraffic();
            BuildCamera();

            // 10. Reconfigure weather system
            if (weatherSystem != null)
            {
                var particleMaterial = Resources.Load<Material>("WeatherParticle");
                if (particleMaterial != null)
                    weatherSystem.Configure(activeWeather, car, particleMaterial);
            }
            RollRunConditions();

            // 11. Stream initial chunks for the new biome
            UpdateStreaming(startDistance);

            // 12. Close picker and restore timescale
            ClosePicker();
        }

		private float timeScaleBeforePicker = 1f;

		/// Opening the picker pauses gameplay and remembers the time scale to restore. Guarded
		/// so a second open (e.g. HUD button plus hotkey in one frame) cannot capture the
		/// already-zeroed scale as the value to restore later.
		public void OpenPicker()
		{
			if (PickerOpen) return;

			timeScaleBeforePicker = Time.timeScale;
			PickerOpen = true;
			Time.timeScale = 0f;
			lastToggleTime = Time.unscaledTime;
		}

		public void ClosePicker()
		{
			if (!PickerOpen) return;

			pickerSeen = true;
			PickerOpen = false;
			Time.timeScale = timeScaleBeforePicker;
			lastToggleTime = Time.unscaledTime;
		}

		private static readonly WeatherKind[] WeatherCycle =
			(WeatherKind[])System.Enum.GetValues(typeof(WeatherKind));

		/// Advance to the next weather and actually reconfigure the particle system, so K
		/// changes what is falling rather than only the label. Mirrors the Configure call
		/// used at boot and on biome reload.
		private void CycleWeather()
		{
			var index = System.Array.IndexOf(WeatherCycle, activeWeather);
			activeWeather = WeatherCycle[(index + 1) % WeatherCycle.Length];

			if (weatherSystem != null)
			{
				weatherSystem.Configure(activeWeather, car, Resources.Load<Material>("WeatherParticle"));
			}

			Debug.Log($"[WEATHER] {WeatherSystem.Label(activeWeather)}");
		}

		private float lastToggleTime;

		private void Update()
		{
			if (car != null)
			{
				var controller = car.GetComponent<ArcadeCarController>();
				if (controller != null)
				{
					TrafficCarController.PlayerDistance = controller.RoadDistance;
					UpdateStreaming(controller.RoadDistance);
					AnnouncePlaces(controller.RoadDistance);
					BlendZoneLighting(controller.RoadDistance);
					EscalateTraffic();
					TryStageHitAndRun(controller.SpeedKph);
					// Four per object is the renderer's limit; a dozen in play is already
					// more than any one surface can use. Mobile gets a third of that.
					LocalLights.Focus(car.position, RichDetailBudget ? 12 : 4);
				}
			}

			// Escape / Start button / B key toggles picker with 350ms unscaled debounce
			if ((GameInput.GetEscapePressed() || GameInput.GetBKeyPressed()) && Time.unscaledTime - lastToggleTime > 0.35f)
			{
				lastToggleTime = Time.unscaledTime;
				if (PickerOpen) ClosePicker();
				else OpenPicker();
			}

			// P measures the loaded world against Gate A's numbers.
			if (GameInput.GetPKeyPressed()) LogLiveWorldCost();


			// K cycles weather. Random weather per run means two runs of the same biome can
			// differ by about a factor of two in light, which invalidates any A/B of a
			// lighting change. Pin it to CLEAR to compare like with like.
			if (GameInput.GetKKeyPressed())
			{
				CycleWeather();
			}

			// N key cycles to next biome
			if (GameInput.GetNKeyPressed())
			{
				NextBiome();
			}

			// Number keys 1-0 for instant biome hot-switching
			for (var i = 0; i < ActiveBiomes.Length; i++)
			{
				var digit = (i + 1) % 10;
				if (GameInput.GetNumberKey(digit))
				{
					Debug.Log($"[BIOME] Hotkey pressed for biome: {Biomes[ActiveBiomes[i]]}");
					SelectBiome(Biomes[ActiveBiomes[i]]);
				}
			}
		}

        // Daily progress lives only in memory during a run (per-hit saves were dropped for
        // performance). Persist it when the app leaves the foreground so a mid-run background
        // kill on mobile, or a desktop quit, does not lose it.
        private void OnApplicationPause(bool paused)
        {
            if (paused) GameState.FlushDailyProgress();
        }

        private void OnApplicationQuit()
        {
            GameState.FlushDailyProgress();
        }

        private Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// How far a surface tint is pulled towards neutral. Desaturating the lighting
        /// alone was not enough: the world geometry carries its own colour, and a biome
        /// like Alien Biomass is mostly violet organics and violet rock, so it still read
        /// as a single hue under a neutral key. BiomeMaterial funnels through here, so
        /// this is the one place every generated surface tint passes.
        private const float SurfaceDesaturation = 0.35f;

        /// Colour that carries meaning is exempt. Road markings, hazard cones, brake
        /// lights, neon and signage are how the player reads the road at speed - washing
        /// those out to fix the scenery would cost more than it gained. Only the
        /// environment is neutralised.
        private static bool IsSignalColour(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("neon") || lower.Contains("sign") || lower.Contains("light")
                || lower.Contains("paint") || lower.Contains("orange") || lower.Contains("hologram")
                || lower.Contains("billboard") || lower.Contains("glow") || lower.Contains("marking")
                || lower.Contains("emissive") || lower.Contains("hazard");
        }

        private Material MakeMaterial(string name, Color color, float metallic = 0f, float smoothness = 0.25f)
        {
            if (!IsSignalColour(name)) color = Desaturate(color, SurfaceDesaturation);
            var material = new Material(LitShader) { name = name, color = color };
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            // Scatter bands place hundreds of copies of the same mesh+material per chunk
            // (850 renderers in Greenwood). Instancing collapses those into few draw calls
            // at no visual cost.
            material.enableInstancing = true;
            materials[name] = material;
            return material;
        }

        private Texture2D Texture(string name) => Resources.Load<Texture2D>($"Hideout/Textures/{name}");

        /// Textures that were stored once per biome pack while being byte-identical.
        ///
        /// One leaf texture was held five times under three different names, an asphalt
        /// normal three times, tree bark three times: 48 files, 180 MB, all shipping in
        /// every build because everything under Resources ships whether it is referenced
        /// or not. Deleting the copies is not enough on its own - these are loaded by
        /// runtime path, not by GUID, so a missing file returns null and the material
        /// silently falls back to flat colour rather than failing loudly.
        ///
        /// So each removed (pack, name) maps to the copy that was kept. Generated from
        /// the LFS content hashes, so every entry is byte-identical to what it replaces
        /// and no biome renders differently.
        private static readonly Dictionary<string, string> DedupedBiomeTextures =
            new Dictionary<string, string>
        {
            { "ElderTreeGate/T_leaves_D_02",              "Biomes/RedCanyon/Textures/T_leafs_D" },
            { "ElderTreeGate/T_leaves_N",                 "Biomes/RedCanyon/Textures/T_leafs_N" },
            { "ElderTreeGate/T_stones_D",                 "Biomes/RedCanyon/Textures/T_stones_D" },
            { "ElderTreeGate/T_stones_N",                 "Biomes/RedCanyon/Textures/T_stones_N" },
            { "ForestVillage/T_bush_D",                   "Biomes/ElderTreeGate/Textures/T_desert_bush_D" },
            { "ForestVillage/T_bush_MSO",                 "Biomes/ElderTreeGate/Textures/T_desert_bush_MSO" },
            { "ForestVillage/T_bush_N",                   "Biomes/ElderTreeGate/Textures/T_desert_bush_N" },
            { "HollywoodHills/T_desert_bush_D",           "Biomes/ElderTreeGate/Textures/T_desert_bush_D" },
            { "HollywoodHills/T_desert_bush_MSO",         "Biomes/ElderTreeGate/Textures/T_desert_bush_MSO" },
            { "HollywoodHills/T_desert_bush_N",           "Biomes/ElderTreeGate/Textures/T_desert_bush_N" },
            { "HollywoodHills/T_desert_plant_D",          "Biomes/ForestVillage/Textures/T_plant_D" },
            { "HollywoodHills/T_desert_plant_MSO",        "Biomes/ForestVillage/Textures/T_plant_MSO" },
            { "HollywoodHills/T_desert_plant_N",          "Biomes/ForestVillage/Textures/T_plant_N" },
            { "HollywoodHills/T_leafs_D",                 "Biomes/RedCanyon/Textures/T_leafs_D" },
            { "HollywoodHills/T_leafs_MSO",               "Biomes/ElderTreeGate/Textures/T_leaves_MSO" },
            { "HollywoodHills/T_leafs_N",                 "Biomes/RedCanyon/Textures/T_leafs_N" },
            { "HongKong/T_ground_texture_01_D",           "Biomes/CyberpunkCity/Textures/T_ground_texture_01_D" },
            { "HongKong/T_ground_texture_01_MSO",         "Biomes/CyberpunkCity/Textures/T_ground_texture_01_MSO" },
            { "HongKong/T_ground_texture_01_N",           "Biomes/CyberpunkCity/Textures/T_ground_texture_01_N" },
            { "HongKong/T_street_props_02_MSO",           "Biomes/CyberpunkCity/Textures/T_street_props_02_MSO" },
            { "JungleRuins/T_leafs_D",                    "Biomes/RedCanyon/Textures/T_leafs_D" },
            { "JungleRuins/T_leafs_MSO",                  "Biomes/ElderTreeGate/Textures/T_leaves_MSO" },
            { "JungleRuins/T_leafs_N",                    "Biomes/RedCanyon/Textures/T_leafs_N" },
            { "JungleRuins/T_tree_bark_D",                "Biomes/HollywoodHills/Textures/T_tree_bark_D" },
            { "JungleRuins/T_tree_bark_MSO",              "Biomes/HollywoodHills/Textures/T_tree_bark_MSO" },
            { "JungleRuins/T_tree_bark_N",                "Biomes/HollywoodHills/Textures/T_tree_bark_N" },
            { "RedCanyon/T_grass_D",                      "Biomes/ElderTreeGate/Textures/T_grass_D" },
            { "RedCanyon/T_grass_MSO",                    "Biomes/ElderTreeGate/Textures/T_grass_MSO" },
            { "RedCanyon/T_grass_N",                      "Biomes/ElderTreeGate/Textures/T_grass_N" },
            { "RedCanyon/T_leafs_MSO",                    "Biomes/ElderTreeGate/Textures/T_leaves_MSO" },
            { "RedCanyon/T_stones_MSO",                   "Biomes/ElderTreeGate/Textures/T_stones_MSO" },
            { "RedCanyon/T_tree_bark_D",                  "Biomes/HollywoodHills/Textures/T_tree_bark_D" },
            { "RedCanyon/T_tree_bark_MSO",                "Biomes/HollywoodHills/Textures/T_tree_bark_MSO" },
            { "RedCanyon/T_tree_bark_N",                  "Biomes/HollywoodHills/Textures/T_tree_bark_N" },
            { "RunicForest/T_ground_02_D",                "Biomes/HollywoodHills/Textures/T_ground_02_D" },
            { "RunicForest/T_ground_02_MSO",              "Biomes/HollywoodHills/Textures/T_ground_02_MSO" },
            { "RunicForest/T_ground_02_N",                "Biomes/HollywoodHills/Textures/T_ground_02_N" },
            { "RunicForest/T_leaves_D",                   "Biomes/RedCanyon/Textures/T_leafs_D" },
            { "RunicForest/T_leaves_MSO",                 "Biomes/ElderTreeGate/Textures/T_leaves_MSO" },
            { "RunicForest/T_leaves_N",                   "Biomes/RedCanyon/Textures/T_leafs_N" },
            { "RunicForest/T_vetegation_atlas_MSO",       "Biomes/HollywoodHills/Textures/T_vetegation_atlas_MSO" },
            { "RunicForest/T_vetegation_atlas_basecolor", "Biomes/HollywoodHills/Textures/T_vetegation_atlas_basecolor" },
            { "RunicForest/T_vetegation_atlas_normal",    "Biomes/HollywoodHills/Textures/T_vetegation_atlas_normal" },
            { "Shared/T_asphalt_D",                       "Biomes/CyberpunkCity/Textures/T_ground_texture_01_D" },
            { "Shared/T_asphalt_MSO",                     "Biomes/CyberpunkCity/Textures/T_ground_texture_01_MSO" },
            { "Shared/T_asphalt_N",                       "Biomes/CyberpunkCity/Textures/T_ground_texture_01_N" },
            { "Synthwave/T_car_B_02_E",                   "Biomes/Synthwave/Textures/T_car_B_01_E" },
            { "Synthwave/T_car_B_02_N",                   "Biomes/Synthwave/Textures/T_car_B_01_N" },
        };

        private Texture2D BiomeTexture(string pack, string name) =>
            Resources.Load<Texture2D>(
                DedupedBiomeTextures.TryGetValue($"{pack}/{name}", out var shared)
                    ? shared
                    : $"Biomes/{pack}/Textures/{name}");

        /// Applies a repacked _MSO map (R=metallic, G=occlusion, A=smoothness) to a material.
        /// URP reads metallic/smoothness from _MetallicGlossMap and occlusion from
        /// _OcclusionMap.g, so the same texture serves both slots. With this bound, the
        /// per-material metallic/smoothness floats become multipliers and must go to 1
        /// or they scale the map down to nothing.
        /// smoothnessScale exists for large flat surfaces: the packed maps sit around 0.45
        /// smoothness, which on a 240 m ground plane turns the directional light into one
        /// broad blown-out sheet of specular. Props want the full map value.
        private Material BiomeSurface(Material material, string pack, string mso, float smoothnessScale = 1f)
        {
            var packed = BiomeTexture(pack, mso);
            if (packed == null)
            {
                Debug.LogWarning($"Missing surface map: {pack}/{mso}");
                return material;
            }
            material.SetTexture("_MetallicGlossMap", packed);
            material.SetTexture("_OcclusionMap", packed);
            // Do NOT force _Metallic to 1: URP replaces metallic with the map's red
            // channel rather than multiplying, so the float only matters when the
            // _METALLICSPECGLOSSMAP variant is unavailable (runtime-created materials can
            // lose it to build-time variant stripping). Leaving the caller's per-surface
            // value means the fallback path degrades to sane rock/concrete instead of
            // chrome. Same reasoning for smoothness.
            var fallbackSmoothness = material.HasProperty("_Smoothness")
                ? material.GetFloat("_Smoothness")
                : 0.3f;
            material.SetFloat("_Smoothness", Mathf.Min(smoothnessScale, Mathf.Max(fallbackSmoothness, 0.25f)));
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            return material;
        }

        private Material BiomeMaterial(string name, string pack, string albedo, string normal,
            Color tint, float metallic = 0f, float smoothness = 0.25f, string emission = null)
        {
            var material = MakeMaterial(name, tint, metallic, smoothness);
            var albedoTexture = BiomeTexture(pack, albedo);
            if (albedoTexture != null)
            {
                material.mainTexture = albedoTexture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedoTexture);
            }
            var normalTexture = BiomeTexture(pack, normal);
            if (normalTexture != null)
            {
                material.SetTexture("_BumpMap", normalTexture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (!string.IsNullOrEmpty(emission))
            {
                var emissionTexture = BiomeTexture(pack, emission);
                if (emissionTexture != null) material.SetTexture("_EmissionMap", emissionTexture);
                // One fixed blue-white applied to every emissive biome surface. At full
                // strength it put a cyan wash over lit windows in every zone at once.
                material.SetColor("_EmissionColor", Desaturate(new Color(1.6f, 2.1f, 2.7f), 0.45f));
                material.EnableKeyword("_EMISSION");
            }
            return material;
        }

        private Material BiomeCutoutMaterial(string name, string pack, string albedo, string normal,
            Color tint, float cutoff = 0.42f)
        {
            var material = BiomeMaterial(name, pack, albedo, normal, tint, 0f, 0.14f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", cutoff);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.doubleSidedGI = true;
            return material;
        }

        private void BuildMaterials()
        {
            // Dry-road caches are keyed by material name and rebuilt here. If they carried
            // over from the previous biome's materials, ApplyRoadWetness would restore stale
            // colours/smoothness to the fresh materials.
            dryRoadColors.Clear();
            dryRoadSmoothness.Clear();

            var bark = MakeMaterial("Hideout Bark PBR", new Color(0.72f, 0.66f, 0.56f), 0f, 0.2f);
            bark.mainTexture = Texture("bark_albedo");
            bark.SetTexture("_BumpMap", Texture("bark_normal"));
            bark.EnableKeyword("_NORMALMAP");

            var leaf = MakeMaterial("Hideout Leaf Cutout", new Color(0.48f, 0.78f, 0.42f), 0f, 0.12f);
            leaf.mainTexture = Texture("branch_albedo");
            leaf.SetTexture("_BumpMap", Texture("branch_normal"));
            leaf.EnableKeyword("_NORMALMAP");
            leaf.SetFloat("_AlphaClip", 1f);
            leaf.SetFloat("_Cutoff", 0.42f);
            leaf.SetFloat("_Cull", 0f);
            leaf.EnableKeyword("_ALPHATEST_ON");
            leaf.doubleSidedGI = true;

			// The kit's ground_albedo is warm grey dirt; tinting it green still reads as
			// pinkish soil under the canopy. Drive the colour directly and keep only the
			// normal map for surface break-up.
			// The Hideout kit's ground is bare dirt and read as a flat lawn once the
			// canopy went in. Runic Forest ships a real forest floor with leaf litter.
			// This is "Forest Floor PBR" - the key GroundNameFor(Greenwood) and the forest
			// verge look up for the main ground plane. It was previously created under the
			// "Forest Grass" key, which BuildMaterials then overwrote with a cutout foliage
			// material, so "Forest Floor PBR" was never in the dictionary and the ground fell
			// back to a flat brown material. Naming it correctly fixes the brown ground and
			// leaves "Forest Grass" to be solely the cutout foliage created later.
			//
			// The albedo was RedCanyon/T_grass_D, which the texture dedupe maps to
			// ElderTreeGate/T_grass_D - a sheet of grass-blade cards on black, made for
			// cutout foliage. Tiled across the ground it read as fake striped lawn, and
			// the kits' painted soil textures still read as one flat pattern. The floor
			// is now baked from a modelled forest floor - soil under individual fallen
			// leaves, needles, twigs and stones (Tools/Blender/build_forest_floor.py) -
			// so the colour, normal and occlusion come from real overlapping geometry.
			//
			// This single-texture version is only the fallback: BuildSplatMaterials
			// replaces "Forest Floor PBR" with a three-layer blend (litter, humus, verge
			// dirt) whenever the TerrainSplat shader is available.
			var ground = BiomeSurface(BiomeMaterial("Forest Floor PBR", "ForestFloor", "T_forest_litter_D", "T_forest_litter_N", Color.white, 0f, 0.1f),
				"ForestFloor", "T_forest_litter_MSO", 0.35f);
            // One texture repeat covers 2 m of floor; the ribbon UVs run 0.08 per metre.
            // Square, so leaves are not stretched along the road.
            ground.mainTextureScale = new Vector2(6.25f, 6.25f);
            // The thin strips flush with the asphalt at the road edge. They carry no
            // vertex colours, so they cannot use the splat blend; this is its verge layer.
            // Their UVs run 0.08 per relative unit across (0.15 wide) and 0.08 per metre
            // along, hence the uneven scale: ~2 m repeats both ways.
            var vergeDirt = BiomeSurface(BiomeMaterial("Forest Verge Dirt", "ForestFloor", "T_forest_verge_D", "T_forest_verge_N", Color.white, 0f, 0.1f),
				"ForestFloor", "T_forest_verge_MSO", 0.3f);
            vergeDirt.mainTextureScale = new Vector2(28f, 6.25f);

            var rock = MakeMaterial("Hideout Rock PBR", new Color(0.66f, 0.72f, 0.65f), 0f, 0.18f);
            rock.mainTexture = Texture("rock_albedo");
            rock.SetTexture("_BumpMap", Texture("rock_normal"));
            rock.EnableKeyword("_NORMALMAP");

            var plant = MakeMaterial("Hideout Plant Cutout", new Color(0.48f, 0.82f, 0.50f), 0f, 0.1f);
            plant.mainTexture = Texture("plant_albedo");
            plant.SetTexture("_BumpMap", Texture("plant_normal"));
            plant.EnableKeyword("_NORMALMAP");
            plant.SetFloat("_AlphaClip", 1f);
            plant.SetFloat("_Cutoff", 0.4f);
            plant.SetFloat("_Cull", 0f);
            plant.EnableKeyword("_ALPHATEST_ON");
            plant.doubleSidedGI = true;

            // The road ribbon fills most of the screen, so it gets a real PBR surface
            // instead of a flat colour. UVs run 0..1 across the width and distance*0.08
            // along the path, so the scales below work out to roughly 4 m asphalt tiles.
            var road = BiomeSurface(BiomeMaterial("Road", "Shared", "T_asphalt_D", "T_asphalt_N",
                new Color(0.42f, 0.44f, 0.47f), 0.03f, 0.22f), "Shared", "T_asphalt_MSO", 0.55f);
            road.mainTextureScale = new Vector2(4.5f, 3.2f);
            // Greenwood's own road surface (Tools/Road/build_forest_road.py): a worn
            // country road laid out edge to edge - wheel tracks where tyres run, a sealed
            // centre joint, edges crumbling into the verge. The ribbon's UVs run 0.16 across
            // the carriageway (0.08 per relative unit, -1..1) and 0.08 per metre along, so
            // 6.25 fits the texture exactly across and 1.0417 repeats it every 12 m.
            // The colour and smoothness are all in the maps: white tint, smoothness 1 so
            // the MSO alpha is used as is.
            var forestRoad = BiomeSurface(BiomeMaterial("Forest Road", "ForestRoad", "T_forest_road_D", "T_forest_road_N",
                Color.white, 0f, 1f), "ForestRoad", "T_forest_road_MSO", 1f);
            forestRoad.mainTextureScale = new Vector2(6.25f, 1f / (0.08f * 12f));
            var shoulder = BiomeSurface(BiomeMaterial("Shoulder", "Shared", "T_asphalt_D", "T_asphalt_N",
                new Color(0.32f, 0.34f, 0.34f), 0f, 0.1f), "Shared", "T_asphalt_MSO", 0.45f);
            shoulder.mainTextureScale = new Vector2(1.4f, 3.2f);
            MakeMaterial("White Paint", new Color(0.92f, 0.94f, 0.9f), 0f, 0.3f);
            MakeMaterial("Yellow Paint", new Color(1f, 0.66f, 0.06f), 0f, 0.25f);
            tireMaterial = MakeMaterial("Tire Rubber", new Color(0.055f, 0.055f, 0.06f), 0f, 0.4f);
            rimMaterial = MakeMaterial("Wheel Rim", new Color(0.58f, 0.59f, 0.61f), 0.75f, 0.7f);
            MakeMaterial("Car Orange", new Color(0.95f, 0.22f, 0.035f), 0.55f, 0.78f);
            MakeMaterial("Car Dark", new Color(0.012f, 0.018f, 0.022f), 0.25f, 0.55f);
            MakeMaterial("Glass", new Color(0.025f, 0.12f, 0.16f), 0.7f, 0.92f);
            MakeMaterial("Tire", new Color(0.012f, 0.012f, 0.014f), 0f, 0.08f);
            MakeMaterial("Driver Skin", new Color(0.72f, 0.43f, 0.28f), 0f, 0.32f);
            MakeMaterial("Driver Jacket", new Color(0.08f, 0.11f, 0.16f), 0.12f, 0.38f);
            MakeMaterial("Driver Hair", new Color(0.035f, 0.022f, 0.018f), 0f, 0.16f);
            MakeMaterial("Low Bark", new Color(0.18f, 0.12f, 0.075f), 0f, 0.12f);
            MakeMaterial("Low Leaf", new Color(0.09f, 0.31f, 0.12f), 0f, 0.08f);
            // Neon City + Tire District sidewalks: concrete floor albedo instead of a
            // flat colour slab.
            BiomeSurface(BiomeMaterial("Sidewalk", "CyberpunkCity",
                "T_concrete_floor_D", "T_concrete_floor_N", new Color(1.35f, 1.35f, 1.35f), 0.05f, 0.32f),
                "CyberpunkCity", "T_concrete_floor_MSO");
            // Everything the material pass calls a light source lands here: lamp heads
            // (any submesh named light/lamp/farola), signs, holograms, and the kerb glow
            // ribbons. It was a plain Lit material with no emission, so none of it glowed
            // - a "neon" that was only shiny blue paint, and lamp heads that stayed dark
            // even once the lights they stand for were switched back on in 41fb537.
            //
            // ApplyCityPhotorealSignature has been setting this material's _EmissionColor
            // per biome the whole time. Without the keyword that write did nothing, which
            // is why the intent reads as present and the result never showed.
            //
            // Emission on URP Lit, not Unlit: the variant is kept alive by
            // Assets/Resources/EmissiveVariantAnchor.mat, and ten other runtime materials
            // in this file already depend on it - Cyber Hologram and Cyber Neon Strip
            // twenty lines below, in this same family. Unlit is what the windows use
            // because a window pane should ignore scene lighting entirely; a lamp housing
            // should still take it.
            var cityNeon = MakeMaterial("City Neon", new Color(0.17f, 0.45f, 0.72f), 0.48f, 0.72f);
            cityNeon.SetColor("_EmissionColor", new Color(0.34f, 0.72f, 1.15f));
            cityNeon.EnableKeyword("_EMISSION");
            cityNeon.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

			var sign = MakeMaterial("Hideout Sign PBR", Color.white, 0.12f, 0.48f);
			sign.mainTexture = Texture("sign_albedo");
			sign.SetTexture("_BumpMap", Texture("sign_normal"));
			sign.EnableKeyword("_NORMALMAP");
			sign.SetTexture("_EmissionMap", Texture("sign_emission"));
			sign.SetColor("_EmissionColor", new Color(1.2f, 1.7f, 2.2f));
			sign.EnableKeyword("_EMISSION");
			var vehicle = MakeMaterial("Hideout Vehicle PBR", new Color(0.52f, 0.69f, 0.60f), 0.62f, 0.62f);
			vehicle.mainTexture = Texture("vehicle_albedo");
			vehicle.SetTexture("_BumpMap", Texture("vehicle_normal"));
			vehicle.EnableKeyword("_NORMALMAP");
			MakeMaterial("Hideout Tank", new Color(0.12f, 0.22f, 0.18f), 0.72f, 0.32f);
			var racerAtlasTex = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_01_A");
			var racerLightsTex = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_Emissive_01");

			var racerAtlas = MakeMaterial("Street Racer Atlas", Color.white, 0.25f, 0.75f);
			if (racerAtlasTex != null)
			{
				racerAtlas.mainTexture = racerAtlasTex;
				if (racerAtlas.HasProperty("_BaseMap")) racerAtlas.SetTexture("_BaseMap", racerAtlasTex);
			}
			if (racerLightsTex != null)
			{
				racerAtlas.SetTexture("_EmissionMap", racerLightsTex);
				racerAtlas.SetColor("_EmissionColor", new Color(2.4f, 2.3f, 2.1f));
				racerAtlas.EnableKeyword("_EMISSION");
				racerAtlas.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			}

			var racerChassis = MakeMaterial("Street Racer Chassis", Color.white, 0.20f, 0.55f);
			if (racerAtlasTex != null)
			{
				racerChassis.mainTexture = racerAtlasTex;
				if (racerChassis.HasProperty("_BaseMap")) racerChassis.SetTexture("_BaseMap", racerAtlasTex);
			}
			if (racerLightsTex != null)
			{
				racerChassis.SetTexture("_EmissionMap", racerLightsTex);
				racerChassis.SetColor("_EmissionColor", new Color(2.4f, 2.3f, 2.1f));
				racerChassis.EnableKeyword("_EMISSION");
				racerChassis.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			}

			var racerGlass = MakeMaterial("Street Racer Glass", new Color(0.06f, 0.09f, 0.12f), 0.0f, 0.96f);
			racerGlass.SetFloat("_Smoothness", 0.96f);
			materials["Street Racer Atlas"] = racerAtlas;
			materials["Street Racer Chassis"] = racerChassis;
			materials["Street Racer Glass"] = racerGlass;

            BiomeSurface(BiomeMaterial("Snow Ground", "IceStation", "T_snow_D", "T_snow_N", Color.white, 0f, 0.12f), "IceStation", "T_snow_MSO", 0.4f);
            BiomeSurface(BiomeMaterial("Ice Station", "IceStation", "T_trim_01_D", "T_trim_01_N", new Color(0.88f, 0.96f, 1f), 0.5f, 0.58f, "T_trim_01_E"), "IceStation", "T_trim_01_MSO");
            BiomeSurface(BiomeMaterial("Ice Ship", "IceStation", "T_ship_D", "T_ship_N", Color.white, 0.58f, 0.62f, "T_ship_E"), "IceStation", "T_ship_MSO");
            BiomeSurface(BiomeMaterial("Sewer Concrete", "Sewers", "T_concrete_03_D", "T_concrete_03_N", new Color(0.55f, 0.62f, 0.52f), 0f, 0.24f), "Sewers", "T_concrete_03_MSO", 0.5f);
            materials["Sewer Concrete"].SetFloat("_Cull", 0f);
            materials["Sewer Concrete"].doubleSidedGI = true;
            BiomeSurface(BiomeMaterial("Sewer Pipe", "Sewers", "T_pipes_D", "T_pipes_N", new Color(0.72f, 0.78f, 0.66f), 0.55f, 0.48f, "T_pipes_E"), "Sewers", "T_pipes_MSO");
            BiomeSurface(BiomeMaterial("Sewer Rust", "Sewers", "T_rust_modules_D", "T_rust_modules_N", new Color(0.68f, 0.56f, 0.42f), 0.42f, 0.28f), "Sewers", "T_rust_modules_MSO");
            BiomeSurface(BiomeMaterial("Garage Wall", "TireRepair", "T_Wall01a_B", "T_Wall01a_N", Color.white, 0.08f, 0.28f), "TireRepair", "T_Wall01a_MSO");
            BiomeSurface(BiomeMaterial("Garage Door", "TireRepair", "T_MetalDoor_B", "T_MetalDoor_N", Color.white, 0.62f, 0.42f), "TireRepair", "T_MetalDoor_MSO");
            BiomeSurface(BiomeMaterial("Garage Equipment", "TireRepair", "T_TireMachine01_BC", "T_TireMachine01_N", Color.white, 0.48f, 0.38f), "TireRepair", "T_TireMachine01_MSO");
            BiomeSurface(BiomeMaterial("Garage Shelf", "TireRepair", "T_TireShelf_B", "T_TireShelf_N", Color.white, 0.35f, 0.32f), "TireRepair", "T_TireShelf_MSO");
            // The flat near-black slab read as a textureless void under Neon City's
            // night mood - give it the Cyberpunk City ground and concrete albedo.
            BiomeSurface(BiomeMaterial("Industrial Ground", "CyberpunkCity",
                "T_ground_texture_01_D", "T_ground_texture_01_N", new Color(1.35f, 1.35f, 1.35f), 0.06f, 0.30f),
                "CyberpunkCity", "T_ground_texture_01_MSO");
            // "Sidewalk" is already built above (with the Neon City / Tire District sidewalk
            // comment). The byte-for-byte identical duplicate that was here has been removed -
            // it only rebuilt the same material and leaked the first instance.
            BiomeMaterial("Demo Facades", "DemoCity", "building_facades", "building_facades_nm", Color.white, 0.15f, 0.42f);
            BiomeMaterial("Demo Highrise", "DemoCity", "highrise_facades", "highrise_facades_nm", Color.white, 0.18f, 0.55f);
            BiomeMaterial("Demo Bases", "DemoCity", "building_bases", "building_bases_nm", Color.white, 0.12f, 0.38f);
            BiomeMaterial("Demo Windows", "DemoCity", "building_windows_wet", "building_windows_wet_nm", new Color(0.85f, 0.92f, 1f), 0.35f, 0.85f);
            BiomeMaterial("Demo Interior", "DemoCity", "building_interior", "building_interior_nm", Color.white, 0.15f, 0.40f);
            BiomeMaterial("Demo Props", "DemoCity", "props_main", "props_main_nm", Color.white, 0.45f, 0.50f);
            BiomeMaterial("Demo Fence", "DemoCity", "road_sideway_fences", "road_sideway_fences_nm", Color.white, 0.65f, 0.45f);
            BiomeSurface(BiomeMaterial("City Concrete", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.42f, 0.46f, 0.54f), 0.18f, 0.42f), "Synthwave", "T_concrete_MSO");
            // A real Manhattan block is brick, limestone, sandstone and glass, not one
            // grey. The NYC pack does ship that variety - its meshes carry Brick_Modern,
            // Plaster_Rough, OldWood2 and Paint_Epoxy slots - but every one of those
            // materials is HDRP/Lit, which does not render in this URP project, so the
            // material pass replaces them all. It was replacing them with a single
            // concrete grey. These give it something to choose between instead.
            //
            // Same albedo and normal as the concrete, so no new textures ship; the tint,
            // metallic and smoothness carry the difference. Each is passed through
            // MakeMaterial's 0.35 desaturation like every other surface, which is why the
            // tints start further from grey than the finished facade should look.
            BiomeSurface(BiomeMaterial("City Brick", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.55f, 0.33f, 0.26f), 0.04f, 0.22f), "Synthwave", "T_concrete_MSO");
            BiomeSurface(BiomeMaterial("City Limestone", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.80f, 0.76f, 0.67f), 0.05f, 0.30f), "Synthwave", "T_concrete_MSO");
            BiomeSurface(BiomeMaterial("City Sandstone", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.70f, 0.57f, 0.42f), 0.05f, 0.26f), "Synthwave", "T_concrete_MSO");
            BiomeSurface(BiomeMaterial("City Glass Tower", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.30f, 0.37f, 0.42f), 0.55f, 0.80f), "Synthwave", "T_concrete_MSO");
            // Storefront awning canvas. Named without "sign"/"paint"/"billboard" etc, so
            // it takes the same 0.35 desaturation as the rest of the street - fabric
            // outside a bodega is not a light source, and after the mood grade a pure hue
            // here would read as gaudy against the muted brick and stone. Pitched well
            // past the finished colour, same as the facade tints, so what survives the
            // desaturation and the grade still reads as red, green, navy et al rather than
            // collapsing toward the wall behind it.
            MakeMaterial("Awning Red", new Color(0.72f, 0.10f, 0.09f), 0f, 0.20f);
            MakeMaterial("Awning Green", new Color(0.09f, 0.44f, 0.20f), 0f, 0.20f);
            MakeMaterial("Awning Navy", new Color(0.09f, 0.16f, 0.40f), 0f, 0.20f);
            MakeMaterial("Awning Burgundy", new Color(0.42f, 0.08f, 0.18f), 0f, 0.20f);
            MakeMaterial("Awning Gold", new Color(0.70f, 0.52f, 0.10f), 0.05f, 0.30f);
            BiomeSurface(BiomeMaterial("City Windows", "Synthwave", "T_window_02_D", "T_window_02_N", new Color(0.58f, 0.72f, 1f), 0.34f, 0.72f, "T_window_02_RE"), "Synthwave", "T_window_02_MSO");
            // Emissive skyline for distant towers - the pack's own RE sheet is flat grey,
            // so the procedural pane grid gives real lit windows (Unlit, see Cyber Window).
            var citySkyline = BiomeSurface(BiomeMaterial("City Skyline", "Synthwave", "T_concrete_D", "T_concrete_N",
                new Color(0.5f, 0.54f, 0.62f), 0.15f, 0.35f, null), "Synthwave", "T_concrete_MSO");
            BiomeSurface(BiomeMaterial("City Sign", "Synthwave", "T_road_sign_D", "T_road_sign_N", Color.white, 0.18f, 0.48f, "T_road_sign_E"), "Synthwave", "T_road_sign_MSO");
            // The buildings carry MI_window_* and MI_neon_* slots that all resolve to City Windows;
            // at night in NEON CITY that emission is the main light source, so push it hard.
            // This emission is the main light source at night, so a near-pure violet
            // here dyed every wall, road and car in the zone. Desaturating the source is
            // what actually fixes the cast; Desaturate preserves luma so it stays as
            // bright a key as before.
            if (biomeName == Biomes[5])
                materials["City Windows"].SetColor("_EmissionColor",
                    Desaturate(new Color(3.4f, 2.5f, 4.8f), 0.55f));
            BiomeSurface(BiomeMaterial("City Car Paint", "Synthwave", "T_car_pain_D", "T_car_pain_N", Color.white, 0.58f, 0.72f), "Synthwave", "T_car_pain_MSO");
            BiomeSurface(BiomeMaterial("City Car Parts", "Synthwave", "T_car_parts_D", "T_car_parts_N", Color.white, 0.68f, 0.62f, "T_car_parts_E"), "Synthwave", "T_car_parts_MSO");
            BiomeSurface(BiomeMaterial("City Car B1", "Synthwave", "T_car_B_01_D", "T_car_B_01_N", Color.white, 0.52f, 0.72f, "T_car_B_01_E"), "Synthwave", "T_car_B_01_MSO");
            BiomeSurface(BiomeMaterial("City Car B2", "Synthwave", "T_car_B_02_D", "T_car_B_02_N", Color.white, 0.52f, 0.72f, "T_car_B_02_E"), "Synthwave", "T_car_B_02_MSO");
            BiomeSurface(BiomeMaterial("Alien Organic A", "AlienBiomass", "T_alien_organic_D", "T_alien_organic_N", new Color(0.68f, 0.86f, 0.72f), 0.04f, 0.48f, "T_alien_organic_E"), "AlienBiomass", "T_alien_organic_MSO");
            BiomeSurface(BiomeMaterial("Alien Organic B", "AlienBiomass", "T_alien_organic_02_D", "T_alien_organic_02_N", new Color(0.78f, 0.58f, 0.92f), 0.03f, 0.5f, "T_alien_organic_02_E"), "AlienBiomass", "T_alien_organic_02_MSO");
            BiomeSurface(BiomeMaterial("Alien Facility", "AlienBiomass", "T_modules_D", "T_modules_N", new Color(0.74f, 0.82f, 0.83f), 0.62f, 0.55f, "T_modules_E"), "AlienBiomass", "T_modules_MSO");
            BiomeSurface(BiomeMaterial("Alien Floor", "AlienBiomass", "T_floor_D", "T_floor_N", new Color(0.19f, 0.25f, 0.22f), 0.18f, 0.42f), "AlienBiomass", "T_floor_MSO", 0.45f);
            BiomeSurface(BiomeMaterial("Alien Rock", "AlienBiomass", "T_rock_01_D", "T_rock_01_N", new Color(0.48f, 0.38f, 0.56f), 0.05f, 0.24f), "AlienBiomass", "T_rock_01_MSO");

            var billboard = MakeMaterial("City Billboard", Color.white, 0.1f, 0.44f);
            var advertisement = BiomeTexture("Synthwave", "T_pub_07");
            if (advertisement != null)
            {
                billboard.mainTexture = advertisement;
                if (billboard.HasProperty("_BaseMap")) billboard.SetTexture("_BaseMap", advertisement);
                billboard.SetTexture("_EmissionMap", advertisement);
                billboard.SetColor("_EmissionColor", Desaturate(new Color(1.9f, 1.3f, 2.4f), 0.45f));
                billboard.EnableKeyword("_EMISSION");
            }
            // Cutout, not opaque. Every other foliage material in the project goes through
            // BiomeCutoutMaterial; this one went through BiomeMaterial, so the city trees
            // drew their leaf cards as solid single-sided sheets with a palm texture
            // stretched over them - the pale fans hanging over the Manhattan pavement.
            //
            // The measurement said so before anyone looked: RR_COST MANHATTAN reported
            // cutout=0 across 11,682 renderers. A biome with trees in it cannot have zero
            // alpha-clipped renderers, and that number sat in three separate readings
            // being read as "no overdraw, good" rather than as "no foliage, broken".
            BiomeSurface(BiomeCutoutMaterial("City Palm", "Synthwave", "T_palm_tree_D",
                "T_palm_tree_N", Color.white), "Synthwave", "T_palm_tree_MSO");
            // "Palm Frond" is the RedCanyon frond cutout, built explicitly below. The alias
            // to "City Palm" that was here was dead (overwritten before any use) and made the
            // city vs canyon palm assignment look conflated - the two are distinct materials.
            MakeMaterial("City Asphalt Trim", new Color(0.10f, 0.10f, 0.13f), 0.2f, 0.44f);
            MakeMaterial("City Props", new Color(0.44f, 0.43f, 0.42f), 0.15f, 0.30f);
            MakeMaterial("Taxi Sign", new Color(0.97f, 0.79f, 0.13f), 0.05f, 0.28f);

            var sand = BiomeSurface(BiomeMaterial("Canyon Sand", "RedCanyon", "T_sand_D", "T_sand_N", new Color(0.94f, 0.76f, 0.55f), 0f, 0.08f), "RedCanyon", "T_sand_MSO", 0.4f);
            sand.mainTextureScale = new Vector2(34f, 160f);
            BiomeSurface(BiomeMaterial("Canyon Ground Rock", "RedCanyon", "T_rock_ground_D", "T_rock_ground_N", new Color(0.82f, 0.58f, 0.42f), 0f, 0.12f), "RedCanyon", "T_rock_ground_MSO", 0.5f);
            BiomeSurface(BiomeMaterial("Canyon Cliff A", "RedCanyon", "T_rock_01_D", "T_rock_01_N", new Color(0.88f, 0.56f, 0.38f), 0f, 0.14f), "RedCanyon", "T_rock_01_MSO");
            BiomeSurface(BiomeMaterial("Canyon Cliff B", "RedCanyon", "T_rock_03_D", "T_rock_03_N", new Color(0.76f, 0.45f, 0.30f), 0f, 0.16f), "RedCanyon", "T_rock_03_MSO");
            BiomeSurface(BiomeMaterial("Canyon Stone", "RedCanyon", "T_stones_D", "T_stones_N", new Color(0.80f, 0.63f, 0.48f), 0f, 0.18f), "RedCanyon", "T_stones_MSO");
            BiomeMaterial("Palm Bark", "RedCanyon", "T_tree_bark_D", "T_tree_bark_N", new Color(0.72f, 0.60f, 0.44f), 0f, 0.18f);
            BiomeCutoutMaterial("Palm Frond", "RedCanyon", "T_leafs_D", "T_leafs_N", new Color(0.62f, 0.74f, 0.40f));
            BiomeCutoutMaterial("Canyon Grass", "RedCanyon", "T_grass_D", "T_grass_N", new Color(0.78f, 0.72f, 0.38f), 0.36f);

            // Forest kits. Greenwood previously ran on the Hideout kit's single tree and
            // single plant; these two packs supply nine trees and undergrowth cheap
            // enough (20-192 tris) to scatter by the hundred.
            BiomeSurface(BiomeMaterial("Wood Bark", "RunicForest", "T_bark_03_D", "T_bark_03_N",
                new Color(0.66f, 0.60f, 0.52f), 0f, 0.16f), "RunicForest", "T_bark_03_MSO", 0.5f);
            BiomeSurface(BiomeMaterial("Pine Bark", "RunicForest", "T_pinetree_bark_D", "T_pinetree_bark_N",
                new Color(0.58f, 0.50f, 0.42f), 0f, 0.16f), "RunicForest", "T_pinetree_bark_MSO", 0.5f);
            BiomeCutoutMaterial("Broadleaf Canopy", "RunicForest", "T_leaves_D", "T_leaves_N",
                new Color(0.72f, 0.84f, 0.60f), 0.38f);
            BiomeCutoutMaterial("Pine Canopy", "RunicForest", "T_pine_tree_D", "T_pine_tree_N",
                new Color(0.62f, 0.78f, 0.58f), 0.38f);
            BiomeCutoutMaterial("Forest Branch", "RunicForest", "T_branch_D", "T_branch_N",
                new Color(0.70f, 0.80f, 0.58f), 0.36f);
            BiomeCutoutMaterial("Forest Undergrowth", "RunicForest", "T_vetegation_atlas_basecolor",
                "T_vetegation_atlas_normal", new Color(0.58f, 0.66f, 0.48f), 0.34f);
            BiomeCutoutMaterial("Forest Flowers", "RunicForest", "T_flowers_D", "T_flowers_N",
                new Color(0.86f, 0.86f, 0.70f), 0.36f);
            // Black Forest vegetation (Tools/Blender/build_black_forest.py): Norway spruce,
            // silver fir and the ground cover under them, all on one atlas - bark and
            // needles in one material. Three tints so a stand is not one flat colour.
            // All three darker than the atlas: at full white (and "fresh" above it) these
            // spruces stood out bright green on the slopes beside the pack's trees.
            BiomeCutoutMaterial("Black Forest", "BlackForest", "T_blackforest_D", "T_blackforest_N",
                new Color(0.72f, 0.76f, 0.70f), 0.45f);
            BiomeCutoutMaterial("Black Forest Dark", "BlackForest", "T_blackforest_D", "T_blackforest_N",
                new Color(0.58f, 0.62f, 0.57f), 0.45f);
            BiomeCutoutMaterial("Black Forest Fresh", "BlackForest", "T_blackforest_D", "T_blackforest_N",
                new Color(0.80f, 0.84f, 0.74f), 0.45f);
            // B500 roadside furniture and the hotel (Tools/Blender/build_b500_props.py):
            // one opaque atlas for posts, signs, logs and the house.
            BiomeMaterial("B500 Props", "BlackForest", "T_b500props_D", "T_b500props_N", Color.white, 0f, 0.2f);
            BiomeSurface(BiomeMaterial("Forest Pebble", "RunicForest", "T_small_rock_D", "T_small_rock_N",
                new Color(0.62f, 0.62f, 0.58f), 0f, 0.2f), "RunicForest", "T_small_rock_MSO", 0.6f);

            BiomeCutoutMaterial("Forest Bush", "ForestVillage", "T_bush_D", "T_bush_N",
                new Color(0.54f, 0.62f, 0.44f), 0.36f);
            BiomeCutoutMaterial("Forest Fern", "ForestVillage", "T_plant_D", "T_plant_N",
                new Color(0.56f, 0.66f, 0.46f), 0.34f);
            BiomeSurface(BiomeMaterial("Forest Roots", "ForestVillage", "T_roots_D", "T_roots_N",
                new Color(0.56f, 0.48f, 0.40f), 0f, 0.18f), "ForestVillage", "T_roots_MSO", 0.5f);
            // W-beam guard rail built in Blender (Tools/Blender/build_guardrail.py). Its
            // look is baked into the textures, so the tint stays white and the
            // metallic/smoothness floats are only the fallback if the MSO variant is lost.
            BiomeSurface(BiomeMaterial("Forest Guard Rail", "Guardrail", "T_guardrail_D", "T_guardrail_N",
                Color.white, 0.6f, 0.4f), "Guardrail", "T_guardrail_MSO");
            // Roadside cliffs built in Blender (Tools/Blender/build_cliffs.py).
            BiomeSurface(BiomeMaterial("Forest Cliff", "Cliffs", "T_cliffs_D", "T_cliffs_N", Color.white, 0f, 1f),
                "Cliffs", "T_cliffs_MSO", 1f);
            BiomeSurface(BiomeMaterial("Forest Boulder", "ForestVillage", "T_rock_01_D", "T_rock_01_N",
                new Color(0.60f, 0.60f, 0.56f), 0f, 0.2f), "ForestVillage", "T_rock_01_MSO", 0.6f);
            // The Mummelsee: a small, deep, dark lake in a spruce hollow. Near black,
            // glossy, so it shows the sky and the treeline through the probe.
            MakeMaterial("Mountain Lake", new Color(0.03f, 0.045f, 0.045f), 0f, 0.94f);
            BiomeSurface(BiomeMaterial("Forest Boulder B", "ForestVillage", "T_rock_02_D", "T_rock_02_N",
                new Color(0.56f, 0.57f, 0.54f), 0f, 0.2f), "ForestVillage", "T_rock_02_MSO", 0.6f);
            BiomeSurface(BiomeMaterial("Forest Mountain", "ForestVillage", "T_mountain_D", "T_mountain_N",
                new Color(0.52f, 0.56f, 0.58f), 0f, 0.14f), "ForestVillage", "T_mountain_MSO", 0.4f);

            // Elder Tree Gate hero specimens + Jungle Ruins broadleaf undergrowth,
            // used sparingly in Greenwood to break up the repeated stands.
            BiomeSurface(BiomeMaterial("Elder Trunk", "ElderTreeGate", "T_trunk_D", "T_trunk_N",
                new Color(0.60f, 0.54f, 0.46f), 0f, 0.16f), "ElderTreeGate", "T_trunk_MSO", 0.5f);
            BiomeCutoutMaterial("Elder Canopy", "ElderTreeGate", "T_leaves_D_02", "T_leaves_N",
                new Color(0.66f, 0.80f, 0.56f), 0.38f);
            BiomeCutoutMaterial("Elder Grass", "ElderTreeGate", "T_grass_D", "T_grass_N",
                new Color(0.64f, 0.78f, 0.48f), 0.34f);
            BiomeCutoutMaterial("Jungle Frond", "JungleRuins", "T_jungle_plant_D", "T_jungle_plant_N",
                new Color(0.56f, 0.76f, 0.46f), 0.34f);

            BuildHillsMaterials();
        }

        /// Hollywood Hills: dry suburban hillside. Background buildings are 28-284 tris
        /// with their own emission map, so a whole city skyline costs almost nothing.
        private void BuildHillsMaterials()
        {
            const string pack = "HollywoodHills";
            var ground = BiomeSurface(BiomeMaterial("Hills Ground", pack, "T_ground_01_D", "T_ground_01_N",
                new Color(0.68f, 0.62f, 0.50f), 0f, 0.1f), pack, "T_ground_01_MSO", 0.35f);
            ground.mainTextureScale = new Vector2(60f, 280f);

            BiomeSurface(BiomeMaterial("Hills Concrete", pack, "T_concrete_D", "T_concrete_N",
                new Color(0.76f, 0.74f, 0.70f), 0.05f, 0.24f), pack, "T_concrete_MSO", 0.6f);
            // Second, darker stucco so neighbouring houses don't read as one wall.
            BiomeSurface(BiomeMaterial("Hills Concrete Dark", pack, "T_concrete_D", "T_concrete_N",
                new Color(0.52f, 0.47f, 0.43f), 0.05f, 0.30f), pack, "T_concrete_MSO", 0.5f);
            BiomeSurface(BiomeMaterial("Hills Brick", pack, "T_bricks_D", "T_bricks_N",
                new Color(0.74f, 0.68f, 0.62f), 0.03f, 0.22f), pack, "T_bricks_MSO", 0.6f);
            BiomeSurface(BiomeMaterial("Hills Roof", pack, "T_roof_tiles_D", "T_roof_tiles_N",
                new Color(0.66f, 0.50f, 0.42f), 0.03f, 0.26f), pack, "T_roof_tiles_MSO", 0.6f);
            BiomeSurface(BiomeMaterial("Hills Wood", pack, "T_wood_D", "T_wood_N",
                new Color(0.68f, 0.58f, 0.46f), 0.02f, 0.24f), pack, "T_wood_MSO", 0.6f);
            BiomeSurface(BiomeMaterial("Hills Metal", pack, "T_metal_01_D", "T_metal_01_N",
                new Color(0.70f, 0.72f, 0.74f), 0.55f, 0.45f), pack, "T_metal_01_MSO", 0.8f);
            BiomeSurface(BiomeMaterial("Hills Pole", pack, "T_antenna_D", "T_antenna_N",
                new Color(0.62f, 0.60f, 0.58f), 0.35f, 0.35f), pack, "T_antenna_MSO", 0.7f);
            BiomeSurface(BiomeMaterial("Hills Gate", pack, "T_gate_D", "T_gate_N",
                new Color(0.62f, 0.62f, 0.62f), 0.4f, 0.4f), pack, "T_gate_MSO", 0.7f);
            BiomeSurface(BiomeMaterial("Hills Container", pack, "T_container_D", "T_container_N",
                new Color(0.72f, 0.70f, 0.68f), 0.3f, 0.35f), pack, "T_container_MSO", 0.7f);
            // Iconic Hollywood Sign: Pure bright enamel white metal (clean, no green camo!)
            var signMaterial = MakeMaterial("Hills Sign", new Color(0.98f, 0.98f, 0.98f), 0.1f, 0.35f);
            materials["Hills Sign"] = signMaterial;
            materials["Hills Cloud"] = MakeMaterial("Hills Cloud", new Color(0.98f, 0.98f, 0.95f), 0f, 0.05f);
            BiomeSurface(BiomeMaterial("Hills Landscape", pack, "T_ground_03_D", "T_ground_03_N",
                new Color(0.60f, 0.58f, 0.50f), 0f, 0.12f), pack, "T_ground_03_MSO", 0.35f);
            BiomeSurface(BiomeMaterial("Hills Bark", pack, "T_tree_bark_D", "T_tree_bark_N",
                new Color(0.62f, 0.54f, 0.44f), 0f, 0.18f), pack, "T_tree_bark_MSO", 0.5f);

            BiomeCutoutMaterial("Hills Leaves", pack, "T_leafs_D", "T_leafs_N",
                new Color(0.40f, 0.54f, 0.32f), 0.36f);
            BiomeCutoutMaterial("Hills Scrub", pack, "T_desert_bush_D", "T_desert_bush_N",
                new Color(0.44f, 0.50f, 0.34f), 0.36f);
            BiomeCutoutMaterial("Hills Plant", pack, "T_desert_plant_D", "T_desert_plant_N",
                new Color(0.42f, 0.52f, 0.32f), 0.34f);
            BiomeCutoutMaterial("Hills Groundcover", pack, "T_vetegation_atlas_basecolor",
                "T_vetegation_atlas_normal", new Color(0.44f, 0.52f, 0.34f), 0.34f);
            BiomeCutoutMaterial("Hills Wires", pack, "T_Pole_props_D", "T_Pole_props_N",
                new Color(0.35f, 0.35f, 0.36f), 0.4f);

            // Residential house windows: architectural tinted glass (clean and realistic)
            var residentialGlass = MakeMaterial("Hills Window Glass", new Color(0.06f, 0.09f, 0.14f), 0.85f, 0.96f);
            residentialGlass.SetFloat("_Smoothness", 0.96f);
            residentialGlass.SetFloat("_Metallic", 0.85f);
            materials["Hills Window Glass"] = residentialGlass;

            // Windows and distant blocks glow so the skyline still reads under haze.
            var windows = BiomeMaterial("Hills Windows", pack, "T_background_building_D",
                "T_background_building_N", new Color(0.78f, 0.84f, 0.92f), 0.35f, 0.68f,
                "T_background_building_Emission");
            windows.SetColor("_EmissionColor", new Color(0.9f, 0.95f, 1.1f));
            BiomeSurface(windows, pack, "T_background_building_MSO", 0.8f);

            // Canyon grass meshes reused as forest undergrowth, tinted green.
            BiomeCutoutMaterial("Forest Grass", "RedCanyon", "T_grass_D", "T_grass_N", new Color(0.52f, 0.78f, 0.36f), 0.36f);

            BuildLayaMaterials();
            BuildDecalMaterials();
            BuildSplatMaterials();
        }

        /// Materials for the two Laya kits. Both ship _E emissive maps, which is what
        /// makes these biomes readable at night without lighting every prop.
        private void BuildLayaMaterials()
        {
            // Every Cyberpunk surface drives roughness/metal/AO from its packed map rather
            // than a flat per-material constant - this is what separates wet concrete,
            // painted metal and glass instead of giving them one plastic finish.
            BiomeSurface(BiomeMaterial("Cyber Concrete", "CyberpunkCity", "T_concrete_building_D", "T_concrete_building_N",
                new Color(0.52f, 0.55f, 0.66f), 0.14f, 0.38f), "CyberpunkCity", "T_concrete_building_MSO");
            BiomeSurface(BiomeMaterial("Cyber Trim", "CyberpunkCity", "T_concrete_trim_01_D", "T_concrete_trim_01_N",
                new Color(0.44f, 0.47f, 0.58f), 0.42f, 0.46f), "CyberpunkCity", "T_concrete_trim_01_MSO");
            BiomeSurface(BiomeMaterial("Cyber Ground", "CyberpunkCity", "T_ground_texture_01_D", "T_ground_texture_01_N",
                new Color(0.20f, 0.21f, 0.26f), 0.08f, 0.34f), "CyberpunkCity", "T_ground_texture_01_MSO", 0.4f);
            var cyberFloor = BiomeSurface(BiomeMaterial("Cyber Floor", "CyberpunkCity", "T_concrete_floor_D", "T_concrete_floor_N",
                new Color(0.26f, 0.27f, 0.33f), 0.1f, 0.4f), "CyberpunkCity", "T_concrete_floor_MSO", 0.4f);
            cyberFloor.mainTextureScale = new Vector2(10f, 46f);
            BiomeSurface(BiomeMaterial("Cyber Billboard", "CyberpunkCity", "T_billboard_D", "T_billboard_N",
                Color.white, 0.1f, 0.5f, "T_billboard_E"), "CyberpunkCity", "T_billboard_MSO");
            BiomeSurface(BiomeMaterial("Cyber Billboard B", "CyberpunkCity", "T_billboard_02_D", "T_billboard_02_N",
                Color.white, 0.1f, 0.5f, "T_billboard_02_E"), "CyberpunkCity", "T_billboard_02_MSO");
            BiomeSurface(BiomeMaterial("Cyber Props", "CyberpunkCity", "T_street_props_D", "T_street_props_N",
                new Color(0.72f, 0.75f, 0.8f), 0.38f, 0.42f, "T_street_props_E"), "CyberpunkCity", "T_street_props_MSO");
            BiomeSurface(BiomeMaterial("Cyber Props B", "CyberpunkCity", "T_street_props_02_D", "T_street_props_02_N",
                new Color(0.72f, 0.75f, 0.8f), 0.38f, 0.42f, "T_street_props_02_E"), "CyberpunkCity", "T_street_props_02_MSO");
            BiomeSurface(BiomeMaterial("Cyber Car", "CyberpunkCity", "T_flying_car_D", "T_flying_car_N",
                Color.white, 0.6f, 0.72f, "T_flying_car_E"), "CyberpunkCity", "T_flying_car_MSO");
            BiomeSurface(BiomeMaterial("Cyber Car B", "CyberpunkCity", "T_car_flying_02_D", "T_car_flying_02_N",
                Color.white, 0.6f, 0.72f, "T_car_flying_02_E"), "CyberpunkCity", "T_car_flying_02_MSO");
            BiomeSurface(BiomeMaterial("Cyber Trash", "CyberpunkCity", "T_trash_bag_D", "T_trash_bag_N",
                new Color(0.5f, 0.52f, 0.55f), 0.05f, 0.3f), "CyberpunkCity", "T_trash_bag_MSO");
            BiomeSurface(BiomeMaterial("Cyber Crate", "CyberpunkCity", "T_Crate_D", "T_Crate_N",
                new Color(0.68f, 0.66f, 0.6f), 0.15f, 0.32f), "CyberpunkCity", "T_Crate_MSO");
            BiomeSurface(BiomeMaterial("Cyber Pipes", "CyberpunkCity", "T_pipes_D", "T_pipes_N",
                new Color(0.6f, 0.62f, 0.66f), 0.55f, 0.44f), "CyberpunkCity", "T_pipes_MSO");
            BiomeSurface(BiomeMaterial("Cyber Lamp", "CyberpunkCity", "T_Lamp_D", "T_Lamp_N",
                Color.white, 0.4f, 0.5f, "T_Lamp_E"), "CyberpunkCity", "T_Lamp_MSO");
            BiomeSurface(BiomeMaterial("Cyber Door", "CyberpunkCity", "T_door_D", "T_door_N",
                new Color(0.55f, 0.57f, 0.62f), 0.4f, 0.42f), "CyberpunkCity", "T_door_MSO");

            // Lit windows. The _EMISSION keyword on URP Lit does not survive player-build
            // variant stripping (verified: every "glowing" sign in captures is bright
            // albedo, not emission), so windows use URP/Unlit - anchored in Resources via
            // UnlitVariantAnchor - with the procedural pane grid as a full-bright base map.
            // Unlit ignores scene lights: panes glow at night, read as lit interiors by day.
            var windowGrid = WindowEmissionGrid();
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            var cyberWindow = new Material(unlitShader) { name = "Cyber Window" };
            cyberWindow.SetTexture("_BaseMap", windowGrid);
            cyberWindow.SetColor("_BaseColor", new Color(1.9f, 1.7f, 1.35f));
            cyberWindow.enableInstancing = true;
            materials["Cyber Window"] = cyberWindow;

            // Distant towers: the concrete albedo has painted-on dark windows and no
            // separate window submesh, so the body keeps a Lit concrete look while the
            // MI_window submeshes carry the unlit pane grid (see Cyber Window).
            var cyberSkyline = BiomeSurface(BiomeMaterial("Cyber Skyline", "CyberpunkCity",
                "T_concrete_building_D", "T_concrete_building_N",
                new Color(0.52f, 0.55f, 0.64f), 0.1f, 0.3f, null), "CyberpunkCity", "T_concrete_building_MSO");

            // Signage keeps more of its hue than architecture does - a sign is meant to
            // read as coloured light. It is the surfaces they spill onto that were the
            // problem, and those are handled by the mood and grade above.
            var hologram = MakeMaterial("Cyber Hologram", Desaturate(new Color(0.22f, 0.86f, 1f), 0.35f), 0.1f, 0.85f);
            hologram.SetColor("_EmissionColor", Desaturate(new Color(0.45f, 1.0f, 1.2f), 0.35f));
            hologram.EnableKeyword("_EMISSION");
            var neonStrip = MakeMaterial("Cyber Neon Strip", Desaturate(new Color(1f, 0.22f, 0.62f), 0.35f), 0.2f, 0.8f);
            neonStrip.SetColor("_EmissionColor", Desaturate(new Color(1.1f, 0.4f, 0.8f), 0.35f));
            neonStrip.EnableKeyword("_EMISSION");

            BiomeSurface(BiomeMaterial("Kowloon Building", "HongKong", "T_building_modules_D", "T_building_modules_N",
                new Color(0.62f, 0.60f, 0.58f), 0.12f, 0.36f, "T_building_modules_E"), "HongKong", "T_building_modules_MSO");
            // Brighter skyline variant for the horizon band - at 200-600 m the standard
            // building emission is fog-dimmed into a flat silhouette.
            var kowloonSkyline = BiomeSurface(BiomeMaterial("Kowloon Skyline", "HongKong", "T_building_modules_D",
                "T_building_modules_N", new Color(0.62f, 0.60f, 0.58f), 0.12f, 0.36f, "T_building_modules_E"),
                "HongKong", "T_building_modules_MSO");
            kowloonSkyline.SetColor("_EmissionColor", new Color(1.0f, 1.0f, 1.0f));
            BiomeSurface(BiomeMaterial("Kowloon Building B", "HongKong", "T_building_modules_02_D", "T_building_modules_02_N",
                new Color(0.60f, 0.58f, 0.56f), 0.12f, 0.36f, "T_building_modules_02_E"), "HongKong", "T_building_modules_02_MSO");
            var kowloonSign = BiomeSurface(BiomeMaterial("Kowloon Sign", "HongKong", "T_chinese_signs_D", "T_chinese_signs_N",
                Color.white, 0.15f, 0.55f, "T_chinese_signs_E"), "HongKong", "T_chinese_signs_MSO");
            kowloonSign.SetColor("_EmissionColor", new Color(1.1f, 1.0f, 1.0f));
            BiomeSurface(BiomeMaterial("Kowloon Food", "HongKong", "T_food_market_D", "T_food_market_N",
                new Color(0.82f, 0.78f, 0.7f), 0.18f, 0.4f), "HongKong", "T_food_market_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Produce", "HongKong", "T_vegatables_D", "T_vegatables_N",
                new Color(0.78f, 0.84f, 0.62f), 0.05f, 0.34f), "HongKong", "T_vegatables_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Market", "HongKong", "T_street_market_D", "T_street_market_N",
                new Color(0.74f, 0.70f, 0.64f), 0.2f, 0.36f), "HongKong", "T_street_market_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Market Detail", "HongKong", "T_market_detail_D", "T_market_detail_N",
                new Color(0.72f, 0.68f, 0.62f), 0.24f, 0.38f), "HongKong", "T_market_detail_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Street", "HongKong", "T_street_module_D", "T_street_module_N",
                new Color(0.58f, 0.56f, 0.55f), 0.14f, 0.34f), "HongKong", "T_street_module_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Street Detail", "HongKong", "T_detail_street_modules_D", "T_detail_street_modules_N",
                new Color(0.60f, 0.58f, 0.56f), 0.22f, 0.36f), "HongKong", "T_detail_street_modules_MSO");
            BiomeSurface(BiomeMaterial("Kowloon Props", "CyberpunkCity", "T_street_props_02_D", "T_street_props_02_N",
                new Color(0.68f, 0.70f, 0.72f), 0.35f, 0.4f, "T_street_props_02_E"), "CyberpunkCity", "T_street_props_02_MSO");
            var kowloonGround = BiomeSurface(BiomeMaterial("Kowloon Ground", "HongKong", "T_ground_texture_02_D", "T_ground_texture_02_N",
                new Color(0.28f, 0.27f, 0.26f), 0.08f, 0.32f), "HongKong", "T_ground_texture_02_MSO", 0.45f);
            kowloonGround.mainTextureScale = new Vector2(12f, 52f);
        }

        /// Road Rage ships on iOS, so the desktop-grade settings need a mobile fallback.
        /// Texture size is handled at import time (BiomeTextureImporter); these are the
        /// parts that can still be dialled back at runtime.
        
        /// One budget flag every added system checks before spending.
        ///
        /// PRODUCTION-GATES records Greenwood at 31 FPS on a desktop GPU and under 1 FPS
        /// on a Helio G85, with a stop-work rule at 30 FPS on desktop. Anything added
        /// after that has to be able to switch itself off rather than assume headroom
        /// that measurably is not there.
        public static bool RichDetailBudget { get; private set; } = true;

        /// Overrides the platform test so the mobile budget can be exercised on desktop.
        /// Set by -lowdetail, or from the editor via ForceLowDetailBudget before the world
        /// builds. Null means "decide from the platform", which is the shipping behaviour.
        private static bool? lowDetailOverride;

#if UNITY_EDITOR
        /// Backing store for the override. A plain static is not enough: entering play mode
        /// wipes statics when domain reload is on, and the SubsystemRegistration reset below
        /// clears them when it is off - so an override set before pressing Play was lost
        /// either way, and the run measured the very path it was meant to replace. The two
        /// identical GREENWOOD readings (5402 renderers, 4886 cutout, both times) were that
        /// bug, not a budget that does nothing. An editor pref outlives both.
        private const string LowDetailPrefKey = "RoadRage.ForceLowDetailBudget";

        /// Lets the editor menu read the same pref the bootstrap writes. The key is a
        /// private const here, so a menu item that hardcoded the string again would be a
        /// second copy free to drift - and a drifted copy shows the wrong budget in the
        /// menu while the world builds on the other one, which is the exact failure this
        /// pref exists to prevent.
        public static bool LowDetailBudgetForced =>
            UnityEditor.EditorPrefs.GetBool(LowDetailPrefKey, false);
#endif

        /// The budget path keyed purely off Application.isMobilePlatform, so the only way
        /// to see what the thinning actually removes was to produce an Android build and
        /// install it - rung 6 of the test ladder for a question rung 2 can answer. This
        /// forces the same budget on desktop so the removal can be read off RR_COST.
        ///
        /// It does NOT emulate the device GPU: a desktop frame rate under this budget says
        /// nothing about a Helio G85. What it does prove is how much geometry the budget
        /// actually takes out, which is the part that was never measured.
        ///
        /// Safe to call before OR after entering play mode. Chunks bake scatter density at
        /// build time, so the world still has to rebuild for it to show up in RR_COST.
        public static void ForceLowDetailBudget(bool low)
        {
            lowDetailOverride = low;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetBool(LowDetailPrefKey, low);
#endif
            ApplyPlatformQuality();
            Debug.Log($"RR_QUALITY forced budget: RichDetailBudget={RichDetailBudget} " +
                      "(rebuild the world for scatter density to take effect)");
        }

        /// Drops back to deciding from the platform. Worth calling explicitly once a
        /// forced measurement is done, since the pref otherwise persists across editor
        /// sessions and every later reading would silently be off the shipping path.
        public static void ClearDetailBudgetOverride()
        {
            lowDetailOverride = null;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.DeleteKey(LowDetailPrefKey);
#endif
            ApplyPlatformQuality();
            Debug.Log($"RR_QUALITY budget override cleared: RichDetailBudget={RichDetailBudget}");
        }

        private static void ApplyPlatformQuality()
        {
            var lowDetail = lowDetailOverride ?? (QualitySettings.GetQualityLevel() <= 1);
            RichDetailBudget = !lowDetail;

            // Shadow distance and cascades were the only shadow settings the low branch set,
            // and it never set QualitySettings.shadows at all - so the level it ran on decided,
            // and the level it ran on is "Very Low", which ships with shadows Disable. The log
            // said "70m 1-cascade" while the renderer drew no shadows whatsoever. Both
            // branches now own every shadow value they depend on.
            if (!lowDetail)
            {
                QualitySettings.shadows = UnityEngine.ShadowQuality.All;
                // Mirrors the URP assets, which are what URP actually reads. 160 m in
                // four cascades drew every shadow caster up to four times; in the forest
                // that was most of the frame's draw calls.
                QualitySettings.shadowDistance = 120f;
                QualitySettings.shadowCascades = 2;
                QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
            }
            else
            {
                QualitySettings.shadows = UnityEngine.ShadowQuality.HardOnly;
                QualitySettings.shadowDistance = 45f;
                QualitySettings.shadowCascades = 1;
                QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Low;
            }

            QualitySettings.globalTextureMipmapLimit = 0;
            // Only cap on a real handset. Capping a forced-low desktop run would clamp the
            // frame rate to 60 and hide exactly the headroom the run is measuring.
            if (lowDetail && Application.isMobilePlatform) Application.targetFrameRate = 60;

            // Applied last so it holds on either tier: -noshadows exists to isolate the cost
            // of the shadows this method just turned on for the low tier.
            if (ShadowsDisabled) QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;

            Debug.Log($"RR_QUALITY {QualityPipeline.TierName} tier: {QualitySettings.shadows}, " +
                      $"{QualitySettings.shadowDistance:0}m {QualitySettings.shadowCascades}-cascade shadows, " +
                      $"budget={(RichDetailBudget ? "rich" : "low")} " +
                      $"(forced={lowDetailOverride == true && !Application.isMobilePlatform}, " +
                      $"level={QualitySettings.GetQualityLevel()}, noshadows={ShadowsDisabled})");
        }

        /// Wet asphalt is mostly a smoothness trick: raise road/shoulder gloss so the
        /// probe's reflection reads, and darken the albedo the way real water does.
        private readonly Dictionary<string, Color> dryRoadColors = new();
        private readonly Dictionary<string, float> dryRoadSmoothness = new();

        private static readonly string[] WetRoadMaterialNames =
        {
                      "Road",
                      "Forest Road",
                      "Shoulder"
        };

        private void ApplyRoadWetness(float wetness)
        {
            wetness = Mathf.Clamp01(wetness);

            foreach (var name in WetRoadMaterialNames)
            {
                if (!materials.TryGetValue(name, out var material)
                    || material == null
                    || !material.HasProperty("_BaseColor")
                    || !material.HasProperty("_Smoothness"))
                {
                    continue;
                }

                if (!dryRoadColors.TryGetValue(name, out var dryColor))
                {
                    dryColor = material.GetColor("_BaseColor");
                    dryRoadColors[name] = dryColor;
                }

                if (!dryRoadSmoothness.TryGetValue(name, out var drySmoothness))
                {
                    drySmoothness = material.GetFloat("_Smoothness");
                    dryRoadSmoothness[name] = drySmoothness;
                }

                var wetColor = new Color(
                    dryColor.r * 0.82f,
                    dryColor.g * 0.82f,
                    dryColor.b * 0.82f,
                    dryColor.a);

                material.SetColor(
                    "_BaseColor",
                    Color.Lerp(dryColor, wetColor, wetness));

                material.SetFloat(
                    "_Smoothness",
                    Mathf.Lerp(
                        drySmoothness,
                        Mathf.Max(drySmoothness, 0.40f),
                        wetness));
            }
        }


        /// Creates the player's local reflection probe.
        ///
        /// This method had an empty body while six other sites configured a probe that was
        /// never created: the field stayed null, ReflectionProbeDriver was handed that null,
        /// and its LateUpdate returned on the first line. Meanwhile the road, both shoulders
        /// and every city asphalt renderer ask for probe reflections through
        /// EnableProbeReflections. Wet asphalt reflecting neon is the strongest cue that a
        /// night street is a place rather than a texture, and none of it existed.
        ///
        /// refreshMode is ViaScripting so the capture schedule lives in
        /// ReflectionProbeDriver and nowhere else. The per-biome blocks used to set
        /// EveryFrame, which is six full cubemap captures every frame - on the tier with the
        /// least headroom, and on top of the driver's own calls.
        private void BuildReflectionProbe()
        {
            if (!ReflectionsEnabled)
            {
                Debug.Log("RR_REFLECT disabled by -noreflections");
                return;
            }

            // BuildLighting runs again on every biome switch, so the previous probe has to
            // go: Unity's real-null check makes a destroyed probe read as null, but a live
            // one left behind would keep a stale capture and a second driver.
            if (reflectionProbe != null) Destroy(reflectionProbe.gameObject);

            var probeObject = new GameObject("Road Rage Reflection Probe");
            var probe = probeObject.AddComponent<ReflectionProbe>();
            // Quality level 0 is this project's Android default and it ships with
            // realtimeReflectionProbes off, so without this the probe renders nothing on the
            // one platform where it was least affordable to get wrong - and it fails
            // silently, because RenderProbe() on a disabled tier is not an error.
            if (!QualitySettings.realtimeReflectionProbes)
            {
                QualitySettings.realtimeReflectionProbes = true;
                Debug.Log($"RR_REFLECT re-enabled realtime reflection probes " +
                          $"(quality level {QualitySettings.GetQualityLevel()} had them off)");
            }
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            // Sized for the road corridor, not the skyline: the surfaces that need a
            // reflection are asphalt, kerbs, glass and car paint within ~30 m of the player.
            probe.resolution = RichDetailBudget ? 256 : 64;
            probe.size = RichDetailBudget ? new Vector3(72f, 28f, 72f) : new Vector3(40f, 16f, 40f);
            probe.blendDistance = RichDetailBudget ? 4f : 2f;
            probe.intensity = DefaultProbeIntensity;
            reflectionProbe = probe;

            Debug.Log($"RR_REFLECT probe built: {probe.resolution}px size={probe.size} " +
                      $"blend={probe.blendDistance} budget={(RichDetailBudget ? "rich" : "low")} " +
                      $"schedule={(RichDetailBudget ? "interval" : "travel")}");
        }

        /// Per-biome probe tuning.
        ///
        /// Size and intensity are art choices and stay with the biome that made them.
        /// Resolution and the capture schedule are the driver's, because those are the ones
        /// with a frame cost and they have to differ per tier. A negative blendDistance
        /// means "whatever the builder chose", which is what the sites that never set it
        /// used to get by doing nothing.
        private void TuneReflectionProbe(float intensity, Vector3 size, float blendDistance = -1f)
        {
            if (reflectionProbe == null) return;
            reflectionProbe.intensity = intensity;
            reflectionProbe.size = size;
            if (blendDistance >= 0f) reflectionProbe.blendDistance = blendDistance;
        }

        private void BuildLighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            var mood = Mood();
            // Weather modifies the biome's own palette rather than replacing it, so a
            // rainy Kowloon still looks like Kowloon.
            var weather = WeatherSystem.EffectFor(activeWeather);
            RenderSettings.fogDensity = mood.FogDensity * weather.FogDensityScale;
            RenderSettings.fogColor = Color.Lerp(mood.Fog, weather.FogTint, weather.FogTintAmount);
            // Trilight ambient is a large share of what the eye reads as "scene
            // brightness", and the moods were authored before the post pipeline existed.
            // The gain is applied to the colours rather than through
            // RenderSettings.ambientIntensity, which Unity ignores outside Skybox mode.
            RenderSettings.ambientSkyColor = ScaleRgb(
                Color.Lerp(mood.Sky, weather.FogTint, weather.FogTintAmount * 0.6f), mood.AmbientIntensity * AmbientTrim);
            RenderSettings.ambientEquatorColor = ScaleRgb(
                Color.Lerp(mood.Equator, weather.FogTint, weather.FogTintAmount * 0.4f), mood.AmbientIntensity * AmbientTrim);
            RenderSettings.ambientGroundColor = ScaleRgb(mood.Ground, mood.AmbientIntensity * AmbientTrim);
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;

            ApplyRoadWetness(Mathf.Clamp01(mood.RoadWetness + weather.WetnessAdd));
            ApplyPlatformQuality();
            // After ApplyPlatformQuality: RichDetailBudget is what decides the probe's
            // resolution and its capture schedule, and it is only settled once the tier has
            // been applied. BuildCamera runs later and parents this probe to the chase
            // camera, so the object has to exist by then.
            BuildReflectionProbe();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sunLight = sun;
            sun.type = LightType.Directional;
            sun.flare = null;
            sun.color = mood.SunColor;
            sun.intensity = mood.SunIntensity * weather.SunScale;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.transform.rotation = Quaternion.Euler(54f, 32f, 0f);
            RenderSettings.sun = sun;

			var volume = new GameObject($"{biomeName} Post Processing").AddComponent<Volume>();
			volume.isGlobal = true;
			volume.priority = 10f;
			volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
			// Bloom was switched off and left off. The mood struct carried
			// BloomIntensity/BloomThreshold the whole time and BlendZoneLighting was already
			// lerping both, so the only things missing were a handle and a non-zero default.
			// Without it an emissive sign reads as a bright texture; with it, as a light
			// source - which is most of the difference between a night city that looks
			// rendered and one that looks photographed.
			// Held as a field for the same reason the grade and the tonemapper are: a zone
			// crossing has to be able to move it, or the start biome's bloom follows the
			// player for the whole run.
			zoneBloom = volume.profile.Add<Bloom>();
			ApplyBloom(mood);
			// Without tonemapping every HDR highlight clips flat, which is a large part
			// of the "plastic toy" read. ACES gives filmic rolloff on the bright end.
			// ACES rolls the highlights off filmically but it also shifts hue and lifts
			// saturation, which is a large part of the over-cooked look. Neutral does the
			// range remap only and leaves the grading to ColorAdjustments below, so it is
			// what every mood asks for unless one sets TonemapAces to 1.
			// Held as a field, not a local: without a handle the per-frame zone blend cannot
			// move the grade, and a crossing kept the start biome's look.
			zoneTonemap = volume.profile.Add<Tonemapping>();
			zoneTonemap.mode.Override(mood.TonemapAces == 1 ? TonemappingMode.ACES : TonemappingMode.Neutral);

			// Slight motion blur sells speed and hides the low-poly silhouettes.
			var motionBlur = volume.profile.Add<MotionBlur>();
			motionBlur.intensity.Override(0.18f);
			motionBlur.clamp.Override(0.04f);

			zoneGrading = volume.profile.Add<ColorAdjustments>();
			zoneGrading.postExposure.Override(
				mood.PostExposure + weather.ExposureAdd + NeutralToneCompensation + ExposureTrim);
			// Contrast and saturation were stacked on top of an ACES curve that already
			// pushes both, so every biome graded out as a poster. Neutral tonemapping
			// leaves hue and saturation alone, and the grade now only takes colour away.
			// Contrast is what separates a facade from the sky at night, and unlike the
			// tonemapper it adds no saturation, so it can carry the punch ACES used to.
			// A mood that sets its own value wins; zero means "use the branch default".
			zoneGrading.contrast.Override(mood.Contrast != 0f ? mood.Contrast : 14f);
			zoneGrading.saturation.Override(mood.Saturation != 0f ? mood.Saturation : GradeSaturation);
			var vignette = volume.profile.Add<Vignette>();
			vignette.intensity.Override(0.15f);
			vignette.smoothness.Override(0.68f);

        }

        /// Applies a mood's bloom. Called once at build and again every frame from
        /// BlendZoneLighting, so the bloom follows a zone crossing like the grade does.
        ///
        /// A mood that sets BloomIntensity to 0 means "use the branch default", matching how
        /// Saturation and Contrast already treat zero. The mobile default is roughly half:
        /// the mip chain is a whole-screen cost, and the stylized-neon read survives at the
        /// lower intensity. The threshold stays with the mood - 5.0 in every biome means
        /// only genuinely emissive surfaces bloom, which is the conservative reading and the
        /// one that does not turn wet asphalt into a light source.
        private void ApplyBloom(BiomeMood mood)
        {
            if (zoneBloom == null) return;
            var intensity = mood.BloomIntensity != 0f
                ? mood.BloomIntensity
                : RichDetailBudget ? DefaultBloomIntensity : MobileBloomIntensity;
            zoneBloom.intensity.Override(intensity);
            zoneBloom.threshold.Override(mood.BloomThreshold > 0f ? mood.BloomThreshold : DefaultBloomThreshold);
            // High-quality filtering is a second upsample pass per mip; the low tier keeps
            // the cheaper one.
            zoneBloom.highQualityFiltering.Override(RichDetailBudget);
            zoneBloom.active = !BloomDisabled;
        }

        private struct BiomeMood
        {
            public float FogDensity;
            public Color Fog;
            public Color Sky;
            public Color Equator;
            public Color Ground;
            public Color SunColor;
            public float SunIntensity;
            public float PostExposure;
            public float BloomIntensity;
            public float BloomThreshold;
            /// 0 = dry asphalt, 1 = soaked. Drives road smoothness so the reflection
            /// probe's captured neon actually shows up in the street.
            public float RoadWetness;
            /// Multiplier on the ambient trilight. Lives on the mood so the per-frame
            /// blend owns it too - it used to be set once by the city photoreal pass and
            /// then left to drift out of step with the ambient colours around it.
            public float AmbientIntensity;
            /// URP ColorAdjustments saturation (-100 to 100). 0 means "use the branch
            /// default", GradeSaturation, rather than a literal zero.
            public float Saturation;
            /// URP ColorAdjustments contrast (-100 to 100). 0 means "use the default".
            public float Contrast;
            /// Tonemapper for this biome: 0 leaves the branch default of Neutral, 1 asks
            /// for ACES. Neutral is the default because ACES shifts hue and lifts
            /// saturation, which was a large part of the over-cooked look; a biome that
            /// wants the filmic shoulder back can opt in rather than everything getting it.
            public int TonemapAces;
        }

        /// Manhattan ships two moods and neither is wrong, so the biome carries both.
        ///
        /// The night relight fixed a real defect: an ambient sky of 0.12 luma over a 0.05
        /// ground bounce meant every facade outside the sun's reach fell to black. The
        /// daylight mood answers a different question - what the biome should look like -
        /// and that is a call for whoever is looking at it, not for whoever last edited
        /// the file. Keeping both is cheaper than relitigating it.
        ///
        /// Night is the default because the window emission, the street lamps, the neon
        /// and the baked grime were all tuned against it. In daylight the lit-window
        /// pattern and the neon will read as much weaker; that is expected, not a bug.
        ///
        /// -manhattan=day on the command line, or SetManhattanDaylight(true) from the
        /// editor, which persists so it survives entering play mode.
#if UNITY_EDITOR
        private const string ManhattanDaylightPrefKey = "RoadRage.ManhattanDaylight";
#endif
        private static bool? manhattanDaylightOverride;

        private static bool ManhattanDaylight
        {
            get
            {
                if (manhattanDaylightOverride.HasValue) return manhattanDaylightOverride.Value;
#if UNITY_EDITOR
                if (UnityEditor.EditorPrefs.HasKey(ManhattanDaylightPrefKey))
                    return UnityEditor.EditorPrefs.GetBool(ManhattanDaylightPrefKey);
#endif
                return string.Equals(CommandLineValue("-manhattan="), "day",
                                     System.StringComparison.OrdinalIgnoreCase);
            }
        }

        /// Read-only view for the editor menu's checkmark. Goes through ManhattanDaylight
        /// rather than the pref, so the menu shows what the world will actually build
        /// with - including a -manhattan=day command line and an in-session override,
        /// neither of which is in EditorPrefs.
        public static bool ManhattanIsDaylight => ManhattanDaylight;

        /// Switches Manhattan between its two moods. The world has to rebuild for the
        /// change to show, the same as the detail budget.
        public static void SetManhattanDaylight(bool daylight)
        {
            manhattanDaylightOverride = daylight;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetBool(ManhattanDaylightPrefKey, daylight);
#endif
            Debug.Log($"RR_MOOD Manhattan is now {(daylight ? "daylight" : "night")} " +
                      "(re-enter play mode for the world to rebuild)");
        }

        private static BiomeMood ManhattanNightMood() =>
            new BiomeMood // MANHATTAN - wet night, but a lit one
            {
                // Was an ambient sky of 0.12 luma over a 0.05 ground bounce with a 0.9
                // sun: outside the directional light's reach every facade fell to black,
                // so the buildings read as silhouettes with no surface at all. A night
                // city is not an unlit one - the light comes off windows, wet asphalt
                // and sky glow, and that is ambient, not the key. Still blue, still
                // night, but the geometry is now visible.
                // Raising the ambient 4x also multiplied its blue tint, so the cast came
                // back louder than before even though the mood is desaturated on the way
                // out. At this brightness the hue has to be neutral at source - a cool
                // hint, not a wash.
                FogDensity = 0.0055f, Fog = new Color(0.19f, 0.20f, 0.22f),
                Sky = new Color(0.40f, 0.42f, 0.46f), Equator = new Color(0.33f, 0.35f, 0.38f),
                // Ground bounce was under half the sky, which is backwards for this street.
                // A dry field at night bounces almost nothing, so a low ground term is
                // right there; wet asphalt under a lit city throws a lot back up, and it
                // lands on the bottom few storeys - the only part of a facade a driver
                // ever looks at. Raising it lifts building bases without touching the sky,
                // so the night stays a night. One number if it wants tuning.
                Ground = new Color(0.30f, 0.31f, 0.33f), SunColor = new Color(0.94f, 0.96f, 1f),
                SunIntensity = 1.45f, PostExposure = 0.22f, BloomIntensity = 0f, BloomThreshold = 5f,
                RoadWetness = 0.62f, AmbientIntensity = 1.35f
            };

        private static BiomeMood ManhattanDayMood() =>
            new BiomeMood // MANHATTAN - overcast hazy NYC daylight, desaturated concrete tones
            {
                FogDensity = 0.0018f, Fog = new Color(0.64f, 0.65f, 0.68f),
                Sky = new Color(0.52f, 0.56f, 0.62f), Equator = new Color(0.54f, 0.56f, 0.58f),
                Ground = new Color(0.28f, 0.28f, 0.29f), SunColor = new Color(1f, 0.98f, 0.95f),
                SunIntensity = 1.30f, PostExposure = 0.10f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.12f,
                // Was -75 saturation plus Neutral tonemapping plus the old -20 global
                // offset - effectively grayscale. Brought in line with the other muted
                // biomes (-20 to -24) and back to ACES so it still reads as a colour city.
                Saturation = -22f
            };

        /// How far every biome palette is pulled towards its own luminance. The moods
        /// were authored as near-pure hues (a violet Neon City sky at 0.32/0.16/0.52, a
        /// bottle-green sewer, a magenta Alien Biomass) and those colours multiply into
        /// ambient, fog and the sun, so every surface in the zone inherited the cast.
        /// Pulling them most of the way to neutral keeps each biome's identity readable
        /// while taking the poster-paint saturation out of the frame.
        private const float MoodDesaturation = 0.45f;
        /// Ground bounce carries the strongest cast because it lights the underside of
        /// every car, so it loses slightly more than the sky does.
        private const float GroundDesaturation = 0.55f;
        /// Final global trim in the colour grade, on top of the neutral tonemapper.
        private const float GradeSaturation = -10f;

        /// Bloom defaults for a mood that sets 0, i.e. "use the branch default".
        /// Emissive-only at this threshold, so it costs highlight pixels rather than the
        /// whole frame. Bedrock value for the stylized-real target; the per-biome knob is
        /// BiomeMood.BloomIntensity for anything that wants a heavier neon city.
        private const float DefaultBloomIntensity = 0.55f;
        /// Roughly half, for the tier that is not making its frame budget.
        private const float MobileBloomIntensity = 0.28f;
        private const float DefaultBloomThreshold = 1.1f;
        /// Intensity the probe is built with before any biome tunes it.
        private const float DefaultProbeIntensity = 1f;

        /// Neutral tonemapping is a plain range remap where ACES applied a filmic
        /// S-curve, so swapping to it removed the midtone lift and the shoulder punch
        /// ACES was quietly providing. On a night biome that reads as "dark and flat".
        /// This puts the brightness back without putting the colour cast back.
        private const float NeutralToneCompensation = 0.45f;

        /// Ambient floor, in luma. Manhattan runs an ambient sky of 0.12 and a ground
        /// bounce of 0.05, so anything the directional sun does not hit falls to black
        /// and the facades lose all their detail. Lifting the floor keeps the biome's
        /// hue and its darkness while letting shadowed surfaces still read.
        private const float MinAmbientLuma = 0.22f;

        /// Settled values. These were dialled in play mode against the actual frame
        /// with a pair of trim keys; the keys are gone now that the numbers are known.
        private const float AmbientTrim = 1f;
        private const float ExposureTrim = 0f;

        /// Scales RGB only. Multiplying a Color by a float also scales alpha, which is
        /// meaningless for an ambient colour and shows up as a stray 1.35 in the logs.
        private static Color ScaleRgb(Color value, float gain) =>
            new Color(value.r * gain, value.g * gain, value.b * gain, value.a);

        /// Raises a colour to a minimum luma without changing its hue.
        private static Color LiftToFloor(Color value, float floor)
        {
            var luma = value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;
            if (luma >= floor) return value;
            // Lerp towards white rather than scaling, so a near-black colour lifts to a
            // readable grey instead of amplifying whatever tint it happened to have.
            var t = Mathf.InverseLerp(luma, 1f, floor);
            return Color.Lerp(value, Color.white, t);
        }

        /// Rec.709 luma. Lerping a colour towards its own luma desaturates it without
        /// changing brightness, so a desaturated mood keeps the exposure it was tuned at.
        private static Color Desaturate(Color value, float amount)
        {
            var luma = value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;
            return new Color(
                Mathf.Lerp(value.r, luma, amount),
                Mathf.Lerp(value.g, luma, amount),
                Mathf.Lerp(value.b, luma, amount),
                value.a);
        }

        /// Applied to every mood on the way out, so BuildLighting, BlendZoneLighting and
        /// the camera clear colour all share one definition of how saturated a zone is.
        private static BiomeMood Neutralize(BiomeMood mood)
        {
            if (mood.AmbientIntensity <= 0f) mood.AmbientIntensity = 1f;
            mood.Fog = Desaturate(mood.Fog, MoodDesaturation);
            mood.Sky = LiftToFloor(Desaturate(mood.Sky, MoodDesaturation), MinAmbientLuma);
            mood.Equator = LiftToFloor(Desaturate(mood.Equator, MoodDesaturation), MinAmbientLuma);
            mood.Ground = LiftToFloor(Desaturate(mood.Ground, GroundDesaturation), MinAmbientLuma * 0.7f);
            // Key light keeps more of its warmth than the ambient does - a fully neutral
            // sun flattens the shading, and a tinted key reads as time of day, not as
            // a colour filter over the whole frame.
            mood.SunColor = Desaturate(mood.SunColor, MoodDesaturation * 0.55f);
            return mood;
        }

        private BiomeMood Mood() => Mood(System.Array.IndexOf(Biomes, biomeName));

        private BiomeMood Mood(int biomeIndex) =>
            biomeIndex == 0 && RoadPath.Route != null
                ? WithDayTime(Neutralize(RawMood(biomeIndex)), dayTime)
                : Neutralize(RawMood(biomeIndex));

        // ------------------------------------------------------------ varied runs

        /// Every run on the B500 rolls a time of day and the weather, so the same 46 km
        /// of forest is a misty morning one run, a golden evening the next, dusk with the
        /// headlights on after that. -daytime=morning|midday|evening|dusk forces it;
        /// -weather= still forces the weather.
        public void RollRunConditions()
        {
            if (RoadPath.Route == null)
            {
                SkyHorizonSync.Tint = Color.white;
                SetHeadlights(false);
                return;
            }
            var forced = CommandLineValue("-daytime=");
            if (!string.IsNullOrEmpty(forced) && System.Enum.TryParse(forced, true, out DayTime parsed))
            {
                dayTime = parsed;
            }
            else
            {
                var roll = Random.value;
                dayTime = roll < 0.25f ? DayTime.Morning : roll < 0.55f ? DayTime.Midday
                    : roll < 0.80f ? DayTime.Evening : DayTime.Dusk;
            }
            var forcedWeather = ParseWeather(CommandLineValue("-weather="));
            activeWeather = forcedWeather ?? WeatherSystem.Roll(0);
            // Fog in the blue hour leaves a dark grey wall where the forest and the ridge
            // should be. Fog banks on the B500 are a daytime look: move the run to morning
            // or midday rather than drop the fog.
            if (activeWeather == WeatherKind.Fog && dayTime == DayTime.Dusk && string.IsNullOrEmpty(forced))
                dayTime = Random.value < 0.5f ? DayTime.Morning : DayTime.Midday;
            if (weatherSystem != null && car != null)
            {
                var particleMaterial = Resources.Load<Material>("WeatherParticle");
                if (particleMaterial != null) weatherSystem.Configure(activeWeather, car, particleMaterial);
            }

            // Low sun in the morning and evening: long shadows across the road.
            if (sunLight != null)
            {
                sunLight.transform.rotation = dayTime switch
                {
                    DayTime.Morning => Quaternion.Euler(16f, 75f, 0f),
                    DayTime.Evening => Quaternion.Euler(11f, -105f, 0f),
                    DayTime.Dusk => Quaternion.Euler(5f, -125f, 0f),
                    _ => Quaternion.Euler(54f, 32f, 0f),
                };
                sunLight.shadowStrength = dayTime == DayTime.Dusk ? 0.4f : 0.82f;
            }
            SkyHorizonSync.Tint = dayTime switch
            {
                DayTime.Morning => new Color(1.0f, 0.93f, 0.86f),
                DayTime.Evening => new Color(1.0f, 0.74f, 0.52f),
                DayTime.Dusk => new Color(0.30f, 0.32f, 0.48f),
                _ => Color.white,
            };
            SetHeadlights(dayTime == DayTime.Dusk || activeWeather == WeatherKind.Fog);
            Debug.Log($"RR_EVENT conditions daytime={dayTime} weather={activeWeather}");
            StartCoroutine(ReportSceneBudgetSoon(++budgetRun));
        }

        /// What the frame is made of, by kind of object: renderers visible to a camera
        /// or a shadow map, their draw calls (submeshes) and triangles. Logged once per
        /// run, a few seconds in, so a slow biome can be traced without the Profiler.
        private int budgetRun;

        /// Reports for the latest run only: a run restarted within the wait skips it.
        private System.Collections.IEnumerator ReportSceneBudgetSoon(int run)
        {
            yield return new WaitForSeconds(8f);
            if (run == budgetRun) ReportSceneBudget();
        }

        private void ReportSceneBudget()
        {
            var groups = new Dictionary<string, (int renderers, int draws, long triangles)>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.isVisible || !r.TryGetComponent<MeshFilter>(out var f) || f.sharedMesh == null)
                    continue;
                // Named after the object directly under its chunk (a tree, a bush, the
                // cliff), or after its root when it is not part of a chunk.
                var t = r.transform;
                while (t.parent != null && !t.parent.name.StartsWith("Chunk ")) t = t.parent;
                var key = t.name;
                var mesh = f.sharedMesh;
                long triangles = 0;
                for (var m = 0; m < mesh.subMeshCount; m++) triangles += mesh.GetIndexCount(m) / 3;
                groups.TryGetValue(key, out var g);
                groups[key] = (g.renderers + 1, g.draws + mesh.subMeshCount, g.triangles + triangles);
            }
            var lines = new List<string>();
            foreach (var pair in groups)
                lines.Add($"{pair.Value.triangles / 1000,8}k tris {pair.Value.draws,6} draws {pair.Value.renderers,6} x {pair.Key}");
            lines.Sort((a, b) => string.CompareOrdinal(b, a));
            Debug.Log("RR_BUDGET visible objects by kind (triangles, draw calls, renderers):\n" +
                      string.Join("\n", lines.GetRange(0, Mathf.Min(15, lines.Count))));
        }

        /// The time of day laid over Greenwood's own palette.
        private static BiomeMood WithDayTime(BiomeMood mood, DayTime t)
        {
            switch (t)
            {
                case DayTime.Morning:
                    // Mist in the valleys, a pale warm sun.
                    mood.FogDensity *= 1.45f;
                    mood.Fog = Color.Lerp(mood.Fog, new Color(0.62f, 0.64f, 0.64f), 0.55f);
                    mood.SunColor = new Color(1f, 0.88f, 0.74f);
                    mood.SunIntensity *= 0.85f;
                    mood.Sky = Color.Lerp(mood.Sky, new Color(0.62f, 0.60f, 0.58f), 0.3f);
                    break;
                case DayTime.Evening:
                    // Golden hour: orange sun, warm haze, cooler shade.
                    mood.Fog = Color.Lerp(mood.Fog, new Color(0.62f, 0.48f, 0.34f), 0.6f);
                    mood.SunColor = new Color(1f, 0.66f, 0.38f);
                    mood.SunIntensity *= 1.05f;
                    mood.Sky = Color.Lerp(mood.Sky, new Color(0.46f, 0.44f, 0.52f), 0.4f);
                    mood.Ground = Color.Lerp(mood.Ground, new Color(0.12f, 0.08f, 0.05f), 0.4f);
                    mood.AmbientIntensity *= 0.85f;
                    break;
                case DayTime.Dusk:
                    // Blue hour: the sun is down, headlights on.
                    mood.Fog = new Color(0.13f, 0.15f, 0.22f);
                    mood.FogDensity *= 1.2f;
                    mood.SunColor = new Color(0.55f, 0.55f, 0.85f);
                    mood.SunIntensity *= 0.28f;
                    mood.Sky = new Color(0.20f, 0.23f, 0.34f);
                    mood.Equator = new Color(0.10f, 0.12f, 0.18f);
                    mood.Ground = new Color(0.04f, 0.045f, 0.06f);
                    mood.AmbientIntensity *= 0.7f;
                    mood.PostExposure += 0.25f;
                    mood.BloomIntensity = 0.6f;
                    break;
            }
            return mood;
        }

        private Light headlightBeam;

        /// One spot light ahead of the player's car for dusk and fog; the road is
        /// the thing to see, and one light is cheap.
        private void SetHeadlights(bool on)
        {
            if (car == null) return;
            if (headlightBeam == null && on)
            {
                var holder = new GameObject("Headlight Beam");
                holder.transform.SetParent(car, false);
                holder.transform.localPosition = new Vector3(0f, 0.9f, 1.8f);
                holder.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
                headlightBeam = holder.AddComponent<Light>();
                headlightBeam.type = LightType.Spot;
                headlightBeam.color = new Color(1f, 0.95f, 0.85f);
                headlightBeam.range = 70f;
                headlightBeam.spotAngle = 62f;
                headlightBeam.innerSpotAngle = 30f;
                headlightBeam.intensity = 9f;
                headlightBeam.shadows = LightShadows.None;
            }
            if (headlightBeam != null) headlightBeam.enabled = on;
        }

        /// Authored palettes. Read these through Mood() so the neutral pass is never
        /// bypassed; this is only separate so the per-biome values stay editable.
        private static BiomeMood RawMood(int biomeIndex) => biomeIndex switch
        {
            1 => new BiomeMood // SNOW STATION
            {
                FogDensity = 0.0045f, Fog = new Color(0.62f, 0.75f, 0.86f),
                Sky = new Color(0.68f, 0.82f, 0.96f), Equator = new Color(0.48f, 0.60f, 0.72f),
                Ground = new Color(0.28f, 0.36f, 0.44f), SunColor = new Color(0.92f, 0.96f, 1f),
                SunIntensity = 1.18f, PostExposure = -0.18f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f,
                Saturation = -8f
            },
            2 => new BiomeMood // SEWER TUNNEL
            {
                FogDensity = 0.014f, Fog = new Color(0.05f, 0.10f, 0.08f),
                Sky = new Color(0.13f, 0.26f, 0.19f), Equator = new Color(0.10f, 0.20f, 0.14f),
                Ground = new Color(0.04f, 0.08f, 0.06f), SunColor = new Color(0.85f, 0.92f, 0.88f),
                SunIntensity = 0.62f, PostExposure = 0.34f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.55f,
                Saturation = -12f
            },
            3 => new BiomeMood // TIRE DISTRICT
            {
                FogDensity = 0.0065f, Fog = new Color(0.38f, 0.38f, 0.40f),
                Sky = new Color(0.52f, 0.56f, 0.62f), Equator = new Color(0.32f, 0.35f, 0.38f),
                Ground = new Color(0.14f, 0.14f, 0.14f), SunColor = new Color(0.95f, 0.95f, 0.95f),
                SunIntensity = 1.35f, PostExposure = 0.35f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.3f,
                Saturation = -14f
            },
            4 => new BiomeMood // ALIEN BIOMASS
            {
                FogDensity = 0.015f, Fog = new Color(0.13f, 0.05f, 0.17f),
                Sky = new Color(0.20f, 0.07f, 0.28f), Equator = new Color(0.09f, 0.20f, 0.14f),
                Ground = new Color(0.04f, 0.07f, 0.05f), SunColor = new Color(0.72f, 0.55f, 1f),
                SunIntensity = 0.95f, PostExposure = 0.30f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.22f,
                Saturation = -16f
            },
            5 => new BiomeMood // NEON CITY
            {
                // Neon night, not blackout: the first pass stacked a near-black fog
                // colour, dim ambient and wet-road darkening into a pitch-black read.
                FogDensity = 0.0035f, Fog = new Color(0.27f, 0.21f, 0.44f),
                Sky = new Color(0.46f, 0.31f, 0.70f), Equator = new Color(0.48f, 0.31f, 0.58f),
                Ground = new Color(0.23f, 0.15f, 0.35f), SunColor = new Color(0.88f, 0.74f, 1f),
                SunIntensity = 1.35f, PostExposure = 0.55f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.65f,
                Saturation = -12f
            },
            6 => new BiomeMood // RED CANYON
            {
                FogDensity = 0.0038f, Fog = new Color(0.68f, 0.65f, 0.62f),
                Sky = new Color(0.72f, 0.78f, 0.88f), Equator = new Color(0.55f, 0.48f, 0.42f),
                Ground = new Color(0.24f, 0.18f, 0.14f), SunColor = new Color(1f, 0.98f, 0.92f),
                // Was the single brightest biome (1.50 sun + 0.16 exposure, on top of the
                // global lift) - blown out under the noon desert sun. Pulled back to a
                // level closer to the rest of the roster.
                SunIntensity = 1.18f, PostExposure = -0.08f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f,
                Saturation = -24f
            },
            7 => new BiomeMood // BROOKLYN
            {
                FogDensity = 0.0075f, Fog = new Color(0.30f, 0.43f, 0.55f),
                Sky = new Color(0.41f, 0.60f, 0.78f), Equator = new Color(0.23f, 0.34f, 0.43f),
                Ground = new Color(0.20f, 0.22f, 0.20f), SunColor = new Color(0.98f, 0.98f, 0.95f),
                SunIntensity = 1.40f, PostExposure = 0.20f, BloomIntensity = 0f, BloomThreshold = 5f,
                RoadWetness = 0.08f, AmbientIntensity = 1.25f, Saturation = -24f
            },
            CanalTownIndex => new BiomeMood // CANAL TOWN - old quarter, warm haze over the water
            {
                FogDensity = 0.0055f, Fog = new Color(0.56f, 0.47f, 0.38f),
                Sky = new Color(0.56f, 0.52f, 0.50f), Equator = new Color(0.36f, 0.31f, 0.27f),
                Ground = new Color(0.16f, 0.13f, 0.10f), SunColor = new Color(1f, 0.82f, 0.60f),
                SunIntensity = 1.25f, PostExposure = 0.10f, BloomIntensity = 0.45f, BloomThreshold = 4f,
                RoadWetness = 0.35f, AmbientIntensity = 1.1f, Saturation = -10f
            },
            9 => new BiomeMood // HOLLYWOOD HILLS - Crisp California daylight with blue skies
            {
                FogDensity = 0.0015f, Fog = new Color(0.75f, 0.85f, 0.95f),
                Sky = new Color(0.60f, 0.78f, 0.98f), Equator = new Color(0.65f, 0.72f, 0.78f),
                Ground = new Color(0.35f, 0.35f, 0.35f), SunColor = new Color(1f, 1f, 1f),
                // Was the 2nd brightest biome (1.45 sun + 0.15 exposure); with the pale sky/
                // ground tones and the global lift on top it read as overexposed. Toned down.
                SunIntensity = 1.18f, PostExposure = -0.08f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f,
                Saturation = -20f
            },
            8 => ManhattanDaylight ? ManhattanDayMood() : ManhattanNightMood(),
            _ => new BiomeMood // GREENWOOD
            {
                // 0.0028, down from 0.0065. Fog here is exponential-squared, so visibility
                // falls off as exp(-(d*density)^2): at 0.0065 a 500 m ridge transmits
                // 0.003% of its light and 250 m is already 93% haze. Greenwood was a
                // 200 m bubble, which is why the horizon mountains could not be seen and
                // why the forest reads as a corridor whatever is placed beyond it.
                // 0.0020. The far rank sits at 620 m, and exp(-(620*0.0028)^2) leaves it
                // at 5% - a rumour rather than a mountain. At 0.0020 it holds 21%, which
                // is haze on a real ridgeline rather than erasure, and the near rank at
                // 300 m stays almost clear.
                FogDensity = 0.0020f, Fog = new Color(0.33f, 0.47f, 0.43f),
                Sky = new Color(0.40f, 0.55f, 0.62f), Equator = new Color(0.20f, 0.34f, 0.28f),
                Ground = new Color(0.075f, 0.11f, 0.075f), SunColor = new Color(0.98f, 0.98f, 0.95f),
                SunIntensity = 1.40f, PostExposure = 0.12f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.1f,
                Saturation = -20f
            }
        };

        private GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var item = Adopt(GameObject.CreatePrimitive(type));
            item.name = name;
            if (parent != null) item.transform.SetParent(parent);
            item.transform.position = position;
            item.transform.localScale = scale;
            var primitiveRenderer = item.GetComponent<Renderer>();
            primitiveRenderer.sharedMaterial = material;
            primitiveRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var primitiveCollider = item.GetComponent<Collider>();
            if (primitiveCollider != null) Destroy(primitiveCollider);
            return item;
        }

        private static string GroundNameFor(int biomeIndex) => biomeIndex switch
        {
            1 => "Snow Ground",
            2 => "Sewer Concrete",
            3 => "Industrial Ground",
            4 => "Alien Floor",
            5 => "Industrial Ground",
            6 => "Canyon Sand",
            7 => "Kowloon Ground",
            8 => "Cyber Ground",
            9 => "Hills Ground",
            CanalTownIndex => "Kowloon Ground",
            _ => "Forest Floor PBR"
        };



        /// Three-layer ground materials for the biomes that ship enough ground textures.
        /// Weights come from mesh vertex colour (see BuildRibbon), so this needs no
        /// control map. Biomes with only one ground texture keep their flat material.
        private void BuildSplatMaterials()
        {
            var splatShader = Shader.Find("RoadRage/TerrainSplat");
            if (splatShader == null)
            {
                Debug.LogWarning("TerrainSplat shader missing; ground stays single-texture");
                return;
            }

            void Splat(string name, string pack, (string tex, float tile, Color tint)[] layers, float smoothness)
            {
                var material = new Material(splatShader) { name = name };
                for (var i = 0; i < 3 && i < layers.Length; i++)
                {
                    var (tex, tile, tint) = layers[i];
                    var albedo = BiomeTexture(pack, tex + "_D");
                    var normal = BiomeTexture(pack, tex + "_N");
                    if (albedo != null) material.SetTexture($"_Splat{i}", albedo);
                    if (normal != null) material.SetTexture($"_Normal{i}", normal);
                    material.SetFloat($"_Tile{i}", tile);
                    material.SetColor($"_Tint{i}", tint);
                }
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_NormalScale", 1f);
                materials[name] = material;
            }

            // Greenwood: fallen-leaf litter, dark humus in large patches, compacted dirt and
            // gravel along the road edge. Baked by Tools/Blender/build_forest_floor.py.
            // Each layer repeats at a different size (2 m, 2.7 m, 1.6 m) so no single
            // tile grid lines up across the blend.
            Splat("Forest Floor PBR", "ForestFloor", new[]
            {
                ("T_forest_litter", 0.5f, new Color(1.1f, 1.08f, 1.05f)),
                ("T_forest_humus", 0.37f, new Color(1.15f, 1.12f, 1.1f)),
                ("T_forest_verge", 0.62f, Color.white),
            }, 0.1f);

            // Red Canyon: sand floor, rocky ground in patches, loose stones at the verge.
            Splat("Canyon Sand", "RedCanyon", new[]
            {
                ("T_sand", 0.055f, new Color(1f, 0.88f, 0.74f)),
                ("T_rock_ground", 0.075f, new Color(0.92f, 0.72f, 0.58f)),
                ("T_stones", 0.11f, new Color(0.88f, 0.80f, 0.70f)),
            }, 0.14f);

            // Hollywood: dry dirt, scrubby ground, dusty gravel at the shoulder.
            // NOT T_floor_bricks for the verge - it is a paving texture and turned the
            // roadside into a tiled plaza.
            Splat("Hills Ground", "HollywoodHills", new[]
            {
                ("T_ground_01", 0.07f, new Color(0.96f, 0.90f, 0.78f)),
                ("T_ground_03", 0.09f, new Color(0.84f, 0.82f, 0.68f)),
                ("T_ground_02", 0.14f, new Color(0.80f, 0.76f, 0.68f)),
            }, 0.16f);
        }

        /// Transparent decal materials. Alpha-blended (not alpha-clipped) so grime fades
        /// at its edges instead of showing a hard cut, and ZWrite off so they never
        /// occlude the road they lie on.
        private void BuildDecalMaterials()
        {
            void Decal(string name, string texture, float smoothness, float opacity, Color tint)
            {
                var map = Resources.Load<Texture2D>($"Decals/{texture}");
                var material = new Material(LitShader) { name = name };
                if (map != null)
                {
                    material.mainTexture = map;
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", map);
                }
                // _Surface only drives the material inspector. At runtime URP reads the
                // explicit blend factors, so without these the draw stays One/Zero and an
                // alpha-shaped decal renders as a solid tinted rectangle.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_Cull", 0f);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", 0f);
                // Base colour alpha multiplies the texture mask. The grunge masks are
                // largely opaque, so without this a "stain" becomes a painted slab.
                var c = tint; c.a = opacity;
                material.color = c;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                materials[name] = material;
            }

            Decal("Decal Tyre", "D_tyre_streak", 0.22f, 0.30f, Color.white);
            Decal("Decal Grime", "D_grime_patch", 0.18f, 0.16f, Color.white);
            Decal("Decal Oil", "D_oil_stain", 0.62f, 0.34f, Color.white);
            Decal("Decal Patch", "D_patch_repair", 0.25f, 0.22f, Color.white);
            Decal("Decal Graffiti A", "D_graffiti_01", 0.30f, 0.92f, Color.white);
            Decal("Decal Graffiti B", "D_graffiti_02", 0.30f, 0.92f, Color.white);
            Decal("Decal Graffiti C", "D_graffiti_03", 0.30f, 0.92f, Color.white);
            Decal("Decal Tag A", "D_tag_01", 0.28f, 0.80f, Color.white);
            Decal("Decal Tag B", "D_tag_02", 0.28f, 0.80f, Color.white);
        }

        /// A decal patch that follows the road's curve and camber. Built as its own small
        /// mesh with UV 0..1 across the patch (BuildRibbon tiles UV by distance, which
        /// would repeat the decal instead of showing it once).
        private GameObject BuildDecalPatch(string name, float distance, float lateral,
            float width, float length, Material material, float height = 0.185f)
        {
            const int steps = 4;
            var vertices = new Vector3[(steps + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];
            for (var i = 0; i <= steps; i++)
            {
                var f = i / (float)steps;
                var d = distance + (f - 0.5f) * length;
                vertices[i * 2] = RoadPath.Point(d, lateral - width * 0.5f, height);
                vertices[i * 2 + 1] = RoadPath.Point(d, lateral + width * 0.5f, height);
                uv[i * 2] = new Vector2(0f, f);
                uv[i * 2 + 1] = new Vector2(1f, f);
                if (i == steps) continue;
                var t = i * 6;
                var v = i * 2;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 3;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
            }
            var patch = CreateMeshObject(name, vertices, triangles, uv, material);
            patch.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return patch;
        }

        /// Grime, tyre marks and patched repairs down the carriageway. Cheap geometry,
        /// and it breaks up the single flat asphalt texture that reads as plastic.
        private void ScatterRoadDecals()
        {
            var previous = Random.state;
            Random.InitState(9931 ^ chunkSeed);
            var half = RoadPath.HalfWidthAt((segStart + segEnd) * 0.5f);
            for (var d = segStart; d < segEnd; d += Random.Range(22f, 48f))
            {
                var pick = Random.value;
                var (mat, w, l) = pick < 0.42f
                    ? (materials["Decal Tyre"], Random.Range(0.5f, 0.9f), Random.Range(9f, 22f))
                    : pick < 0.72f
                        ? (materials["Decal Grime"], Random.Range(2f, 4.5f), Random.Range(3f, 6f))
                        : pick < 0.9f
                            ? (materials["Decal Oil"], Random.Range(1.2f, 2.6f), Random.Range(1.5f, 3f))
                            : (materials["Decal Patch"], Random.Range(2f, 4f), Random.Range(2.5f, 5f));
                // Tyre marks sit in lanes; grime and oil wander anywhere on the surface.
                var lateral = pick < 0.42f
                    ? RoadPath.LaneLateral(d, Random.Range(-1, 2) * 0.62f) + Random.Range(-0.5f, 0.5f)
                    : Random.Range(-half + 1f, half - 1f);
                BuildDecalPatch("Road Decal", d, lateral, w, l, mat);
            }
            Random.state = previous;
        }

        private void BuildRoad(int biomeIndex)
        {
            PrepareBuildingPads();
            var groundName = GroundNameFor(biomeIndex);
            var isCity = biomeIndex == 5 || biomeIndex == 7 || biomeIndex == 8 || biomeIndex == 3 || biomeIndex == 9;
            if (biomeIndex == CanalTownIndex)
            {
                BuildCanalGround(materials[groundName]);
            }
            else if (isCity)
            {
                // Flanking solid foundations on left and right sides (leaves central highway 100% clean with zero z-fighting)
                BuildRibbon($"Left {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    -150f, -1.0f, -0.02f, materials[groundName], sampleStep: 6f, displace: 0f, relative: true);
                BuildRibbon($"Right {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    1.0f, 150f, -0.02f, materials[groundName], sampleStep: 6f, displace: 0f, relative: true);
            }
            else
            {
                // The verge layer (gravel/dirt) runs 6-32 m past the clearance in the open
                // biomes. On Greenwood's narrow forest road that turned everything in view
                // into gravel, so there it only covers the road edge to just past the rail
                // and leaf litter takes over beyond.
                //
                // The ground ribbon only has a vertex every ~35 m across, which would
                // smear that band over 35 m, so Greenwood gets a finely divided near strip
                // (to 4x the half width, ~1 m per step) and the coarse ribbon beyond it.
                // Both lie inside the flattened corridor where they meet, so the seam has
                // no height step, and the weights come from the same function either side.
                var biomeName = Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)];
                var vergeFrom = biomeIndex == 0 ? -3f : 6f;
                var vergeTo = biomeIndex == 0 ? 1.5f : 32f;
                var nearEdge = biomeIndex == 0 ? 4f : 1f;
                if (biomeIndex == 0)
                {
                    BuildRibbon($"Left {biomeName} Near Ground", -nearEdge, -1.0f, -0.05f, materials[groundName],
                        sampleStep: 5f, displace: 4.5f, lateralSegments: 14, relative: true, vergeFrom: vergeFrom, vergeTo: vergeTo);
                    BuildRibbon($"Right {biomeName} Near Ground", 1.0f, nearEdge, -0.05f, materials[groundName],
                        sampleStep: 5f, displace: 4.5f, lateralSegments: 14, relative: true, vergeFrom: vergeFrom, vergeTo: vergeTo);
                }
                BuildRibbon($"Left {biomeName} Ground",
                    -150f, -nearEdge, -0.05f, materials[groundName], sampleStep: 5f, displace: 4.5f, lateralSegments: 20, relative: true,
                    vergeFrom: vergeFrom, vergeTo: vergeTo);
                BuildRibbon($"Right {biomeName} Ground",
                    nearEdge, 150f, -0.05f, materials[groundName], sampleStep: 5f, displace: 4.5f, lateralSegments: 20, relative: true,
                    vergeFrom: vergeFrom, vergeTo: vergeTo);
            }
            // Main Asphalt Highway
            var roadMaterial = biomeIndex == 0 ? materials["Forest Road"] : materials["Road"];
            EnableProbeReflections(BuildRibbon("Curved Asphalt Highway", -1f, 1f, 0.02f, roadMaterial, relative: true));

            // Road Edge & Terrain Integration per Biome Type:
            var hasCityCurbs = biomeIndex == 5 || biomeIndex == 7 || biomeIndex == 8 || biomeIndex == 3;
            // Traffic obeys signals only where signals exist. Set from the same test that
            // decides whether the road gets kerbs, because those are the same biomes that
            // place the lights - a stop line nobody can see a reason for reads as traffic
            // randomly halting on an open road.
            TrafficCarController.SignalsActive = hasCityCurbs;
            // A head-on wreck on a one-lane road has nowhere to be passed, so traffic
            // behind it stacks into a wall across the whole carriageway.
            TrafficCarController.HeadOnWrecksAllowed = LaneCountFor(biomeIndex) >= 2;
            TrafficCarController.NarrowRoad = LaneCountFor(biomeIndex) < 2;
            if (hasCityCurbs)
            {
                var curbMat = biomeIndex == 8 ? materials["Cyber Trim"] : materials["City Asphalt Trim"];
                BuildRibbon("Left City Curb", -1.24f, -1.20f, 0.14f, curbMat, relative: true);
                BuildRibbon("Right City Curb", 1.20f, 1.24f, 0.14f, curbMat, relative: true);

                // Paved Elevated Sidewalks
                var sidewalkMat = biomeIndex == 8 ? materials["Cyber Floor"] : (biomeIndex == 7 ? materials["Kowloon Ground"] : materials["Sidewalk"]);
                BuildRibbon("Left Paved Sidewalk", -1.85f, -1.24f, 0.14f, sidewalkMat, relative: true);
                BuildRibbon("Right Paved Sidewalk", 1.24f, 1.85f, 0.14f, sidewalkMat, relative: true);
            }
            else if (biomeIndex == CanalTownIndex)
            {
                BuildCanalPavements();
            }
            else if (biomeIndex == 9) // Hollywood Hills
            {
                // Clean bright paved pedestrian sidewalk (seamless with road edge, no dark shadow strip)
                BuildRibbon("Left Hills Sidewalk", -1.45f, -1.0f, 0.025f, materials["Hills Concrete"], relative: true);
                BuildRibbon("Right Hills Sidewalk", 1.0f, 1.45f, 0.025f, materials["Hills Concrete"], relative: true);
            }
            else if (biomeIndex == 6) // Red Canyon
            {
                // Clean desert edge: road asphalt blends seamlessly into canyon terrain with zero shadow strip
            }
            else if (biomeIndex == 1) // Snow Station
            {
                // Plowed Snow Banks
                BuildRibbon("Left Snow Bank Verge", -1.80f, -1.0f, 0.22f, materials["Snow Ground"], relative: true);
                BuildRibbon("Right Snow Bank Verge", 1.0f, 1.80f, 0.22f, materials["Snow Ground"], relative: true);
            }
            else if (biomeIndex == 4) // Alien Biomass
            {
                // Alien Organic Verge
                BuildRibbon("Left Alien Verge", -1.70f, -1.0f, 0.03f, materials["Alien Floor"], relative: true);
                BuildRibbon("Right Alien Verge", 1.0f, 1.70f, 0.03f, materials["Alien Floor"], relative: true);
            }
            else if (biomeIndex == 0) // Greenwood Forest
            {
                // Forest Litter & Dirt Verge
                BuildRibbon("Left Forest Verge", -1.15f, -1.0f, 0.02f, materials["Forest Verge Dirt"], relative: true);
                BuildRibbon("Right Forest Verge", 1.0f, 1.15f, 0.02f, materials["Forest Verge Dirt"], relative: true);
            }
            else
            {
                EnableProbeReflections(BuildRibbon("Left Curved Shoulder", -1.20f, -1f, 0.035f, materials["Shoulder"], relative: true));
                EnableProbeReflections(BuildRibbon("Right Curved Shoulder", 1f, 1.20f, 0.035f, materials["Shoulder"], relative: true));
            }

            // Road Paint & Lane Striping
            // Double yellow, no raised median. A 2.6 m reservation in City Asphalt Trim
            // was tried and read as a brown stripe down the middle of the road - dark
            // near-black flanked by yellow paint blends into one muddy band at speed,
            // which is not what a central reservation looks like from a car. A median
            // that has to be explained is worse than paint that does not.
            //
            // Greenwood is an unmarked single-lane forest road: no paint at all.
            if (biomeIndex != 0)
            {
                BuildRibbon("Center Yellow L", -0.22f, -0.10f, 0.038f, materials["Yellow Paint"]);
                BuildRibbon("Center Yellow R", 0.10f, 0.22f, 0.038f, materials["Yellow Paint"]);
                BuildRibbon("Left Edge Line", -0.96f, -0.90f, 0.038f, materials["White Paint"], relative: true);
                BuildRibbon("Right Edge Line", 0.90f, 0.96f, 0.038f, materials["White Paint"], relative: true);
            }

            var lanes = LaneCountFor(biomeIndex);
            if (biomeIndex == 0)
            {
                // Two lanes each way, German markings: a double solid white centre line
                // and 6 m dashes with 12 m gaps between the lanes. No edge or yellow
                // lines: those were taken off Greenwood for reading as stray strips.
                BuildRibbon("Center Line L", -0.028f, -0.012f, 0.038f, materials["White Paint"], relative: true);
                BuildRibbon("Center Line R", 0.012f, 0.028f, 0.038f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Left Lane Dashes", -0.50f, 0.14f, 6f, 18f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Right Lane Dashes", 0.50f, 0.14f, 6f, 18f, materials["White Paint"], relative: true);
            }
            else if (lanes == 2)
            {
                // 2 lanes each way: dashed white lane divider on each side
                BuildDashedRibbon("Left Lane Dashes", -0.50f, 0.15f, 5.5f, 11f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Right Lane Dashes", 0.50f, 0.15f, 5.5f, 11f, materials["White Paint"], relative: true);
            }
            else if (lanes >= 3)
            {
                // 3 lanes each way: dashed white lane dividers at 1/3 and 2/3
                BuildDashedRibbon("Left Lane Dashes Inner", -1f / 3f, 0.15f, 6.5f, 12f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Left Lane Dashes Outer", -2f / 3f, 0.15f, 6.5f, 12f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Right Lane Dashes Inner", 1f / 3f, 0.15f, 6.5f, 12f, materials["White Paint"], relative: true);
                BuildDashedRibbon("Right Lane Dashes Outer", 2f / 3f, 0.15f, 6.5f, 12f, materials["White Paint"], relative: true);
            }
        }

        private static void EnableProbeReflections(GameObject item)
        {
            if (item == null) return;
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
        }

        /// Deterministic value noise. Must be a pure function of world position so a
        /// chunk rebuilt on a later pass produces identical terrain at its seams.
        private static float TerrainNoise(float x, float z, float frequency)
        {
            var n = Mathf.Sin(x * frequency * 1.7f + 12.9898f) * Mathf.Cos(z * frequency * 1.3f + 4.1414f)
                  + 0.5f * Mathf.Sin((x + z) * frequency * 3.1f + 7.233f)
                  + 0.25f * Mathf.Cos((x - z) * frequency * 5.7f + 1.618f);
            return n / 1.75f;
        }

        /// Ground ribbons were 2 vertices wide, so a 300 m-wide "hillside" was one flat
        /// quad - the single biggest reason terrain read as cardboard. With `displace`
        /// the strip is subdivided across its width and pushed by noise, becoming real
        /// undulating ground. Road, shoulders and paint stay flat (displace = 0).
        /// Half width of the real B500 plus its shoulder: where the real banks start.
        private const float RealRoadEdge = 4.5f;
        /// Real heights beyond this are eased in (a 200 m valley side stays big but does
        /// not tower over the horizon ring the rest of the sky was tuned against).
        private const float RealGroundLimit = 90f;

        /// The real ground beside the B500 (Road Rage > Bake B500 Terrain, from the LGL
        /// DGM1), relative to the road; 0 without the table. The game's carriageway is
        /// wider than the real one, so the real bank is taken from the real road's edge
        /// and laid from the game's, and eased in over the first 6 m so the shoulder and
        /// rail stay level with the road.
        private static float RealGround(float distance, float lateral)
        {
            var route = RoadPath.Route;
            if (route == null || !route.HasTerrain) return 0f;
            var edge = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth;
            var across = Mathf.Abs(lateral) - edge;
            if (across <= 0f) return 0f;
            var real = route.TerrainAt(distance, Mathf.Sign(lateral) * (RealRoadEdge + across));
            real = RealGroundLimit * (float)System.Math.Tanh(real / RealGroundLimit);
            real = OnBuildingPads(distance, lateral, real);
            return real * Mathf.SmoothStep(0f, 1f, across / 6f);
        }

        /// Level ground under a building, blended into the slope around it.
        private struct BuildingPad
        {
            public float Distance, Lateral, Half, Height;
        }

        private static readonly List<BuildingPad> buildingPads = new();
        /// Metres over which a pad's level ground eases back into the real slope.
        private const float PadBlend = 10f;

        /// A building on a steep slope either hung in the air or sank into it: the
        /// ground is levelled under it first, as it would be for a real house, cut into
        /// the hill behind and banked up in front, and eased into the slope around it.
        private static float OnBuildingPads(float distance, float lateral, float real)
        {
            for (var i = 0; i < buildingPads.Count; i++)
            {
                var pad = buildingPads[i];
                var along = Mathf.Abs(distance - pad.Distance) - pad.Half;
                var across = Mathf.Abs(lateral - pad.Lateral) - pad.Half;
                if (along > PadBlend || across > PadBlend) continue;
                var outside = new Vector2(Mathf.Max(0f, along), Mathf.Max(0f, across)).magnitude;
                var weight = 1f - Mathf.SmoothStep(0f, 1f, outside / PadBlend);
                real = Mathf.Lerp(real, pad.Height, weight);
            }
            return real;
        }

        // The Greenwood ground ribbons over real terrain (BuildRoad): a near strip from
        // 1 to 4 half-widths out in 14 steps and the main one from 4 to 150 in 80, each
        // with its vertices at (step / steps)^2 of the span from the road, rows every
        // 5 m of road from 2 m before each 150 m chunk - the same grid in every chunk.
        private const float RibbonRowStep = 5f;
        private const float RibbonRowOffset = -2f;

        /// The ground exactly as the mesh draws it: the real ground at the four vertices
        /// round the point, blended between them. The real ground itself curves between
        /// the vertices while the mesh runs straight, so at the foot of every bank the
        /// mesh stood above it and anything planted on the real curve was buried.
        private static float MeshGround(float distance, float lateral)
        {
            var route = RoadPath.Route;
            if (route == null || !route.HasTerrain) return 0f;
            var row = Mathf.Floor((distance - RibbonRowOffset) / RibbonRowStep);
            var d0 = RibbonRowOffset + row * RibbonRowStep;
            var d1 = d0 + RibbonRowStep;
            var along = (distance - d0) / RibbonRowStep;
            return Mathf.Lerp(RibbonRowGround(d0, lateral), RibbonRowGround(d1, lateral), along);
        }

        /// Along one row of the ribbon, the ground between the two vertices either side.
        private static float RibbonRowGround(float distance, float lateral)
        {
            var half = RoadPath.HalfWidthAt(distance);
            var u = Mathf.Abs(lateral) / half;
            if (u <= 1f || u >= 150f) return RealGround(distance, lateral);
            float inner, span;
            int steps;
            if (u < 4f) { inner = 1f; span = 3f; steps = 14; }
            else { inner = 4f; span = 146f; steps = 80; }
            var t = Mathf.Sqrt((u - inner) / span) * steps;
            var k = Mathf.Min(steps - 1, Mathf.FloorToInt(t));
            var side = Mathf.Sign(lateral);
            var reach = InnerReach(distance, out var innerSide);
            float Vertex(int step)
            {
                var f = step / (float)steps;
                var at = (inner + span * f * f) * half;
                if (innerSide == side && at > reach) at = reach;
                var l = side * at;
                var y = RealGround(distance, l);
                if (at > RoadPath.ClearanceAt(distance) + 4f) y += LakeBasin(distance, l);
                return y;
            }
            var a = (inner + span * (k / (float)steps) * (k / (float)steps)) * half;
            var b = (inner + span * ((k + 1) / (float)steps) * ((k + 1) / (float)steps)) * half;
            var w = b > a ? Mathf.Clamp01((Mathf.Abs(lateral) - a) / (b - a)) : 0f;
            return Mathf.Lerp(Vertex(k), Vertex(k + 1), w);
        }

        /// The lowest real ground under a footprint of this radius, a little below it:
        /// a trunk on a slope roots on its downhill side, and the ground mesh between
        /// its vertices lies below the exact height at any one point.
        private static float RealGroundUnder(float distance, float lateral, float radius)
        {
            var route = RoadPath.Route;
            if (route == null || !route.HasTerrain) return 0f;
            // As the mesh draws it, so the footprint only has to allow for the trunk's
            // own width on the slope.
            var centre = MeshGround(distance, lateral);
            float low = centre, high = centre;
            foreach (var (along, across) in new[] { (radius, 0f), (-radius, 0f), (0f, radius), (0f, -radius) })
            {
                var y = MeshGround(distance + along, lateral + across);
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
            return low - 0.1f - 0.08f * (high - low);
        }

        /// How far the ground may reach on the inside of the bend at this distance, and
        /// which side that is (+1 right, -1 left, 0 straight).
        ///
        /// The ground strips run hundreds of metres out from the road, square to it. On
        /// the inside of a bend every strip points at the bend's centre, so past the bend
        /// radius they cross over one another, each carrying the real height for its
        /// own road distance: a hillside folded over itself, and trees planted on one
        /// layer were buried by the next. Stopping the strips short of the centre (the
        /// tightest radius nearby, a little inside it) keeps them from crossing; the
        /// ground beyond is covered by the strips of the road before and after the bend.
        private static readonly Dictionary<int, Vector2> innerReachCache = new();

        private static float InnerReach(float distance, out float innerSide)
        {
            // Asked for several times per tree planted; the road does not change, so
            // it is kept per half metre of road.
            var key = Mathf.RoundToInt(distance * 2f);
            if (innerReachCache.TryGetValue(key, out var cached))
            {
                innerSide = cached.y;
                return cached.x;
            }
            if (innerReachCache.Count > 50000) innerReachCache.Clear();
            var reach = MeasureInnerReach(key * 0.5f, out innerSide);
            innerReachCache[key] = new Vector2(reach, innerSide);
            return reach;
        }

        private static float MeasureInnerReach(float distance, out float innerSide)
        {
            var reach = float.PositiveInfinity;
            innerSide = 0f;
            for (var k = -4; k <= 4; k++)
            {
                var d = distance + k * 5f;
                var turn = Vector3.SignedAngle(RoadPath.Forward(d - 4f), RoadPath.Forward(d + 4f), Vector3.up) * Mathf.Deg2Rad;
                if (Mathf.Abs(turn) < 1e-4f) continue;
                var radius = 8f / Mathf.Abs(turn);
                if (radius * 0.9f >= reach) continue;
                reach = radius * 0.9f;
                innerSide = Mathf.Sign(turn);
            }
            return reach;
        }

        /// The ground seen from above at a world position, over the real terrain.
        ///
        /// The ground ribbons run hundreds of metres out from the road, so on the inside
        /// of a bend the strips laid from neighbouring road distances cross over one
        /// another, each with the real height for its own distance. Whichever lies
        /// highest is the ground that is seen there, and a tree placed by its own road
        /// distance could land on a lower strip, floating in front of the hillside that
        /// covered it. This finds every road distance whose strip passes through the
        /// point and returns the top one, as (distance, lateral) on that strip.
        private static bool VisibleGround(Vector3 point, out float groundDistance, out float groundLateral)
        {
            groundDistance = point.z;
            groundLateral = 0f;
            var route = RoadPath.Route;
            if (route == null || !route.HasTerrain) return false;
            var extent = 150f * RoadPath.HalfWidthAt(point.z);
            // Right() is level and the bends are capped at 42 degrees, so a strip that
            // reaches this point starts within extent * sin(42) of it along the road.
            var reach = Mathf.Min(extent * 0.7f, 500f);
            const float step = 3f;
            var best = float.NegativeInfinity;
            var found = false;
            float Along(float d) => Vector3.Dot(point - RoadPath.Center(d), RoadPath.Forward(d));
            var previousD = point.z - reach;
            var previous = Along(previousD);
            for (var d = previousD + step; d <= point.z + reach + 0.01f; d += step)
            {
                var along = Along(d);
                if (previous == 0f || Mathf.Sign(previous) != Mathf.Sign(along))
                {
                    var at = Mathf.Lerp(previousD, d, previous / (previous - along));
                    var lateral = Vector3.Dot(point - RoadPath.Center(at), RoadPath.Right(at));
                    var abs = Mathf.Abs(lateral);
                    var half = RoadPath.HalfWidthAt(at);
                    var stripReach = InnerReach(at, out var innerSide);
                    var built = innerSide == 0f || Mathf.Sign(lateral) != innerSide || abs <= stripReach;
                    if (built && abs >= half && abs <= 150f * half)
                    {
                        var y = RoadPath.CenterY(at) + RealGround(at, lateral) +
                                (abs > RoadPath.ClearanceAt(at) + 4f ? LakeBasin(at, lateral) : 0f);
                        if (y > best)
                        {
                            best = y;
                            groundDistance = at;
                            groundLateral = lateral;
                            found = true;
                        }
                    }
                }
                previousD = d;
                previous = along;
            }
            return found;
        }

        /// The height of the ground seen from above at a world position over the real
        /// terrain, as a trunk of this radius roots on it; NaN without the terrain.
        private static float GroundHeightAt(Vector3 point, float radius) =>
            VisibleGround(point, out var d, out var l) ? RoadPath.CenterY(d) + RealGroundUnder(d, l, radius) : float.NaN;

        /// Moves a model placed on the real ground at (distance, lateral) onto the
        /// ground that is actually seen where it ended up. Scaling it, grounding it by
        /// its bounds and pushing it clear of the road all move it after the first
        /// placement, and over a slope a sideways push leaves it hanging or buried.
        /// False when the terrain has no ground at all under the model where it stands -
        /// past the edge of the ground on the inside of a bend, nothing else covering it.
        private static bool SettleOnRealGround(GameObject model, float distance, float lateral, float radius)
        {
            if (model == null) return true;
            var route = RoadPath.Route;
            if (route == null || !route.HasTerrain) return true;
            if (!VisibleGround(model.transform.position, out var d, out var l)) return false;
            var placedOn = RoadPath.Point(distance, lateral, RealGroundUnder(distance, lateral, radius)).y;
            var ground = RoadPath.CenterY(d) + RealGroundUnder(d, l, radius);
            model.transform.position += Vector3.up * (ground - placedOn);
            return true;
        }

        private GameObject BuildRibbon(string name, float leftLateral, float rightLateral, float height,
            Material material, float start = float.NaN, float end = float.NaN, float sampleStep = 6f,
            bool relative = false, float displace = 0f, int lateralSegments = 1,
            float vergeFrom = 6f, float vergeTo = 32f)
        {
            // NaN means "this segment": ribbons are rebuilt per streamed chunk.
            if (float.IsNaN(start)) start = segStart - 2f;
            if (float.IsNaN(end)) end = segEnd + 2f;
            if (displace > 0f) lateralSegments = Mathf.Max(lateralSegments, 10);
            // The displaced ground of a route with a baked terrain table takes the real
            // ground instead of noise, finely divided across.
            var realGround = displace > 0f && RoadPath.Route != null && RoadPath.Route.HasTerrain;
            if (realGround) lateralSegments = Mathf.Max(lateralSegments, Mathf.Abs(rightLateral - leftLateral) > 20f ? 80 : 14);
            var across = Mathf.Max(1, lateralSegments) + 1;
            var sampleCount = Mathf.CeilToInt((end - start) / sampleStep) + 1;
            var vertices = new Vector3[sampleCount * across];
            var uv = new Vector2[vertices.Length];
            var colors = displace > 0f ? new Color[vertices.Length] : null;
            var triangles = new int[(sampleCount - 1) * (across - 1) * 6];
            var t = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var distance = Mathf.Min(end, start + i * sampleStep);
                var scale = relative ? RoadPath.HalfWidthAt(distance) : 1f;
                var innerSide = 0f;
                var innerReach = realGround ? InnerReach(distance, out innerSide) : float.PositiveInfinity;
                for (var j = 0; j < across; j++)
                {
                    var f = across == 1 ? 0f : j / (float)(across - 1);
                    // Over real terrain the vertices crowd towards the road, where the
                    // banks and cuttings are seen up close, and spread out towards the
                    // valley sides.
                    if (realGround)
                        f = Mathf.Abs(leftLateral) < Mathf.Abs(rightLateral) ? f * f : 1f - (1f - f) * (1f - f);
                    var lateral = Mathf.Lerp(leftLateral, rightLateral, f) * scale;
                    if (innerSide != 0f && Mathf.Sign(lateral) == innerSide && Mathf.Abs(lateral) > innerReach)
                        lateral = innerSide * innerReach;
                    var lift = 0f;
                    if (realGround)
                    {
                        lift = RealGround(distance, lateral);
                        if (colors != null && Mathf.Abs(lateral) > RoadPath.ClearanceAt(distance) + 4f)
                            lift += LakeBasin(distance, lateral);
                    }
                    else if (displace > 0f)
                    {
                        var p = RoadPath.Point(distance, lateral);
                        // Fade at the strip edges so neighbouring ribbons still meet.
                        var edge = Mathf.Sin(f * Mathf.PI);
                        // AND flatten across the road corridor. The ground strip runs
                        // under the carriageway, so displacing it there pushes terrain up
                        // through the asphalt - it looked like brown slabs on the road.
                        var clearance = RoadPath.ClearanceAt(distance);
                        var corridor = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(clearance + 28f, clearance + 65f, Mathf.Abs(lateral)));
                        lift = displace * edge * corridor *
                               (TerrainNoise(p.x, p.z, 0.021f) + 0.45f * TerrainNoise(p.x, p.z, 0.061f));
                        if (colors != null && Mathf.Abs(lateral) > RoadPath.ClearanceAt(distance) + 4f)
                            lift += LakeBasin(distance, lateral);
                    }
                    vertices[i * across + j] = RoadPath.Point(distance, lateral, height + lift);
                    uv[i * across + j] = new Vector2(f * Mathf.Abs(rightLateral - leftLateral) * 0.08f,
                        distance * 0.08f);
                    if (colors != null)
                    {
                        var p = RoadPath.Point(distance, lateral);
                        // Layer 0 = base ground, 1 = patchy overgrowth//rubble,
                        // 2 = swept gravel that collects along the verge.
                        var patch = TerrainNoise(p.x, p.z, 0.010f) * 0.5f + 0.5f;
                        var detail = TerrainNoise(p.x, p.z, 0.038f) * 0.5f + 0.5f;
                        var verge = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(RoadPath.ClearanceAt(distance) + vergeFrom,
                                RoadPath.ClearanceAt(distance) + vergeTo, Mathf.Abs(lateral)));
                        var w1 = Mathf.Clamp01((patch - 0.42f) * 2.6f) * (1f - verge * 0.7f);
                        var w2 = Mathf.Clamp01(verge * 1.15f + (detail - 0.72f) * 2f);
                        var w0 = Mathf.Max(0.02f, 1f - w1 - w2);
                        var sum = w0 + w1 + w2;
                        colors[i * across + j] = new Color(w0 / sum, w1 / sum, w2 / sum, 1f);
                    }
                }
                if (i == sampleCount - 1) continue;
                for (var j = 0; j < across - 1; j++)
                {
                    var v = i * across + j;
                    triangles[t++] = v;
                    triangles[t++] = v + across;
                    triangles[t++] = v + across + 1;
                    triangles[t++] = v;
                    triangles[t++] = v + across + 1;
                    triangles[t++] = v + 1;
                }
            }
            var ribbon = CreateMeshObject(name, vertices, triangles, uv, material, colors);
            if (realGround)
            {
                // Drawn from both sides. Where the strips on the inside of a bend stop
                // short (InnerReach), the ground of the road further on covers the spot but
                // does not quite meet them, and a view up the bank ran under that ground's
                // edge: its underside was culled and a white line of sky showed through
                // the forest. Now the underside is drawn, lit as ground.
                DrawBothSides(ribbon);
                // What planting rays land on (RaycastGround): the ground exactly as drawn.
                ribbon.layer = PlantingGroundLayer;
                ribbon.AddComponent<MeshCollider>().sharedMesh = ribbon.GetComponent<MeshFilter>().sharedMesh;
            }
            return ribbon;
        }

        /// After the chunk is planted (the next frame): how many forest pieces the
        /// planting rays stood on the ground, how many found none, and the furthest one
        /// was moved from where the terrain maths had it.
        private System.Collections.IEnumerator ReportPlanting(float chunkStart, PlantTally tally, Transform root)
        {
            yield return null;
            if (!OverRealTerrain) yield break;
            var colliders = 0;
            if (root != null)
                foreach (var c in root.GetComponentsInChildren<MeshCollider>())
                    if (c.gameObject.layer == PlantingGroundLayer) colliders++;
            Debug.Log($"RR_PLANT chunk at {chunkStart:0} m: {tally.OnRay} stood on the ground by ray, " +
                      $"{tally.NoGround} found no ground and {tally.Ambiguous} crossed ground (not planted), " +
                      $"furthest moved {tally.Shift:0.0} m; " +
                      $"{colliders} ground colliders in the chunk");
        }

        /// "Ignore Raycast": ordinary raycasts pass through it, the planting rays ask for
        /// it by name, and physics (a car thrown off the road) still lands on it.
        private const int PlantingGroundLayer = 2;

        /// The ground straight down at a point as it is drawn - the top surface where
        /// ground strips overlap, the mesh's own straight runs between its vertices -
        /// taken as a trunk of this radius roots on it: its downhill side, a little
        /// deeper the steeper the slope. False where there is no ground under the point.
        private static bool RaycastGround(Vector3 at, float radius, out float ground)
        {
            ground = 0f;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (var k = 0; k < 5; k++)
            {
                var offset = k switch
                {
                    1 => new Vector3(radius, 0f, 0f),
                    2 => new Vector3(-radius, 0f, 0f),
                    3 => new Vector3(0f, 0f, radius),
                    4 => new Vector3(0f, 0f, -radius),
                    _ => Vector3.zero,
                };
                var origin = new Vector3(at.x + offset.x, at.y + 400f, at.z + offset.z);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, 1200f, 1 << PlantingGroundLayer,
                        QueryTriggerInteraction.Ignore))
                {
                    if (k == 0) return false;
                    continue;
                }
                low = Mathf.Min(low, hit.point.y);
                high = Mathf.Max(high, hit.point.y);
            }
            ground = low - 0.1f - 0.08f * (high - low);
            return true;
        }

        /// Lowers or raises a model so the bottom of its bounds is at this height.
        private static void StandOn(GameObject model, float y)
        {
            if (TryGetCombinedBounds(model, out var bounds))
                model.transform.position += Vector3.up * (y - bounds.min.y);
        }

        private static bool OverRealTerrain => RoadPath.Route != null && RoadPath.Route.HasTerrain;

        private GameObject BuildWallRibbon(string name, float lateral, float bottom, float top, Material material,
            float start = float.NaN, float end = float.NaN, float sampleStep = 7f)
        {
            if (float.IsNaN(start)) start = segStart - 2f;
            if (float.IsNaN(end)) end = segEnd + 2f;
            var sampleCount = Mathf.CeilToInt((end - start) / sampleStep) + 1;
            var vertices = new Vector3[sampleCount * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(sampleCount - 1) * 12];
            for (var i = 0; i < sampleCount; i++)
            {
                var distance = Mathf.Min(end, start + i * sampleStep);
                vertices[i * 2] = RoadPath.Point(distance, lateral, bottom);
                vertices[i * 2 + 1] = RoadPath.Point(distance, lateral, top);
                uv[i * 2] = new Vector2(distance * 0.1f, 0f);
                uv[i * 2 + 1] = new Vector2(distance * 0.1f, 1f);
                if (i == sampleCount - 1) continue;
                var t = i * 12;
                var v = i * 2;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
                triangles[t + 6] = v;
                triangles[t + 7] = v + 3;
                triangles[t + 8] = v + 1;
                triangles[t + 9] = v;
                triangles[t + 10] = v + 2;
                triangles[t + 11] = v + 3;
            }
            return CreateMeshObject(name, vertices, triangles, uv, material);
        }

        private GameObject BuildDashedRibbon(string name, float lateral, float width, float dashLength,
            float spacing, Material material, bool relative = false)
        {
            var first = SegBegin(0f, spacing);
            var dashes = Mathf.CeilToInt((segEnd + 4f - first) / spacing);
            var vertices = new Vector3[dashes * 4];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[dashes * 6];
            for (var i = 0; i < dashes; i++)
            {
                var start = first + i * spacing;
                var end = start + dashLength;
                var v = i * 4;
                var ls = relative ? lateral * RoadPath.HalfWidthAt(start) : lateral;
                var le = relative ? lateral * RoadPath.HalfWidthAt(end) : lateral;
                vertices[v] = RoadPath.Point(start, ls - width * 0.5f, 0.08f);
                vertices[v + 1] = RoadPath.Point(start, ls + width * 0.5f, 0.08f);
                vertices[v + 2] = RoadPath.Point(end, le - width * 0.5f, 0.08f);
                vertices[v + 3] = RoadPath.Point(end, le + width * 0.5f, 0.08f);
                uv[v] = Vector2.zero;
                uv[v + 1] = Vector2.right;
                uv[v + 2] = Vector2.up;
                uv[v + 3] = Vector2.one;
                var t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 1;
            }
            return CreateMeshObject(name, vertices, triangles, uv, material);
        }

        private GameObject CreateMeshObject(string name, Vector3[] vertices, int[] triangles, Vector2[] uv,
            Material material, Color[] colors = null)
        {
            var item = Adopt(new GameObject(name));
            var mesh = new Mesh { name = $"{name} Runtime Mesh", vertices = vertices, triangles = triangles, uv = uv };
            if (colors != null) mesh.colors = colors;
            mesh.RecalculateNormals();
            // Tangents only for meshes actually drawn with the splat shader. Every
            // displaced ribbon carries vertex colours (they are cheap), but
            // RecalculateTangents on a 26-column, several-hundred-sample terrain strip is
            // not - doing it for every ground ribbon stalled chunk generation.
            if (colors != null && material != null && material.shader != null &&
                material.shader.name == "RoadRage/TerrainSplat")
                mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            // Only the road opts back into the realtime probe (see BuildRoad). Letting
            // every surface sample it replaces their skybox reflection with the captured
            // night scene, which drains the fill light out of the whole biome.
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return item;
        }

        private GameObject Model(string resourceName, Material material)
        {
            var prefab = Resources.Load<GameObject>($"Hideout/Meshes/{resourceName}");
            if (prefab == null) return null;
            var model = Adopt(Instantiate(prefab));
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = new Material[renderer.sharedMaterials.Length];
                for (var i = 0; i < assigned.Length; i++)
                {
                    var sourceName = renderer.sharedMaterials[i] != null ? renderer.sharedMaterials[i].name.ToLowerInvariant() : string.Empty;
                    assigned[i] = resourceName == "scanned_tree"
                        ? (sourceName.Contains("bark") ? materials["Hideout Bark PBR"] : materials["Hideout Leaf Cutout"])
                        : material;
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
            return model;
        }

        /// Procedural lit-window mask: 12x12 panes per sheet, ~55% lit, warm interior
        /// white with occasional cool offices. Linear colour space so the emission
        /// colour multiplies cleanly. One shared instance; materials only read it.
        private static Texture2D windowGridCache;
        private static Texture2D WindowEmissionGrid()
        {
            if (windowGridCache != null) return windowGridCache;
            const int size = 512;
            const int cells = 12;
            const int cell = size / cells;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var previous = Random.state;
            Random.InitState(77031);
            for (var cy = 0; cy < cells; cy++)
            for (var cx = 0; cx < cells; cx++)
            {
                var lit = Random.value > 0.45f;
                Color color;
                if (!lit) color = new Color(0.015f, 0.02f, 0.03f);
                else if (Random.value > 0.78f) color = new Color(0.75f, 0.85f, 1f) * Random.Range(0.65f, 1f);
                else color = new Color(1f, 0.82f, 0.58f) * Random.Range(0.55f, 1f);
                var margin = Mathf.Max(1, cell / 5);
                for (var py = 0; py < cell; py++)
                for (var px = 0; px < cell; px++)
                {
                    var inPane = px >= margin && px < cell - margin && py >= margin && py < cell - margin;
                    tex.SetPixel(cx * cell + px, cy * cell + py, inPane ? color : Color.black);
                }
            }
            Random.state = previous;
            tex.Apply(true, true);
            windowGridCache = tex;
            return tex;
        }

        private GameObject BiomeModel(string pack, string resourceName, Material material)
        {
            var prefab = Resources.Load<GameObject>($"Biomes/{pack}/Meshes/{resourceName}")
                      ?? Resources.Load<GameObject>($"{pack}/{resourceName}")
                      ?? Resources.Load<GameObject>(resourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"Missing biome model: {pack}/{resourceName}");
                return null;
            }
            var model = Adopt(Instantiate(prefab));
            // The NYC set was authored for HDRP and hangs Decal Projectors off the
            // building parts - fifteen on a single storey. HDRP is not installed here,
            // so each one arrives as a GameObject whose script cannot resolve: no
            // geometry, no effect, one missing-script warning apiece, and a few hundred
            // dead transforms per city block. They are dropped on instantiate.
            foreach (var child in model.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == model.transform) continue;
                if (child.name.IndexOf("decal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    DestroyImmediate(child.gameObject);
            }
            foreach (var l in model.GetComponentsInChildren<Light>(true))
            {
                DestroyImmediate(l.gameObject == model ? l : l.gameObject);
            }
            foreach (var b in model.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                var typeName = b.GetType().Name;
                if (typeName.Contains("Halo") || typeName.Contains("Flare") || typeName.Contains("LensFlare") || typeName.Contains("Light"))
                    DestroyImmediate(b);
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // One-shot diagnostic: dump the real submesh material names so the
                // skyline/window mapping can be verified instead of guessed.
                var assigned = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (var i = 0; i < assigned.Length; i++)
                {
                    var sourceName = i < renderer.sharedMaterials.Length && renderer.sharedMaterials[i] != null
                        ? renderer.sharedMaterials[i].name.ToLowerInvariant()
                        : string.Empty;
                    var sourceMaterial = i < renderer.sharedMaterials.Length
                        ? renderer.sharedMaterials[i]
                        : null;
                    if (pack == "Synthwave" && resourceName.StartsWith("Car/"))
                    {
                        if (resourceName.Contains("SM_car_B1") || resourceName.Contains("SM_car_B2")) assigned[i] = materials["City Car B2"];
                        else if (resourceName.Contains("SM_car_B")) assigned[i] = materials["City Car B1"];
                        else assigned[i] = sourceName.Contains("part") || sourceName.Contains("window")
                            ? materials["City Car Parts"]
                            : material;
                    }
                    else if (pack == "Synthwave")
                    {
                        assigned[i] = sourceName.Contains("window") || sourceName.Contains("neon") || sourceName.Contains("light")
                            ? materials["City Windows"]
                            : material;
                    }
                    else if (pack == "CyberpunkCity" || pack == "Buildings" || resourceName.Contains("Buildings/"))
                    {
                        // This tested material.name == "Cyber Skyline" exactly. Manhattan
                        // passes "City Skyline", so it never matched, and both the frontage
                        // and the skyline towers fell through to City Concrete - which is
                        // why every building in the biome was the same grey regardless of
                        // what the caller asked for.
                        var facadePass = material != null && IsFacadeMaterial(material.name);
                        if (sourceName.Contains("hologram") || sourceName.Contains("sign") || sourceName.Contains("light") || sourceName.Contains("lamp") || sourceName.Contains("farola")) assigned[i] = materials["City Neon"];
                        else if (sourceName.Contains("billboard") || sourceName.Contains("panel")) assigned[i] = materials["City Billboard"];
                        else if (sourceName.Contains("streetlamp") || sourceName.Contains("pole") || sourceName.Contains("post")) assigned[i] = materials["City Asphalt Trim"];
                        else if (sourceName.Contains("fireplug") || sourceName.Contains("hydrant")) assigned[i] = materials["Car Orange"];
                        else if (sourceName.Contains("window_car") || sourceName.Contains("glass")) assigned[i] = materials["Glass"];
                        else if (sourceName.Contains("window") || sourceName.Contains("interior_light")) assigned[i] = materials["City Windows"];
                        else if (sourceName.Contains("trim") || sourceName.Contains("metal") || sourceName.Contains("roof") || sourceName.Contains("tejad")) assigned[i] = materials["City Asphalt Trim"];
                        else if (KeepPackWallMaterials && pack == "Buildings" && sourceMaterial != null)
                            assigned[i] = sourceMaterial;
                        else if (sourceName.Contains("concrete") || sourceName.Contains("concrate") || sourceName.Contains("brick") || sourceName.Contains("plaster") || sourceName.Contains("highrise") || sourceName.Contains("build")) assigned[i] = facadePass ? material : materials["City Concrete"];
                        else assigned[i] = material ?? materials["City Concrete"];
                    }
                    else if (pack == "HongKong")
                    {
                        var kowloonSkylinePass = material != null && material.name == "Kowloon Skyline";
                        if (sourceName.Contains("chinese_neon")) assigned[i] = materials["Kowloon Sign"];
                        else if (sourceName.Contains("building_modules")) assigned[i] = kowloonSkylinePass ? material : materials["Kowloon Building"];
                        else if (sourceName.Contains("vegtables")) assigned[i] = materials["Kowloon Produce"];
                        else if (sourceName.Contains("food_market")) assigned[i] = materials["Kowloon Food"];
                        else if (sourceName.Contains("street_market_detail")) assigned[i] = materials["Kowloon Market Detail"];
                        else if (sourceName.Contains("street_market")) assigned[i] = materials["Kowloon Market"];
                        else if (sourceName.Contains("street_module_detail")) assigned[i] = materials["Kowloon Street Detail"];
                        else if (sourceName.Contains("street_module")) assigned[i] = materials["Kowloon Street"];
                        else if (sourceName.Contains("street_props")) assigned[i] = materials["Kowloon Props"];
                        else assigned[i] = material;
                    }
                    else if (pack == "DemoCity")
                    {
                        if (sourceName.Contains("highrise")) assigned[i] = materials["Demo Highrise"];
                        else if (sourceName.Contains("base") || sourceName.Contains("sideway") || sourceName.Contains("concrete") || sourceName.Contains("wall")) assigned[i] = materials["Demo Bases"];
                        else if (sourceName.Contains("window") || sourceName.Contains("glass")) assigned[i] = materials["Demo Windows"];
                        else if (sourceName.Contains("interior")) assigned[i] = materials["Demo Interior"];
                        else if (sourceName.Contains("prop") || sourceName.Contains("metal") || sourceName.Contains("lamp") || sourceName.Contains("bench")) assigned[i] = materials["Demo Props"];
                        else if (sourceName.Contains("fence")) assigned[i] = materials["Demo Fence"];
                        else if (sourceName.Contains("vegetation") || sourceName.Contains("tree")) assigned[i] = materials["City Palm"];
                        else assigned[i] = materials["Demo Facades"];
                    }
                    else if (pack == "HollywoodHills")
                    {
                        if (sourceName.Contains("hollywood_sign") || sourceName.Contains("letter") || resourceName.Contains("Letters/")) assigned[i] = materials["Hills Sign"];
                        else if (sourceName.Contains("background_building") || resourceName.Contains("Background_buidings/")) assigned[i] = materials["Hills Windows"];
                        else if (sourceName.Contains("landscape_far") || sourceName.Contains("mountain") || resourceName.Contains("Mountain/")) assigned[i] = materials["Hills Landscape"];
                        else if (sourceName.Contains("window") || sourceName.Contains("glass")) assigned[i] = materials["Hills Window Glass"];
                        else if (sourceName.Contains("roof")) assigned[i] = materials["Hills Roof"];
                        else if (sourceName.Contains("brick")) assigned[i] = materials["Hills Brick"];
                        else if (sourceName.Contains("antenna") || resourceName.Contains("Antenna/")) assigned[i] = materials["Hills Pole"];
                        else if (sourceName.Contains("metal") || resourceName.Contains("Container/")) assigned[i] = materials["Hills Metal"];
                        else if (sourceName.Contains("wood") || resourceName.Contains("Crates/")) assigned[i] = materials["Hills Wood"];
                        else if (sourceName.Contains("water") || sourceName.Contains("pool") || resourceName.Contains("Pool/")) assigned[i] = materials["Hills Pool"];
                        else if (sourceName.Contains("tree_bark") || resourceName.Contains("tree/")) assigned[i] = materials["Hills Bark"];
                        else if (sourceName.Contains("palm_tree") || sourceName.Contains("leaf") || sourceName.Contains("leaves")) assigned[i] = materials["Hills Leaves"];
                        else if (sourceName.Contains("bush") || resourceName.Contains("Vegetation/")) assigned[i] = materials["Hills Scrub"];
                        else if (sourceName.Contains("plant") || resourceName.Contains("Plants/")) assigned[i] = materials["Hills Plant"];
                        else if (sourceName.Contains("concrete") || resourceName.Contains("Houses/") || resourceName.Contains("Wall/")) assigned[i] = materials["Hills Concrete"];
                        else assigned[i] = material;
                    }
                    else if (pack == "ElderTreeGate")
                    {
                        if (sourceName.Contains("trunk") || sourceName.Contains("bark"))
                            assigned[i] = materials["Elder Trunk"];
                        else if (sourceName.Contains("grass")) assigned[i] = materials["Elder Grass"];
                        else if (sourceName.Contains("bush")) assigned[i] = materials["Forest Bush"];
                        else if (sourceName.Contains("stone")) assigned[i] = materials["Forest Boulder"];
                        else assigned[i] = materials["Elder Canopy"];
                    }
                    else if (pack == "JungleRuins")
                    {
                        assigned[i] = sourceName.Contains("bark")
                            ? materials["Wood Bark"]
                            : materials["Jungle Frond"];
                    }
                    else if (pack == "RunicForest" || pack == "ForestVillage")
                    {
                        // Both kits share Laya's naming, so one rule set covers them.
                        if (sourceName.Contains("pinetree_bark")) assigned[i] = materials["Pine Bark"];
                        else if (sourceName.Contains("pine_tree")) assigned[i] = materials["Pine Canopy"];
                        else if (sourceName.Contains("bark")) assigned[i] = materials["Wood Bark"];
                        else if (sourceName.Contains("branch")) assigned[i] = materials["Forest Branch"];
                        else if (sourceName.Contains("bush")) assigned[i] = materials["Forest Bush"];
                        else if (sourceName.Contains("roots")) assigned[i] = materials["Forest Roots"];
                        else if (sourceName.Contains("flower")) assigned[i] = materials["Forest Flowers"];
                        else if (sourceName.Contains("atlas") || sourceName.Contains("grass"))
                            assigned[i] = materials["Forest Undergrowth"];
                        else if (sourceName.Contains("plant")) assigned[i] = materials["Forest Fern"];
                        else if (sourceName.Contains("mountain")) assigned[i] = materials["Forest Mountain"];
                        else if (sourceName.Contains("rock")) assigned[i] = materials["Forest Boulder"];
                        else if (sourceName.Contains("leaves")) assigned[i] = materials["Broadleaf Canopy"];
                        else assigned[i] = material;
                    }
                    else if (pack == "RedCanyon")
                    {
                        // Palms and the bush split into a bark slot (MI_tree_bark*) and a
                        // foliage slot (MI_palm_tree / MI_branch) that has to be alpha clipped.
                        if (sourceName.Contains("bark")) assigned[i] = materials["Palm Bark"];
                        else if (sourceName.Contains("palm") || sourceName.Contains("branch") || sourceName.Contains("leaf"))
                            assigned[i] = materials["Palm Frond"];
                        else assigned[i] = material;
                    }
                    else assigned[i] = material;
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
            return model;
        }

        private GameObject PlaceBiomeModel(string pack, string resourceName, Material material,
            Vector3 position, Vector3 rotation, Vector3 scale, string label = null)
        {
            var model = BiomeModel(pack, resourceName, material);
            if (model == null) return null;
            model.name = label ?? resourceName;
            model.transform.position = position;
            model.transform.rotation = Quaternion.Euler(rotation);
            model.transform.localScale = scale;
            return model;
        }

        private GameObject PlaceBiomeModelOnRoad(string pack, string resourceName, Material material,
            float distance, float lateral, float height, Vector3 localEuler, Vector3 scale,
            string label = null, bool enforceClearance = true)
        {
            var model = BiomeModel(pack, resourceName, material);
            if (model == null) return null;
            model.name = label ?? resourceName;
            model.transform.position = RoadPath.Point(distance, lateral, height + RealGroundUnder(distance, lateral, 1.5f));
            model.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(localEuler);
            model.transform.localScale = scale;
            if (enforceClearance && Mathf.Abs(lateral) > 0.01f)
                EnsureOutsideRoad(model, distance, Mathf.Sign(lateral));
            SettleOnRealGround(model, distance, lateral, 1.5f);
            return model;
        }

        private GameObject PrimitiveOnRoad(PrimitiveType type, string name, float distance, float lateral,
            float height, Vector3 scale, Material material, Vector3 localEuler, bool enforceClearance = true)
        {
            var item = Primitive(type, name, RoadPath.Point(distance, lateral, height), scale, material);
            item.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(localEuler);
            if (enforceClearance && Mathf.Abs(lateral) > 0.01f)
                EnsureOutsideRoad(item, distance, Mathf.Sign(lateral));
            return item;
        }

        internal static bool TryGetCombinedBoundsPublic(GameObject item, out Bounds bounds) =>
            TryGetCombinedBounds(item, out bounds);

        private static bool TryGetCombinedBounds(GameObject item, out Bounds bounds)
        {
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private static void EnsureOutsideRoad(GameObject item, float distance, float side)
        {
            if (!TryGetCombinedBounds(item, out var bounds)) return;
            var roadCenter = RoadPath.Point(distance, 0f, 0f);
            var roadRight = RoadPath.Right(distance);
            var minProjection = float.PositiveInfinity;
            var maxProjection = float.NegativeInfinity;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                var projection = Vector3.Dot(corner - roadCenter, roadRight);
                minProjection = Mathf.Min(minProjection, projection);
                maxProjection = Mathf.Max(maxProjection, projection);
            }

            var clearance = RoadPath.ClearanceAt(distance);
            var shift = side > 0f
                ? Mathf.Max(0f, clearance - minProjection)
                : -Mathf.Max(0f, maxProjection + clearance);
            item.transform.position += roadRight * shift;
        }

        /// Scales by the longest horizontal axis instead of height - for things that are
        /// defined by how far they reach across the road (overpasses, fences, parked cars).
        /// Same road-relative rule as NormalizeModelHeight - see the note there.
        private static void NormalizeModelSpan(GameObject model, float targetSpan, float baseHeight)
        {
            if (!TryGetCombinedBounds(model, out var bounds)) return;
            var span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span < 0.01f) return;
            var groundY = model.transform.position.y;
            model.transform.localScale *= targetSpan / span;
            if (!TryGetCombinedBounds(model, out bounds)) return;
            model.transform.position += Vector3.up * (groundY - bounds.min.y);
            var dist = bounds.center.z;
            var roadRight = RoadPath.Right(dist);
            var roadCenter = RoadPath.Point(dist, 0f, 0f);
            var proj = Vector3.Dot(bounds.center - roadCenter, roadRight);
            if (Mathf.Abs(proj) > 0.01f)
                EnsureOutsideRoad(model, dist, Mathf.Sign(proj));
        }

        /// Scales a model to a target height and sits its base on the ground.
        /// A building mesh should not need to be blown up more than an order of
        /// magnitude to reach its target height. Beyond that the source is the wrong
        /// asset for the job and scaling it only produces something the wrong shape.
        private const float MinModelScale = 0.05f;
        private const float MaxModelScale = 12f;

        /// A static survives entering play mode when domain reload is off, so a
        /// catalogue built once would outlive the run that measured it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBuildingCatalogue()
        {
            buildingCatalogue = null;
            lowDetailOverride = null;
#if UNITY_EDITOR
            // Restore a deliberately forced budget across the play-mode boundary, but say
            // so loudly every single run. The risk this guards is a measurement taken
            // months later on a forced budget and recorded as the shipping desktop path;
            // a warning in the console every boot is what makes that impossible to miss.
            if (UnityEditor.EditorPrefs.HasKey(LowDetailPrefKey))
            {
                lowDetailOverride = UnityEditor.EditorPrefs.GetBool(LowDetailPrefKey);
                Debug.LogWarning($"RR_QUALITY detail budget is FORCED to low={lowDetailOverride} " +
                                 "by an editor pref, not by the platform. Any RR_COST or FPS " +
                                 "reading from this run is NOT the shipping desktop path. " +
                                 "Call RoadRageBootstrap.ClearDetailBudgetOverride() to drop it.");
            }
#endif
        }

        /// A candidate building mesh, measured in its own right.
        ///
        /// Sizing buildings by scaling whatever mesh a hash picked to a target height
        /// cannot work when the roster runs from 0.65 m props to 228 m city blocks: the
        /// same rule turns one into a 93x-wide monster and the other into a stub. So
        /// every candidate is measured first and used only where it fits.
        private struct BuildingEntry
        {
            public string Resource;
            public float Height;
            public float Width;   // larger horizontal extent
            public float Depth;   // smaller horizontal extent
            public BuildingClass Class;
        }

        /// What a mesh actually is, decided from its own proportions rather than from
        /// the list it happened to be written into.
        private enum BuildingClass
        {
            /// Too small or too squat to be a building. Never placed as one.
            NotABuilding,
            /// Fits a street plot: fronts the sidewalk.
            Frontage,
            /// Needs a deeper plot: sits behind the frontage line.
            MidBlock,
            /// A whole city block. Background skyline only, far from the road.
            Skyline
        }

        private static List<BuildingEntry> buildingCatalogue;

        /// Measures a prefab without instantiating it, by combining each mesh's local
        /// bounds through the hierarchy into the prefab root's space.
        private static bool TryMeasurePrefab(GameObject prefab, out Vector3 size)
        {
            size = Vector3.zero;
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return false;

            var toRoot = prefab.transform.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var found = false;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var local = toRoot * filter.transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = b.center + Vector3.Scale(b.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    var world = local.MultiplyPoint3x4(point);
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                    found = true;
                }
            }
            if (!found) return false;
            size = max - min;
            return size.y > 0.001f;
        }

        /// Only the genuinely-not-a-building test is absolute. Everything else is decided
        /// by rank once the whole set is measured - see AssignClasses.
        private static bool IsBuildingShaped(float height) => height >= 4f;

        /// Splits the measured set into thirds by width: narrowest front the street,
        /// widest go to the skyline.
        ///
        /// The first attempt used absolute cutoffs - under 34 m fronts the street, over
        /// 80 m is skyline - and produced zero frontage buildings, because nothing in
        /// this set is narrower than 34 m. Numbers picked from intuition about what a
        /// building "should" measure describe no particular asset set. Ranking cannot
        /// empty a bucket: whatever is narrowest here fronts the street, whatever that
        /// turns out to be.
        private static void AssignClasses(List<BuildingEntry> entries)
        {
            entries.Sort((a, b) => a.Width.CompareTo(b.Width));
            var third = Mathf.Max(1, entries.Count / 3);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                entry.Class = i < third ? BuildingClass.Frontage
                            : i < third * 2 ? BuildingClass.MidBlock
                            : BuildingClass.Skyline;
                entries[i] = entry;
            }
        }

        /// Built once per run from every mesh the city blocks can draw on, so placement
        /// picks by measured fit instead of by list position.
        private static List<BuildingEntry> BuildingCatalogue()
        {
            if (buildingCatalogue != null) return buildingCatalogue;
            buildingCatalogue = new List<BuildingEntry>();

            // Buildings/USA/building is out. It is a detached, furnished American house -
            // its own folder ships a sofa, a table and a carpet next to it - and
            // AssignClasses ranks by width, so being the narrowest thing in the set put
            // it straight into the Frontage bucket and onto a Manhattan avenue. The
            // catalogue only serves BuildCyberSprawl, so nothing else loses it.
            var candidates = new List<string>(NycVariants);
            for (var i = 1; i <= 8; i++) candidates.Add($"Buildings/NYC/building_{i}");
            candidates.Add("Buildings/NYCBlock6/builds");
            candidates.Add("Buildings/NYCBlock6/shops");

            // Architecture the NYC set does not contain.
            //
            // Everything above is the NYC-Like City Buildings Set: nineteen NYCVariants
            // prefabs and eight FBXs, all assembled from the same eight part families -
            // building_1..8 in bottom, middle and roof - wearing the same three materials.
            // A street built from that is the same building over and over however it is
            // recoloured, which is what tinting seven ways could not fix and what
            // RR_CITY's counters proved was not a placement bug: the set is simply small.
            //
            // DemoCity is a whole city pack already in Resources, already used by this
            // biome for its benches and trees, so its materials resolve. The mid-rise
            // houses and office blocks are street-fronting buildings with silhouettes
            // nothing in the NYC set has.
            //
            // Left out deliberately: small_house_1..3 for the same reason
            // Buildings/USA/building was dropped, the factory buildings and chimney
            // because an avenue is not an industrial estate, and the _bgr variants
            // because those are flat background stand-ins.
            for (var i = 1; i <= 5; i++) candidates.Add($"Buildings/DemoCity/mid_house_{i}");
            candidates.Add("Buildings/DemoCity/mid_house_1_2");
            candidates.Add("Buildings/DemoCity/mid_house_4_2");
            for (var i = 1; i <= 4; i++) candidates.Add($"Buildings/DemoCity/office_building_{i}");

            var rejected = 0;
            foreach (var resource in candidates)
            {
                var prefab = Resources.Load<GameObject>(resource);
                if (prefab == null) continue;
                if (!TryMeasurePrefab(prefab, out var size)) continue;

                var width = Mathf.Max(size.x, size.z);
                var depth = Mathf.Min(size.x, size.z);
                if (!IsBuildingShaped(size.y))
                {
                    rejected++;
                    Debug.LogWarning($"[CITY] rejected {resource}: {size.y:0.00}m tall, {width:0.0}x{depth:0.0}m - not a building");
                    continue;
                }
                buildingCatalogue.Add(new BuildingEntry
                {
                    Resource = resource, Height = size.y, Width = width, Depth = depth
                });
            }
            AssignClasses(buildingCatalogue);

            return buildingCatalogue;
        }

        /// Let a building keep the material its own mesh shipped with.
        ///
        /// Which material that is takes some tracing, and I got it wrong the first time.
        /// BuildingCatalogue loads Resources/Buildings/NYC/building_1..8, and those FBX
        /// importers are set to external materials with a recursive-up name search
        /// (materialLocation 0, materialSearch 1). So Unity resolves each submesh against
        /// Resources/Buildings/NYC/Materials, the folder sitting beside the FBX. The
        /// source pack's own Materials folder is never read at runtime, and neither are
        /// its Prefabs - nothing under Resources points into them.
        ///
        /// That resolved set was stripped. Every one of its surface materials was on
        /// URP/Lit with an empty _BaseMap and a white _BaseColor, so a wall arrived flat
        /// white and whatever tint this pass applied became the entire surface. That is
        /// the real reason the city read as one flat colour per building, and why the
        /// facade family here had to exist at all.
        ///
        /// Tools/MaterialTextures/rebind.py put the bindings back, taking them from each
        /// pack's intact twin of the same name under its Models/Materials - the set the
        /// FBX was authored against - so a brick wall is photographed brick at the tiling
        /// its UVs were laid out for. 22 materials across the NYC and USA biomes.
        ///
        /// Walls only, and by falling through rather than by naming: lamps, signs,
        /// billboards, glass, windows and trim are all matched by the branches above this
        /// one and keep the game's own night look. The eight window and light materials
        /// in the resolved set are still deliberately unbound, because City Windows and
        /// City Neon replace them.
        ///
        /// Set false to go back to the flat facade family in one edit.
        private const bool KeepPackWallMaterials = true;

        /// The facades a city block can be built from, chosen per building.
        private static readonly string[] FacadeFamily =
        {
            "City Brick", "City Limestone", "City Sandstone", "City Concrete", "City Glass Tower"
        };

        /// Picks a facade deterministically, so a block looks the same every time its
        /// chunk is rebuilt while neighbouring plots differ from each other.
        private Material FacadeMaterial(int hash) =>
            materials[FacadeFamily[(hash & 0x7fffffff) % FacadeFamily.Length]];

        /// Does this material describe a building wall? The wall branch of the material
        /// pass hands the caller's choice through only for these, so a prop that happens
        /// to arrive with some other material still gets the default treatment.
        private static bool IsFacadeMaterial(string name) =>
            System.Array.IndexOf(FacadeFamily, name) >= 0
            || name == "City Skyline" || name == "Cyber Skyline";

        /// City Windows is one shared material carrying one procedural pane-lit texture
        /// (WindowEmissionGrid, seed 77031) so every window submesh in the game samples
        /// the same 512px sheet - that is what makes every Manhattan tower show the exact
        /// same lit/unlit fingerprint side by side, sharing one material is what keeps the
        /// biome's draw calls low, so the fix is not a texture per building.
        ///
        /// The grid tiles cleanly in 12 x 12 cells, so shifting the sample by a whole
        /// number of cells picks a different lit pattern without moving pane boundaries
        /// inside a window - no seam, no distortion, one texture throughout, just a
        /// different offset. The offset lives in a small pool of materials rather than in
        /// per-renderer instance data; see VaryWindowLighting for why the property block
        /// that used to carry it was costing far more than the materials do.
        /// How many lit-window patterns are in circulation. Twelve is enough that a
        /// street does not repeat within sight; the old per-building offset drew from
        /// 144 combinations, which nobody could tell apart from twelve at driving speed.
        private const int WindowVariants = 12;

        /// The variant pool, and the material it was built from. BuildMaterials makes new
        /// Material objects on every biome reload, so a pool cached against the old one
        /// would tint windows with a material nothing else in the world is using. Keyed on
        /// the source so a reload rebuilds it; also covers a domain reload nulling both.
        private static Material[] windowPool;
        private static Material windowPoolSource;

        private static Material[] WindowPool(Material cityWindows)
        {
            if (windowPool != null && windowPoolSource == cityWindows) return windowPool;

            // Index 0 is the shared asset itself, so a twelfth of buildings add no
            // material at all and the pool costs eleven.
            var pool = new Material[WindowVariants];
            pool[0] = cityWindows;
            for (var i = 1; i < WindowVariants; i++)
            {
                // 5 and 7 are coprime with 12, so u and v each walk the whole grid and
                // the pair does not repeat across the pool.
                var variant = new Material(cityWindows)
                {
                    name = $"{cityWindows.name} Lit {i}",
                };
                variant.SetTextureOffset("_BaseMap",
                    new Vector2(i * 5 % WindowVariants / (float)WindowVariants,
                                i * 7 % WindowVariants / (float)WindowVariants));
                pool[i] = variant;
            }
            windowPool = pool;
            windowPoolSource = cityWindows;
            Debug.Log($"RR_WINDOWS built {WindowVariants} lit-window variants from " +
                      $"{cityWindows.name}");
            return pool;
        }

        /// Gives each building its own lit-window pattern by swapping in one of a small
        /// pool of materials.
        ///
        /// This used to set _BaseMap_ST through a MaterialPropertyBlock, which is the
        /// right move under the built-in pipeline and the wrong one under URP: a renderer
        /// carrying a property block is skipped by the SRP Batcher. Manhattan is where
        /// that costs the most - measured at 30,667 draw calls for a 7.1 ms GPU frame
        /// inside a 28.4 ms one, so the biome was submitting far more than it was
        /// drawing - and the buildings this ran on are exactly what the batcher should be
        /// collapsing.
        ///
        /// Swapping sharedMaterials keeps the batcher, for the same reason WeatherWalls
        /// swaps rather than overrides. The cost is eleven materials for the whole biome.
        private static void VaryWindowLighting(GameObject model, Material cityWindows, int hash)
        {
            if (model == null || cityWindows == null) return;
            var pool = WindowPool(cityWindows);
            var salted = unchecked(hash * -1640531527); // hash ^ golden-ratio multiplier,
            var chosen = pool[(salted & 0x7fffffff) % WindowVariants];
            if (chosen == cityWindows) return;

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var current = renderer.sharedMaterials;
                Material[] swapped = null;
                for (var i = 0; i < current.Length; i++)
                {
                    if (current[i] != cityWindows) continue;
                    swapped ??= (Material[])current.Clone();
                    swapped[i] = chosen;
                }
                if (swapped != null) renderer.sharedMaterials = swapped;
            }
        }

        /// The three wall surfaces that read as a building exterior, and so are worth
        /// weathering. The pack's other wall materials - rope, plastic, wood - are trim.
        private static readonly string[] GrimedWalls =
        {
            "TexturesCom_Brick_Modern_1K_albedo",
            "TexturesCom_Plaster_Rough_1K_albedo",
            "TexturesCom_Paint_Epoxy_1K_albedo",
        };

        /// How many weathered variants Tools/GrimeBake/bake.py writes per wall.
        ///
        /// Resources.Load is used rather than a serialised reference because nothing in
        /// this world is authored in a scene. If a variant is ever missing, the lookup
        /// keeps only the clean material and this method leaves every wall alone, which
        /// is the behaviour before the variants existed.
        private const int GrimeVariants = 3;

        /// Facade tints, multiplied into a wall's albedo. Index 0 is the untinted
        /// original, so a seventh of buildings wear the pack texture as it ships.
        ///
        /// KeepPackWallMaterials hands every building in the pack its own wall material,
        /// which is why FacadeMaterial's five-colour family never reaches a wall: the
        /// pack branch returns sourceMaterial before the facade branch is reached. That
        /// was the right call - the pack textures are far better than the flat family -
        /// but it means one brick, one plaster and one epoxy across the whole biome, and
        /// a street of identical colour.
        ///
        /// These stay close to white because they multiply a photographic albedo: a
        /// saturated tint reads as coloured light on the wall rather than as a different
        /// building. Painted brick, limestone, sandstone, soot-grey, verdigris and a cold
        /// steel-blue, all of which a Manhattan block genuinely contains.
        private static readonly Color[] FacadeTints =
        {
            new Color(1.00f, 1.00f, 1.00f),   // as the pack ships it
            new Color(0.84f, 0.62f, 0.54f),   // red brick
            new Color(0.86f, 0.83f, 0.75f),   // aged limestone
            new Color(0.90f, 0.80f, 0.64f),   // sandstone
            new Color(0.62f, 0.61f, 0.62f),   // soot
            new Color(0.68f, 0.74f, 0.70f),   // verdigris
            new Color(0.58f, 0.64f, 0.74f),   // cold steel
        };

        /// Tinted wall materials, keyed on the material they were tinted from. Built on
        /// demand and shared, so a biome costs at most six extra materials per wall it
        /// actually uses rather than one per building.
        private Dictionary<Material, Material[]> wallTints;

        /// Does this material name describe a wall worth tinting?
        ///
        /// GrimedWalls names the NYCBlock6 pack's three walls. Manhattan is not built
        /// from those: its buildings are the NYCVariants prefabs, whose parts wear
        /// materials called brick, concrate and window out of the NYC-Like City Buildings
        /// Set. So the grime bake and the tint both keyed on names nothing in the biome
        /// was wearing, which is why the street stayed one shade of brown after both.
        ///
        /// Matched by substring, the same way the material assignment pass already
        /// identifies a wall, rather than by an exact list that has to be kept in step
        /// with whichever pack a biome happens to be built from.
        private static bool IsTintableWall(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("window") || n.Contains("glass") || n.Contains("light")
                || n.Contains("neon") || n.Contains("metal") || n.Contains("wood")) return false;
            return n.Contains("brick") || n.Contains("concrate") || n.Contains("concrete")
                || n.Contains("plaster") || n.Contains("epoxy") || n.Contains("stucco")
                || n.Contains("highrise");
        }

        private Material TintedWall(Material source, int tint)
        {
            if (tint <= 0 || source == null) return source;
            wallTints ??= new Dictionary<Material, Material[]>();
            if (!wallTints.TryGetValue(source, out var variants))
            {
                variants = new Material[FacadeTints.Length];
                variants[0] = source;
                wallTints[source] = variants;
            }
            if (variants[tint] == null)
            {
                var tinted = new Material(source) { name = $"{source.name} Tint {tint}" };
                // Swapping a material rather than setting a property block, for the same
                // reason VaryWindowLighting does: a property block takes the renderer out
                // of the SRP Batcher, which cost Manhattan 4,573 SetPass calls the last
                // time something here did it.
                tinted.SetColor("_BaseColor", FacadeTints[tint]);
                variants[tint] = tinted;
            }
            return variants[tint];
        }

        /// Wall material name to its variants, index 0 being the clean original.
        /// Rebuilt on demand: a domain reload wipes this while the chunks that used it
        /// survive, which is the same trap that produced duplicate worlds earlier.
        private Dictionary<string, Material[]> wallGrime;

        /// Weathers a building, so a street is not a row of identically clean facades.
        ///
        /// The NYC pack shipped this variation as HDRP decal projectors. Drawing those
        /// under URP would mean adding the Decal Renderer Feature, a screen-space pass,
        /// and hundreds of live projectors per block, in a biome already over its
        /// triangle budget. Baking the same stains into the albedo costs nothing at all
        /// at render time - the wall was going to sample a texture either way.
        ///
        /// One level per building rather than per section, so a tower is weathered
        /// consistently up its height while its neighbours differ. A quarter of buildings
        /// draw the clean original, because a street where everything is stained reads as
        /// uniformly as one where nothing is.
        ///
        /// This swaps sharedMaterials rather than using a MaterialPropertyBlock: the
        /// variants are different textures, not different parameters, so there is nothing
        /// a property block could override. It costs one material per wall per grime
        /// level, which is twelve materials across the biome.
        private void WeatherWalls(GameObject model, int hash)
        {
            if (model == null) return;
            if (wallGrime == null)
            {
                wallGrime = new Dictionary<string, Material[]>();
                foreach (var wall in GrimedWalls)
                {
                    var clean = Resources.Load<Material>($"Buildings/NYC/Materials/{wall}");
                    if (clean == null) continue;
                    var variants = new List<Material> { clean };
                    for (var v = 1; v <= GrimeVariants; v++)
                    {
                        var grimed = Resources.Load<Material>(
                            $"Buildings/NYC/Materials/{wall}_grime{v}");
                        if (grimed != null) variants.Add(grimed);
                    }
                    wallGrime[wall] = variants.ToArray();
                }
            }
            var pick = (hash & 0x7fffffff);
            // Salted apart from the grime pick, so weathering and colour do not move
            // together and a street cannot end up with all its clean walls one shade.
            var tint = (unchecked(hash * 0x27d4eb2d) & 0x7fffffff) % FacadeTints.Length;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var current = renderer.sharedMaterials;
                Material[] swapped = null;
                for (var i = 0; i < current.Length; i++)
                {
                    var source = current[i];
                    if (source == null) continue;
                    // Grime first, where a baked variant exists for this wall, then the
                    // tint on whatever came out. The two are independent: a wall with no
                    // grime bake still gets a colour, which is the whole of Manhattan.
                    var chosen = source;
                    if (wallGrime.TryGetValue(source.name, out var variants) && variants.Length >= 2)
                        chosen = variants[pick % variants.Length];
                    if (IsTintableWall(chosen.name))
                    {
                        var before = chosen;
                        chosen = TintedWall(chosen, tint);
                        if (chosen != before) tintedWalls++;
                    }
                    if (chosen == source) continue;
                    swapped ??= (Material[])current.Clone();
                    swapped[i] = chosen;
                }
                if (swapped != null) renderer.sharedMaterials = swapped;
            }
        }

        /// Class/plot combinations already reported. Reset with the rest of the
        /// domain-reload state so a fresh run reports again.
        private static readonly HashSet<string> fitReported = new();

        private static readonly string[] AwningPalette =
        {
            "Awning Red", "Awning Green", "Awning Navy", "Awning Burgundy", "Awning Gold"
        };

        /// A shop awning: a slab jutting from the wall at door height plus a short drop
        /// valance at its outer lip, the two visual cues that read as "awning" rather than
        /// "ledge" from a moving car. Two Cube primitives - the same GameObject.
        /// CreatePrimitive() path already used for lamp posts - so this is ~24 triangles
        /// total, not worth measuring against a biome that costs 3M+ a chunk.
        ///
        /// Placed at a fixed size and a fixed offset from the frontage line rather than
        /// fitted to the building behind it: there is no door position to fit to (the NYC
        /// meshes carry no submesh that identifies one), so this is a recurring street
        /// fixture like the lamp posts and traffic lights beside it, not an attachment.
        private void BuildStorefrontAwning(GameObject building, float distance, float side, int hash)
        {
            // No wall, no awning. This is what left canopies hanging in mid-air: the
            // awning went up on every block that passed the hash test, while
            // PlaceBuildingOnPlot returns null whenever nothing in the catalogue fits the
            // plot - so a block with no frontage building still got a canopy, attached to
            // nothing.
            if (building == null || !TryGetCombinedBounds(building, out var bounds)) return;

            // And measure where the facade actually ended up rather than trusting the line
            // it was asked to meet. PRODUCTION-GATES section 8 calls "requested bounds are
            // not measured bounds" this project's recurring fault; an awning positioned off
            // a constant while the wall behind it is positioned off its own geometry is
            // exactly that shape of bug waiting to happen.
            var roadRight = RoadPath.Right(distance);
            var roadCentre = RoadPath.Point(distance, 0f, 0f);
            var facade = float.PositiveInfinity;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                facade = Mathf.Min(facade, Vector3.Dot(corner - roadCentre, roadRight) * side);
            }
            if (float.IsInfinity(facade)) return;

            var colour = materials[AwningPalette[(hash & 0x7fffffff) % AwningPalette.Length]];
            const float overhang = 1.25f;
            const float embed = 0.2f;      // buried in the wall, so no gap can open up
            const float aboveDoor = 3.15f;
            // Height off the building's own base rather than off the road, so a canopy
            // cannot drift when the two sit at different elevations.
            var baseHeight = bounds.min.y - roadCentre.y + aboveDoor;

            var depth = overhang + embed;

            // The shop itself, which was missing. A Manhattan block is a continuous strip
            // of glazed ground floor under blank masonry, and this biome had the masonry
            // running all the way to the pavement - so the street read as warehouses with
            // canopies stuck on. The awning was built first and hung on nothing.
            //
            // Glazing sits just proud of the measured facade rather than on it, because
            // two coplanar surfaces z-fight, and it wears City Windows so a shop is lit
            // from inside at night like the storeys above it.
            var groundY = bounds.min.y - roadCentre.y;
            var glassLateral = side * (facade - 0.12f);
            var glass = materials.TryGetValue("City Windows", out var lit) ? lit : colour;
            PrimitiveOnRoad(PrimitiveType.Cube, "Storefront Glazing", distance, glassLateral,
                groundY + 1.6f, new Vector3(0.22f, 2.4f, 5.4f), glass, Vector3.zero, false);

            // Mullions, so a 5.4 m pane reads as a shopfront rather than as one sheet.
            for (var mullion = -1; mullion <= 1; mullion += 2)
                PrimitiveOnRoad(PrimitiveType.Cube, "Storefront Mullion",
                    distance + mullion * 1.35f, side * (facade - 0.2f),
                    groundY + 1.6f, new Vector3(0.18f, 2.5f, 0.16f),
                    materials["City Asphalt Trim"], Vector3.zero, false);

            // Fascia above the glass, in the shop's own colour, where a name would go.
            PrimitiveOnRoad(PrimitiveType.Cube, "Storefront Sign", distance,
                side * (facade - 0.16f), groundY + 3.55f,
                new Vector3(0.3f, 0.8f, 5.4f), colour, Vector3.zero, false);

            // Not every storefront has an awning out front. One in five bare fronts keeps
            // the rhythm from turning into wallpaper - the shop below it is always there.
            if (hash % 5 == 0) return;

            var canopyLateral = side * (facade + (embed - overhang) * 0.5f);
            PrimitiveOnRoad(PrimitiveType.Cube, "Storefront Awning", distance, canopyLateral,
                baseHeight, new Vector3(depth, 0.14f, 4.5f), colour, Vector3.zero, false);

            var valanceLateral = side * (facade - overhang);
            PrimitiveOnRoad(PrimitiveType.Cube, "Storefront Awning Valance", distance, valanceLateral,
                baseHeight - 0.32f, new Vector3(0.06f, 0.5f, 4.5f), colour, Vector3.zero, false);
        }

        /// Screen height below which a background tower is drawn as a box instead of as
        /// its full mesh. Screen-relative, so it is an apparent-size rule rather than a
        /// distance one: with a 60 degree field of view this swaps a 60 m tower at roughly
        /// 870 m and a 25 m one at roughly 360 m. Lower it to keep full detail further out
        /// at more cost; raise it to save more and swap sooner. One number, one place.
        private const float SkylineImpostorHeight = 0.06f;

        /// Draws a distant skyline tower as a single box.
        ///
        /// Nothing in this biome had an LODGroup, so every tower cost the same at 200 m as
        /// at 20 m. Measured: the NYCVariants prefabs stack a mean of 5.1 sections of
        /// 18-25k triangles, so one tower is around 100k, and BuildCyberSprawl places about
        /// 24 buildings per 150 m chunk - roughly 2.4M of the 3.16M triangles a chunk.
        ///
        /// The stand-in is a cube, 12 triangles, wearing the same facade material. That is
        /// a real loss of detail, which is why this is applied only to MidBlock towers:
        /// they sit behind the frontage line and read as skyline, while the street
        /// frontages the player drives past keep their full mesh at every distance.
        ///
        /// The box is left unparented so Adopt puts it under the chunk root, whose
        /// transform is identity - world size and local scale are then the same number,
        /// and none of the rotated-parent arithmetic that PRODUCTION-GATES section 8 calls
        /// this project's recurring fault is needed. It is destroyed with the chunk like
        /// anything else under that root, and buildings never move after placement.
        private void AddSkylineImpostor(GameObject model, Material facade)
        {
            var detailed = model.GetComponentsInChildren<Renderer>(true);
            if (detailed.Length == 0 || !TryGetCombinedBounds(model, out var bounds)) return;

            var box = Primitive(PrimitiveType.Cube, "Skyline Impostor",
                bounds.center, bounds.size, facade);
            if (box == null) return;
            var boxRenderer = box.GetComponent<Renderer>();
            if (boxRenderer == null) return;
            // Beyond the shadow distance by the time it is showing, and a box's shadow
            // would not match the silhouette it stands in for anyway.
            boxRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var group = model.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(SkylineImpostorHeight, detailed),
                // Zero, not a cull threshold: a tower that vanished at the edge of the
                // streamed world would pop a hole in the skyline. It stays a box.
                new LOD(0f, new[] { boxRenderer }),
            });
            group.RecalculateBounds();
        }

        /// Places a building on a plot rather than scaling it into a gap.
        ///
        /// Two things make a street read as a street: every facade meets the pavement on
        /// one continuous line, and a building is never wider than the plot it stands on.
        /// The old code did neither - it centred each building at a random lateral and
        /// then scaled it to a target height, so facades zigzagged and an oversized mesh
        /// simply grew until EnsureOutsideRoad shoved it away from the road, leaving the
        /// gap between the buildings and the pavement.
        ///
        /// Here the mesh is chosen to fit the plot, scaled only within a band that keeps
        /// it recognisable, and then positioned by its street-facing face.
        private GameObject PlaceBuildingOnPlot(BuildingClass wanted, Material material, string label,
            float distance, float side, float frontageLine, float plotWidth, int hash)
        {
            var catalogue = BuildingCatalogue();
            var fitting = new List<BuildingEntry>();
            foreach (var entry in catalogue)
            {
                if (!ClassUsableFor(entry.Class, wanted)) continue;
                // Usable if it can be brought within the plot without shrinking so far
                // that it stops reading as a building.
                if (entry.Width * MinPlotScale <= plotWidth) fitting.Add(entry);
            }
            if (fitting.Count == 0) return null;

            // How many distinct buildings this plot size can actually draw on. "Every
            // building looks the same" is a number, not an impression, and it is this one
            // - the catalogue can hold sixty entries while a 30 m plot fits six of them.
            // Once per class and width, not per building.
            if (fitReported.Add($"{wanted}/{plotWidth:0}"))
                Debug.Log($"RR_CITY {wanted} plots {plotWidth:0}m wide can use " +
                          $"{fitting.Count} of {catalogue.Count} catalogued buildings");

            var chosen = fitting[Mathf.Abs(hash) % fitting.Count];

            // Scale to fill the plot's width, capped so a small mesh is not blown up and
            // a large one is not shrunk into a model. Height follows - the proportions
            // of a real building are not ours to invent.
            var scale = Mathf.Clamp(plotWidth / chosen.Width, MinPlotScale, MaxPlotScale);

            // Same mesh, different building. The catalogue only offers what fits the plot,
            // so a street runs through its options fast and then repeats them - which is
            // the "copy paste buildings" of it. Stretching the storey height per plot
            // changes the silhouette and the window rhythm without another asset, and
            // buildings genuinely differ in floor height. Y only: the model is rotated
            // about Y alone, so local Y is world up and nothing skews.
            var storey = 0.85f + (hash >> 5) % 41 / 100f;   // 0.85 to 1.25
            var footprint = Vector3.one * scale;
            footprint.y *= storey;

            var facing = side > 0f ? -90f : 90f;
            var model = PlaceBiomeModelOnRoad("Buildings", chosen.Resource, material,
                distance, side * (frontageLine + chosen.Depth * scale * 0.5f), 0f,
                new Vector3(0f, facing, 0f), footprint, label, enforceClearance: false);
            if (model == null) return null;

            // Ground it, then set the street-facing face exactly on the frontage line so
            // the whole block shares one facade.
            if (TryGetCombinedBounds(model, out var bounds))
            {
                var roadRight = RoadPath.Right(distance);
                var roadCenter = RoadPath.Point(distance, 0f, 0f);
                var nearest = float.PositiveInfinity;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    var projection = Vector3.Dot(corner - roadCenter, roadRight) * side;
                    nearest = Mathf.Min(nearest, projection);
                }
                model.transform.position += roadRight * (side * (frontageLine - nearest));

                if (TryGetCombinedBounds(model, out bounds))
                    model.transform.position += Vector3.up *
                        (RoadPath.Point(distance, 0f, 0f).y - bounds.min.y + 0.05f);
            }
            if (materials.TryGetValue("City Windows", out var cityWindows))
                VaryWindowLighting(model, cityWindows, hash);
            WeatherWalls(model, hash);
            if (wanted == BuildingClass.MidBlock) AddSkylineImpostor(model, material);
            return model;
        }

        /// Scale band for a building on a plot. Outside this the mesh stops looking like
        /// the thing it was modelled as, which is worse than an imperfect fit.
        /// Distance from the road centreline to the facade line. Road half-width plus
        /// the shoulder and the pavement - every frontage meets the pavement here.
        /// Distance from the road centreline to the facade line.
        ///
        /// Was 17.5, and the city kerb sits at 1.20-1.24 x half width - 16.2 to 16.74 m
        /// on Manhattan's 27 m road. So the buildings began 0.76 m behind the kerb and
        /// stood on the whole of the 8 m sidewalk ribbon the road builder was drawing
        /// underneath them. There was no pavement to walk on, and every piece of street
        /// furniture had been placed at 13-14.5 m to stay clear of the buildings - which
        /// is inside the kerb, so the hydrants, lamps, trees and benches were all standing
        /// in the road.
        ///
        /// 23 m leaves 6.3 m of pavement between kerb and facade, which is a Manhattan
        /// sidewalk, and gives the furniture somewhere to stand that is not the gutter.
        private const float FrontageSetback = 23f;
        /// Whether a catalogued building of one class may be placed on another's plot.
        ///
        /// The class is a size hint, not a licence. AssignClasses ranks the whole set by
        /// width and cuts it in thirds, so a frontage plot could only ever draw on the
        /// narrowest third - 21 of 64 - and a street runs through 21 buildings in a
        /// couple of blocks. That is the measurable half of "every building looks the
        /// same": RR_CITY has been printing the number all along.
        ///
        /// A MidBlock-width building that scales into a 30 m plot is a perfectly good
        /// frontage, and MinPlotScale already refuses anything that would have to shrink
        /// out of proportion to fit, so the fit test is the real guard and the class
        /// filter was only narrowing the draw. Neighbouring classes are now allowed;
        /// Skyline entries still stay off the street, because those are whole city blocks
        /// and nothing about them reads at 30 m.
        private static bool ClassUsableFor(BuildingClass entry, BuildingClass wanted) =>
            entry == wanted
            || (wanted == BuildingClass.Frontage && entry == BuildingClass.MidBlock)
            || (wanted == BuildingClass.MidBlock && entry != BuildingClass.NotABuilding);

        private const float MinPlotScale = 0.55f;
        private const float MaxPlotScale = 1.9f;

        /// Mesh names already reported as badly sized.
        ///
        /// The warning below fires per placement, and Manhattan normalises street
        /// furniture, lamps, traffic lights, shelters and rooftop props - hundreds per
        /// world build, more with every chunk streamed in. That is a console at 999+
        /// warnings within a minute of pressing Play, which is not a diagnostic, it is a
        /// place other diagnostics go to hide: it is what buried the empty material
        /// palette (fixed in 22729e0) and a prop pointing at a model that did not exist
        /// (8ca67b1) for an entire session.
        ///
        /// The signal is per mesh, not per instance - the same mesh clamped four hundred
        /// times is one fact - so it is reported once per name. Cleared on
        /// SubsystemRegistration below, so entering play mode reports afresh rather than
        /// staying silent about a mesh a previous run already named.
        private static readonly HashSet<string> clampReported = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClampReports()
        {
            clampReported.Clear();
            fitReported.Clear();
        }

        private static void NormalizeModelHeight(GameObject model, float targetHeight, float groundHeight = 0.05f,
            float maxFootprint = 0f)
        {
            if (!TryGetCombinedBounds(model, out var bounds) || bounds.size.y < 0.01f) return;
            var groundY = model.transform.position.y;

            // Uniform scale to a target height only behaves when the roster's meshes are
            // roughly comparable. This one spans 0.65 m to 228 m native, so reaching a
            // 60 m building from a 0.65 m mesh takes 93x - which widens and deepens it by
            // 93x too, running its walls out through the sidewalk and across the road.
            // That is the "buildings not on the ground" report: they are grounded exactly
            // right and simply enormous. Cap the factor and take a shorter building over
            // a misshapen one.
            var wanted = targetHeight / bounds.size.y;
            var scale = Mathf.Clamp(wanted, MinModelScale, MaxModelScale);
            if (!Mathf.Approximately(scale, wanted) && clampReported.Add(model.name))
                Debug.LogWarning($"[CITY] {model.name} native height {bounds.size.y:0.00}m needs {wanted:0.0}x " +
                                 $"for {targetHeight:0.0}m - clamped to {scale:0.0}x. This mesh does not belong " +
                                 "in a building roster. Reported once per mesh name per run.");
            model.transform.localScale *= scale;
            if (!TryGetCombinedBounds(model, out bounds)) return;

            // Height alone does not describe a building. Several of these meshes are
            // whole city blocks, so normalising them to a plausible height left
            // footprints of 111 m and 167 m across - dropped onto a plot about 25 m wide
            // between the kerb and the block behind. They swallowed the sidewalk and ran
            // into their neighbours. Scale down further to fit the plot, keeping the
            // proportions, and accept a shorter building.
            if (maxFootprint > 0f)
            {
                var widest = Mathf.Max(bounds.size.x, bounds.size.z);
                if (widest > maxFootprint)
                {
                    model.transform.localScale *= maxFootprint / widest;
                    if (!TryGetCombinedBounds(model, out bounds)) return;
                }
            }
            model.transform.position += Vector3.up * (groundY - bounds.min.y + groundHeight);
            var dist = bounds.center.z;
            var roadRight = RoadPath.Right(dist);
            var roadCenter = RoadPath.Point(dist, 0f, 0f);
            var proj = Vector3.Dot(bounds.center - roadCenter, roadRight);
            if (Mathf.Abs(proj) > 0.01f)
                EnsureOutsideRoad(model, dist, Mathf.Sign(proj));
        }

        /// Hard-pins a model's world-space bounds base to the road surface at distance.
        /// Call after NormalizeModelHeight when the FBX pivot may not be at the mesh base.
        private static void SnapToGround(GameObject model, float distance)
        {
            if (!TryGetCombinedBounds(model, out var bounds)) return;
            var roadY = RoadPath.Point(distance, 0f, 0f).y;
            var gap = roadY - bounds.min.y;
            if (Mathf.Abs(gap) > 0.02f)
                model.transform.position += Vector3.up * gap;
        }

        /// The garage was a grid of text buttons over the running world. This builds an
        /// actual showroom: the browsed vehicle on a lit turntable in front of its own
        /// camera, so you look at the truck you are buying rather than reading its name.
        public Camera ShowroomCamera;
        private Transform showroomStage;
        private int showroomCar = -999;

        public void EnsureShowroom(int carIndex)
        {
            if (ShowroomCamera == null)
            {
                // Parked far from the road so the streamed world never intersects it.
                var rig = new GameObject("Showroom").transform;
                rig.position = new Vector3(0f, -4000f, 0f);

                var camObj = new GameObject("Showroom Camera");
                camObj.transform.SetParent(rig, false);
                camObj.transform.localPosition = new Vector3(0f, 2.2f, -9.6f);
                camObj.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
                ShowroomCamera = camObj.AddComponent<Camera>();
                ShowroomCamera.clearFlags = CameraClearFlags.SolidColor;
                ShowroomCamera.backgroundColor = new Color(0.035f, 0.04f, 0.055f);
                ShowroomCamera.fieldOfView = 38f;
                ShowroomCamera.depth = 5f;
                ShowroomCamera.enabled = false;

                var key = new GameObject("Showroom Key").AddComponent<Light>();
                key.transform.SetParent(rig, false);
                key.type = LightType.Directional;
                key.transform.rotation = Quaternion.Euler(34f, 152f, 0f);
                key.intensity = 2.1f;
                key.color = new Color(1f, 0.96f, 0.9f);

                var rim = new GameObject("Showroom Rim").AddComponent<Light>();
                rim.transform.SetParent(rig, false);
                rim.type = LightType.Directional;
                rim.transform.rotation = Quaternion.Euler(12f, -35f, 0f);
                rim.intensity = 1.3f;
                rim.color = new Color(0.55f, 0.72f, 1f);

                // A plinth so the vehicle is not floating in void.
                var floor = Primitive(PrimitiveType.Cylinder, "Showroom Plinth",
                    rig.position + Vector3.down * 0.5f, new Vector3(9f, 0.5f, 9f),
                    materials["Shoulder"]);
                floor.transform.SetParent(rig, true);

                showroomStage = new GameObject("Turntable").transform;
                showroomStage.SetParent(rig, false);

                // Directional lights light the whole world, wherever they are parked:
                // left on, these two lit the road as two extra suns from the first
                // garage visit onwards, and the road and car paint burned out to
                // white. They are on only while the showroom camera is.
                var gate = rig.gameObject.AddComponent<ShowroomLightGate>();
                gate.Camera = ShowroomCamera;
                gate.Lights = new[] { key, rim };
                key.enabled = rim.enabled = false;
            }

            if (showroomCar == carIndex) return;
            showroomCar = carIndex;
            // Immediate destroy: the deferred variant let the previous browsed car
            // linger in the turntable for a frame - and visibly clip through the new
            // one whenever the GPU raced the end-of-frame teardown.
            for (var i = showroomStage.childCount - 1; i >= 0; i--)
                DestroyImmediate(showroomStage.GetChild(i).gameObject);

            var spec = GameState.Cars[Mathf.Clamp(carIndex, 0, GameState.Cars.Length - 1)];
            var prefab = Resources.Load<GameObject>($"Vehicles/{spec.Mesh}");
            if (prefab == null) return;

            // Showroom materials are built FRESH, and BOTH paint slots use the
            // browsed car's own livery sheet - each livery is a full-car texture
            // (body + trim + wheels), and most presets keep their visible body panels
            // in the chassis slot, which is exactly why every browsed car used to
            // render in the blue base atlas.
            var browsedLivery = Resources.Load<Texture2D>($"Vehicles/{spec.Livery}")
                             ?? Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_01_A");
            var paint = MakeMaterial("Showroom Paint", Color.white, 0.42f, 0.82f);
            if (browsedLivery != null)
            {
                paint.mainTexture = browsedLivery;
                if (paint.HasProperty("_BaseMap")) paint.SetTexture("_BaseMap", browsedLivery);
            }
            var showroomChassis = MakeMaterial("Showroom Chassis", Color.white, 0.30f, 0.55f);
            if (browsedLivery != null)
            {
                showroomChassis.mainTexture = browsedLivery;
                if (showroomChassis.HasProperty("_BaseMap")) showroomChassis.SetTexture("_BaseMap", browsedLivery);
            }

            var visual = Instantiate(prefab, showroomStage);
            SmoothVehicleMeshes(visual);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var assigned = new Material[src.Length];
                for (var i = 0; i < assigned.Length; i++)
                {
                    var slot = src[i] != null ? src[i].name.ToLowerInvariant() : string.Empty;
                    assigned[i] = slot.Contains("glass") ? materials["Street Racer Glass"]
                        : slot.Contains("livery") ? paint
                        : showroomChassis;
                }
                r.sharedMaterials = assigned;
            }
            // Wheels last: the slot pass above would overwrite their rubber/rim pair.
            ReplaceWheelMeshes(visual);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
            // Frame every vehicle the same: bikes and semis differ by 4x in length.
            if (TryGetCombinedBounds(visual, out var b) && b.size.magnitude > 0.01f)
            {
                var longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                visual.transform.localScale *= 6.0f / Mathf.Max(0.01f, longest);
                if (TryGetCombinedBounds(visual, out b))
                    visual.transform.localPosition -= new Vector3(0f, b.min.y - showroomStage.position.y, 0f);
                // Big vehicles need a wider showcase orbit - the fixed 4.7 m radius
                // put the camera inside semis and box trucks.
                RoadRageLandingDirector.ShowcaseRadiusScale =
                    Mathf.Clamp(b.size.magnitude / 7f, 1f, 1.9f);
            }
        }

        /// Spins the turntable and toggles the showroom camera from the HUD.
        public void SetShowroomActive(bool active, float spinDegrees = 0f)
        {
            if (ShowroomCamera == null) return;
            ShowroomCamera.enabled = active;
            if (active && showroomStage != null)
                showroomStage.localRotation = Quaternion.Euler(0f, spinDegrees, 0f);
        }

        // ---- Streaming ------------------------------------------------------------
        // The road is endless: chunks are built ahead of the player and destroyed behind.
        // Zones map absolute distance onto biomes, so a run travels *through* biomes
        // (Greenwood -> Tire District -> Neon City -> ...) instead of lapping one.

        private const float ChunkLength = 150f;
        private const int ChunksAhead = 6;
        private const int ChunksBehind = 1;
        /// Distance a single biome occupies before the next begins.
        // 1800 m was ~70 s per biome - Hollywood turned into Neon City before it
        // registered as anywhere. 5400 m is ~3.7 min, so a zone reads as a place.
        private const float ZoneLength = 5400f;
        /// Order a journey visits biomes, starting from whichever the player picked.
        private static readonly int[] JourneyOrder = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        private readonly Dictionary<int, GameObject> liveChunks = new();
        private int journeyStart;
        private int chunkSeed;

        /// Lanes per direction. Forest, canyon and the sewer tunnel run a two-lane road
        /// (one each way) so the surroundings can crowd it; cities keep three each way.
        /// Three lanes each way, everywhere. Half width is lanes x 4.5 m, so every biome
        /// now runs the 27 m profile the road constants were written for.
        ///
        /// This used to vary: one lane on the country and hillside roads, two through the
        /// sewer and the snow highway. Two things had to move with it. ScatterBand now
        /// refuses to place inside the carriageway, because twenty-two roadside bands were
        /// written as absolute distances that a 4.5 m half width cleared and a 13.5 m one
        /// does not - they would have been standing on the tarmac. And the sewer tunnel
        /// was widened, because its walls stood at 13.4 m and the road they enclose is now
        /// 13.5 m to the kerb: the carriageway would have been wider than the tunnel.
        ///
        /// Greenwood was the last single-lane road, and a truck in the only lane each way
        /// could not be got past: it now has two each way (18 m of carriageway).
        private static int LaneCountFor(int biomeIndex) => biomeIndex == 0 || biomeIndex == CanalTownIndex ? 2 : 3;

        private static float HalfWidthFor(int biomeIndex) =>
            LaneCountFor(biomeIndex) * RoadPath.LaneWidth;

        private static RoadRoute greenwoodRoute;

        /// Greenwood drives the real Schwarzwaldhochstrasse (B500), Baden-Baden to
        /// Freudenstadt and back: its bends and hills, fitted to this road by
        /// Tools/Terrain/build_b500_road.py. Every other biome keeps the procedural
        /// road. Set before any chunk is built, since everything placed along the
        /// road reads its shape through RoadPath.
        private static void ApplyBiomeRoute(int biomeIndex)
        {
            if (biomeIndex != 0)
            {
                RoadPath.Route = null;
                return;
            }
            greenwoodRoute ??= RoadRoute.Load("Biomes/Routes/b500");
            if (greenwoodRoute == null) Debug.LogWarning("Missing Biomes/Routes/b500 - Greenwood keeps the procedural road.");
            else Debug.Log($"RR_ROUTE Greenwood follows the B500: {greenwoodRoute.Length / 1000f:0.0} km, there and back");
            RoadPath.Route = greenwoodRoute;
        }

        /// Smoothly interpolated so the carriageway tapers across a zone seam. The taper
        /// straddles the boundary, which is also where the gateway stands.
        /// How much each biome's road wanders. Greenwood is a mountain pass, so it gets
        /// sweeping bends; everything else keeps the road it had. Blended across a zone
        /// seam exactly like the half width, because a step change in curvature at a
        /// boundary is a kink in the road, and the gateway stands right on it.
        private static float CurveScaleFor(int biomeIndex) =>
            biomeIndex == 0 ? 3.0f : biomeIndex == CanalTownIndex ? 0.35f : 1f;

        private float CurveScaleAtDistance(float distance)
        {
            const float taper = 260f;
            var zone = ZoneIndexAt(distance);
            var boundary = (zone + 1) * ZoneLength;
            var here = CurveScaleFor(BiomeIndexAt(distance));
            var toBoundary = boundary - distance;
            if (toBoundary > taper * 0.5f) return here;
            var next = CurveScaleFor(BiomeIndexAt(boundary + 10f));
            var t = Mathf.InverseLerp(taper * 0.5f, -taper * 0.5f, toBoundary);
            return Mathf.Lerp(here, next, Mathf.SmoothStep(0f, 1f, t));
        }

        private static float ElevationScaleFor(int biomeIndex) =>
            biomeIndex == 0 ? 1.8f : biomeIndex == CanalTownIndex ? 0.15f : 1f;

        private float ElevationScaleAtDistance(float distance)
        {
            const float taper = 260f;
            var zone = ZoneIndexAt(distance);
            var boundary = (zone + 1) * ZoneLength;
            var here = ElevationScaleFor(BiomeIndexAt(distance));
            var toBoundary = boundary - distance;
            if (toBoundary > taper * 0.5f) return here;
            var next = ElevationScaleFor(BiomeIndexAt(boundary + 10f));
            var t = Mathf.InverseLerp(taper * 0.5f, -taper * 0.5f, toBoundary);
            return Mathf.Lerp(here, next, Mathf.SmoothStep(0f, 1f, t));
        }

        private float HalfWidthAtDistance(float distance)
        {
            const float taper = 260f;
            var zone = ZoneIndexAt(distance);
            var boundary = (zone + 1) * ZoneLength;
            var here = HalfWidthFor(BiomeIndexAt(distance));
            var toBoundary = boundary - distance;
            if (toBoundary > taper * 0.5f) return here;
            var next = HalfWidthFor(BiomeIndexAt(boundary + 10f));
            var t = Mathf.InverseLerp(taper * 0.5f, -taper * 0.5f, toBoundary);
            return Mathf.Lerp(here, next, Mathf.SmoothStep(0f, 1f, t));
        }

        private int ZoneIndexAt(float distance) =>
            Mathf.FloorToInt(Mathf.Max(0f, distance) / ZoneLength);

        /// A run stays in the biome the player picked. It used to move on to the next
        /// biome in JourneyOrder every ZoneLength (5.4 km), so a Greenwood drive turned
        /// into Snow Station part-way. Changing biome is the picker's and the N key's
        /// job. Everything that asks about the biome ahead (curves, elevation, road
        /// width, mood blending) goes through here, so they all see one biome too.
        public int BiomeIndexAt(float distance) => JourneyOrder[journeyStart];

        public string BiomeNameAt(float distance) => Biomes[BiomeIndexAt(distance)];

        /// -profile logs per-chunk render cost; -nocanopy skips the forest canopy bands
        /// so overdraw can be separated from raw geometry cost. Diagnostics only.
        private static bool ProfileChunks;
        private static bool NoCanopy;
        private static bool LogSky;
        /// -nobloom. Bloom is off for the whole run when set, which is how its cost is
        /// measured on a device that is not making frame rate.
        private static bool BloomDisabled;
        /// -noreflections. Suppresses the reflection probe build, its capture schedule and
        /// the renderers' probe sampling, so the probe's cost can be measured on its own.
        public static bool ReflectionsEnabled { get; private set; } = true;
        /// -noshadows. Shadows are now enabled on the low tier, where the quality level used
        /// to have them off, so the sweep needs to be able to isolate that as well.
        private static bool ShadowsDisabled;

        /// The measurement Gate A was decided on: renderers, triangles and how many of
        /// those renderers are alpha-tested. Greenwood measured 850 / 824 at the time.
        private static void LogChunkCost(GameObject root, string biome, float seg)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            long tris = 0;
            var cutouts = 0;
            foreach (var r in renderers)
            {
                if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                {
                    var mesh = mf.sharedMesh;
                    for (var s = 0; s < mesh.subMeshCount; s++) tris += mesh.GetIndexCount(s) / 3;
                }
                foreach (var m in r.sharedMaterials)
                    if (IsAlphaClipped(m)) { cutouts++; break; }
            }
            Debug.Log($"RR_COST {biome} seg={seg:0}: renderers={renderers.Length} tris={tris} cutoutRenderers={cutouts}");
        }

        /// Is this material alpha-clipped? Three tests, because no single one holds.
        ///
        /// The keyword alone was the original test and it is not dependable on a material
        /// built at runtime with new Material(): PRODUCTION-GATES section 8 already records
        /// that URP's stripper never scans those, and one Greenwood session reported
        /// cutout=0, cutout=1693 and cutout=4886 for the same biome while the leaves
        /// visibly clipped correctly the whole time. A cost probe that swings by 4886 on
        /// identical content is not measuring the content.
        ///
        /// _AlphaClip is what URP's shader actually branches on, and the AlphaTest queue
        /// is where it puts the result, so either standing alone still means clipped.
        private static bool IsAlphaClipped(Material m)
        {
            if (m == null) return false;
            if (m.IsKeywordEnabled("_ALPHATEST_ON")) return true;
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) return true;
            return m.renderQueue >= 2225 && m.renderQueue <= 2500;
        }

        /// Measures every chunk currently loaded and totals them.
        ///
        /// The cost log was reachable only through a -profile command-line flag, which
        /// cannot be passed from the editor and which Gate C names as a smell in its own
        /// right: no system should need a flag to exercise. Since the foliage work has to
        /// be judged on this number rather than on how a frame looks, it needs to be one
        /// keypress away from wherever the player already is.
        /// Names the meshes carrying the triangle load, worst first.
        ///
        /// Reported per mesh AND per instance: a mesh drawn 40 times at 5k triangles and
        /// one drawn twice at 100k need opposite fixes - thin the placement, or replace
        /// or decimate the asset - and a total alone cannot tell them apart.
        /// Names an object and its parents up to the chunk, so a mesh called Cube.008 is
        /// reported as whatever the placement code called the thing holding it.
        private static string OwnerTrail(Transform t)
        {
            var trail = t.name;
            var parent = t.parent;
            for (var depth = 0; parent != null && depth < 3; depth++)
            {
                if (parent.name.StartsWith("Chunk ")) break;
                trail = parent.name + "/" + trail;
                parent = parent.parent;
            }
            return trail;
        }

        /// Merges a prop's child meshes into one batch, so it costs draw calls in
        /// proportion to its materials rather than to how many pieces it was modelled in.
        ///
        /// The NYC roof sets are the reason this exists. RR_PLACEMENT measured NYC Rooftop
        /// Water Tank at 7,675 renderers - 66.3% of everything Manhattan submits - for
        /// 709k triangles, 92 apiece. They are not water tanks but whole rooftop clutter
        /// sets shipped as one FBX of roughly 85 separate objects, and every object was
        /// its own submission, on roofs 28-35 m up, seen from a car in a canyon. Top of
        /// the renderer ranking and nowhere on the triangle one is exactly the shape a
        /// draw call problem takes.
        ///
        /// Combine bakes the children's transforms into shared buffers, so it has to run
        /// after the model is positioned and scaled - after NormalizeModelHeight, not
        /// before - and the children must not move afterwards. Nothing moves these.
        ///
        /// It reads the source vertex data, which is why the nine roof FBXs are imported
        /// with Read/Write enabled. That keeps a CPU copy of about 9 MB of mesh; worth
        /// watching, and cheap against two thirds of the biome's submissions.
        private static void CombineChildRenderers(GameObject model)
        {
            if (model == null) return;
            StaticBatchingUtility.Combine(model);
        }

        /// The name of the object the placement code created, found by walking up to the
        /// direct child of the chunk root. PlaceBiomeModelOnRoad and PlaceBuildingOnPlot
        /// both name what they spawn, so this is the label a row can be acted on by.
        private static string PlacementName(Transform t)
        {
            var node = t;
            while (node.parent != null && !node.parent.name.StartsWith("Chunk ")) node = node.parent;
            return node.name;
        }

        /// Ranks placements by renderer count.
        ///
        /// The triangle ranking answers "what is heavy", which is the right question for
        /// a GPU-bound biome. Manhattan was neither: 30,667 draw calls for a 7.1 ms GPU
        /// frame inside a 28.4 ms one, so what mattered was how many things were being
        /// submitted, and nothing in the log said. Triangles are still printed per row,
        /// so the two rankings can be read against each other - a placement high here and
        /// low there is cheap geometry submitted too many times, which is the shape a
        /// draw call problem takes.
        private static void LogBusiestPlacements(Dictionary<string, int> renderersByPlacement,
                                                 Dictionary<string, long> trisByPlacement,
                                                 long totalRenderers)
        {
            if (renderersByPlacement.Count == 0 || totalRenderers <= 0) return;

            var ranked = new List<KeyValuePair<string, int>>(renderersByPlacement);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            Debug.Log($"RR_PLACEMENT busiest of {renderersByPlacement.Count} placements " +
                      $"({totalRenderers} renderers total):");
            // All of them, not a top ten. The rows that answer "did the thing I just
            // added actually get placed" are the small ones at the bottom, and a set
            // piece every fifth block is never going to outrank a building.
            var shown = ranked.Count;
            for (var i = 0; i < shown; i++)
            {
                var name = ranked[i].Key;
                var count = ranked[i].Value;
                trisByPlacement.TryGetValue(name, out var tris);
                Debug.Log($"RR_PLACEMENT {100f * count / totalRenderers,5:0.0}%  {count,6} renderers  " +
                          $"{tris / 1000,7}k tris  ({(float)tris / count / 1000f:0.0}k each)  {name}");
            }
        }

        private static void LogHeaviestMeshes(Dictionary<string, long> trisByMesh,
                                              Dictionary<string, int> countByMesh,
                                              Dictionary<string, string> exampleOwner, long totalTris)
        {
            if (trisByMesh.Count == 0 || totalTris <= 0) return;

            var ranked = new List<KeyValuePair<string, long>>(trisByMesh);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            // One entry a mesh, on one line each. The ranking used to be a single
            // multi-line Debug.Log, and the Console list only ever showed its first two
            // lines - so the report that names what is expensive showed the header and
            // the worst offender, and hid the other nine behind a click nobody knew to
            // make. The owner trail rides on the same line rather than a second one.
            Debug.Log($"RR_MESH heaviest of {trisByMesh.Count} distinct meshes " +
                      $"({totalTris / 1000}k triangles total):");
            var shown = Mathf.Min(HeaviestMeshCount, ranked.Count);
            for (var i = 0; i < shown; i++)
            {
                var name = ranked[i].Key;
                var tris = ranked[i].Value;
                var instances = countByMesh.TryGetValue(name, out var n) ? n : 0;
                var each = instances > 0 ? tris / instances : tris;
                var owner = exampleOwner.TryGetValue(name, out var o) ? o : "?";
                Debug.Log($"RR_MESH {100f * tris / totalTris,5:0.0}%  {tris / 1000,7}k tris  " +
                          $"x{instances,-4} ({each / 1000f:0.0}k each)  {name}  in: {owner}");
            }
        }

        private const int HeaviestMeshCount = 10;

        /// Frames discarded after lifting the cap, then frames averaged. The first frames
        /// after uncapping still carry the capped pacing, and a single frame is noise.
        private const int CostProbeWarmupFrames = 30;
        private const int CostProbeSampleFrames = 120;
        private bool costProbeRunning;

        /// The keypress path only works while the Game view holds keyboard focus. In the
        /// Editor that is easy to lose - clicking the Console to filter for RR_COST takes
        /// it away - and a P pressed anywhere else never reaches Update, so the
        /// measurement silently does not happen and the Console stays empty. Road
        /// Rage/Measure Live World Cost calls this directly, with no focus to lose.
        public static void MeasureLiveWorldCost()
        {
            var bootstrap = Instance;
            if (bootstrap == null)
            {
                Debug.LogWarning("RR_COST no bootstrap in the scene. Enter play mode first.");
                return;
            }
            bootstrap.LogLiveWorldCost();
        }

        private void LogLiveWorldCost()
        {
            if (costProbeRunning)
            {
                Debug.Log("RR_COST probe already running.");
                return;
            }
            // Announce the start. The sample takes a couple of seconds, during which a
            // trigger that fired and a trigger that never arrived look identical - which
            // is exactly the ambiguity that left an empty Console unexplained.
            Debug.Log($"RR_COST probe started: {CostProbeWarmupFrames} warmup + " +
                      $"{CostProbeSampleFrames} sampled frames, uncapped.");
            StartCoroutine(CostProbe());
        }

        /// Counts are read immediately; the frame rate is sampled with the cap lifted.
        ///
        /// Boot sets Application.targetFrameRate = 120, and this log used to report a
        /// single 1/unscaledDeltaTime against it. Three Greenwood runs whose geometry
        /// differed by 3x - 675, 675 and 240 renderers a chunk, 19.6M down to 6.7M
        /// triangles - all reported 121-123 FPS, because none of them was measuring
        /// anything except the ceiling. The capture path already knew this (see
        /// FrameCapture.Initialize) and uncapped; the keypress path never did.
        private System.Collections.IEnumerator CostProbe()
        {
            costProbeRunning = true;

            // Walk the scene, not liveChunks. A domain reload can empty that dictionary
            // while the chunk objects keep rendering, and a probe that trusts it reports
            // half the world - which is how a Greenwood hierarchy holding two full sets of
            // Chunk -1..6 still logged chunks=8.
            long totalRenderers = 0, totalCutouts = 0, totalTris = 0;
            var chunkRoots = new List<GameObject>();
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go != null && go.name.StartsWith("Chunk ")) chunkRoots.Add(go);

            // Triangles per mesh, so the log names what is expensive instead of only
            // saying how much there is. Manhattan reporting 3.2M triangles a chunk is a
            // number nobody can act on; the same total attributed to a handful of meshes
            // is a decision about those meshes.
            var trisByMesh = new Dictionary<string, long>();
            var countByMesh = new Dictionary<string, int>();
            // One example owner per mesh. A mesh name alone can be unidentifiable: the NYC
            // pack ships meshes called Cube.008 and Light1, its .meta files record no name
            // table, and the FBXs are LFS-stored - so nothing in the repo says what they
            // are. The placement code does name what it spawns ("NYC Street Lamp",
            // "Manhattan Midtown Skyscraper"), so recording one owning object identifies
            // the call site that produced the cost.
            var exampleOwner = new Dictionary<string, string>();
            // Renderers per placement, which is a different question from triangles per
            // mesh and the one Manhattan actually turned on. Its frame was CPU-bound on
            // submission while the triangle ranking pointed at buildings, so a ranking by
            // triangle is the wrong map for a draw call problem. Keyed by the name the
            // placement code gave the object, so a row names a call site.
            var renderersByPlacement = new Dictionary<string, int>();
            var trisByPlacement = new Dictionary<string, long>();
            // Combined static batches already counted. StaticBatchingUtility.Combine puts
            // every child of a prop onto one shared mesh and gives each renderer a range
            // of it, so reading sharedMesh per renderer counts the whole batch once per
            // child. The NYC roof props reported 709k triangles before they were combined
            // and 61,344k after, from geometry that did not change by one triangle. Unity
            // names these meshes "Combined Mesh (root: ...)", which is the only handle
            // there is - a renderer's range within its batch is not public.
            var countedBatches = new HashSet<Mesh>();

            foreach (var chunk in chunkRoots)
            {
                var renderers = chunk.GetComponentsInChildren<Renderer>(true);
                totalRenderers += renderers.Length;
                foreach (var r in renderers)
                {
                    var placement = PlacementName(r.transform);
                    renderersByPlacement.TryGetValue(placement, out var placed);
                    renderersByPlacement[placement] = placed + 1;
                    if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null
                        && (!mf.sharedMesh.name.StartsWith("Combined Mesh") || countedBatches.Add(mf.sharedMesh)))
                    {
                        long meshTris = 0;
                        for (var sm = 0; sm < mf.sharedMesh.subMeshCount; sm++)
                            meshTris += mf.sharedMesh.GetIndexCount(sm) / 3;
                        totalTris += meshTris;
                        trisByPlacement.TryGetValue(placement, out var placedTris);
                        trisByPlacement[placement] = placedTris + meshTris;

                        var meshName = mf.sharedMesh.name;
                        trisByMesh.TryGetValue(meshName, out var running);
                        trisByMesh[meshName] = running + meshTris;
                        countByMesh.TryGetValue(meshName, out var instances);
                        countByMesh[meshName] = instances + 1;
                        if (!exampleOwner.ContainsKey(meshName))
                            exampleOwner[meshName] = OwnerTrail(r.transform);
                    }
                    foreach (var m in r.sharedMaterials)
                        if (IsAlphaClipped(m)) { totalCutouts++; break; }
                }
            }

            var priorTarget = Application.targetFrameRate;
            var priorVsync = QualitySettings.vSyncCount;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            for (var i = 0; i < CostProbeWarmupFrames; i++) yield return null;

            var elapsed = 0f;
            var worstFrame = 0f;
            for (var i = 0; i < CostProbeSampleFrames; i++)
            {
                yield return null;
                var dt = Time.unscaledDeltaTime;
                elapsed += dt;
                if (dt > worstFrame) worstFrame = dt;
            }

            Application.targetFrameRate = priorTarget;
            QualitySettings.vSyncCount = priorVsync;

            var chunks = Mathf.Max(1, chunkRoots.Count);
            var avgFps = CostProbeSampleFrames / Mathf.Max(elapsed, 0.0001f);
            var worstFps = 1f / Mathf.Max(worstFrame, 0.0001f);
            // Say so when the streamer has lost track of part of what it is drawing:
            // every count above is then real, but the world is not the one intended.
            var orphans = chunkRoots.Count - liveChunks.Count;
            if (orphans != 0)
                Debug.LogWarning($"RR_COST {chunkRoots.Count} chunk roots in the scene but " +
                                 $"{liveChunks.Count} tracked ({orphans:+#;-#;0} untracked). The counts " +
                                 "below are what renders; the difference is a leak, not content.");
            // Gate A wants a sustained figure, so the worst frame in the window is the
            // one that decides the gate - an average hides exactly the stalls that fail it.
            // The 850 / 824 baseline is Greenwood's alpha-test canopy and means nothing
            // anywhere else, so it is only printed where it applies.
            var baseline = biomeName == "GREENWOOD" ? " (Gate A measured Greenwood at 850 / 824)" : "";
            // One Debug.Log a line, not one call with embedded newlines. The Console list
            // shows the first two lines of an entry and hides the rest behind a click, so
            // a three-line result read as two lines with the frame rate - the number the
            // probe exists to produce - silently missing.
            Debug.Log($"RR_COST {biomeName} live: chunks={chunkRoots.Count} tracked={liveChunks.Count} " +
                      $"renderers={totalRenderers} cutout={totalCutouts} tris={totalTris}");
            Debug.Log($"RR_COST per chunk: renderers={totalRenderers / chunks} " +
                      $"cutout={totalCutouts / chunks}{baseline}");
            Debug.Log($"RR_COST uncapped over {CostProbeSampleFrames} frames: avg={avgFps:0.0} fps " +
                      $"worst={worstFps:0.0} fps budget={(RichDetailBudget ? "rich" : "low")} " +
                      $"(was capped at {priorTarget})");

            LogHeaviestMeshes(trisByMesh, countByMesh, exampleOwner, totalTris);
            LogBusiestPlacements(renderersByPlacement, trisByPlacement, totalRenderers);

            costProbeRunning = false;
        }

        /// Reset by the same domain reload that empties liveChunks, so the sweep below
        /// runs again exactly when the dictionary has been lost.
        private bool orphanChunkSweepDone;

        /// Destroys chunk roots this instance is not tracking.
        ///
        /// liveChunks is a plain Dictionary, so a recompile during play mode empties it
        /// while the chunk GameObjects it referenced stay in the scene. The streamer then
        /// finds no chunks, rebuilds every one, and the world quietly renders twice - two
        /// full sets of Chunk -1..6 in the hierarchy, double the foliage, and a cost probe
        /// that walks liveChunks reporting only half of what is actually drawn.
        /// Rebuilds the material palette if a domain reload emptied it.
        ///
        /// materials and liveChunks are both plain instance fields, so recompiling while
        /// play mode runs resets both while every GameObject they described survives. The
        /// chunk half of that is handled by PurgeOrphanChunks above; this is the other
        /// half, and it was doing quiet damage: with the dictionary empty the streamer
        /// still rebuilt the world, and every materials[key] lookup fell through to the
        /// flat fallback. A Greenwood built in that state reported cutout=0 across 5585
        /// renderers, against cutout=4886 for the same biome after a ReloadBiome - not a
        /// keyword that reads differently, but a forest genuinely wearing solid brown
        /// stand-ins with no alpha clip on any of them.
        ///
        /// ReloadBiome recovered by accident, because step 5 calls BuildMaterials itself.
        /// Streaming had no such step.
        private void EnsureMaterialsBuilt()
        {
            if (materials.Count > 0) return;
            Debug.LogWarning("RR_MATERIALS palette was empty at stream time - rebuilding. A " +
                             "domain reload (a recompile during play) clears it while the world " +
                             "it built stays in the scene.");
            dryRoadColors.Clear();
            dryRoadSmoothness.Clear();
            BuildMaterials();
        }

        private void PurgeOrphanChunks()
        {
            orphanChunkSweepDone = true;
            var purged = 0;
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go == null || !go.name.StartsWith("Chunk ")) continue;
                if (liveChunks.ContainsValue(go)) continue;
                go.SetActive(false);
                Destroy(go);
                purged++;
            }
            if (purged > 0)
                Debug.LogWarning($"RR_STREAM purged {purged} orphaned chunk root(s) left by a domain " +
                                 "reload. Without this the world renders twice and every RR_COST " +
                                 "reading understates what is on screen.");
        }

        private void BuildChunk(int index)
        {
            if (!orphanChunkSweepDone) PurgeOrphanChunks();
            EnsureMaterialsBuilt();
            if (liveChunks.ContainsKey(index)) return;
            var start = index * ChunkLength;
            var biomeIndex = BiomeIndexAt(start + ChunkLength * 0.5f);

            var root = new GameObject($"Chunk {index} [{Biomes[biomeIndex]}]");
            liveChunks[index] = root;

            segStart = start;
            segEnd = start + ChunkLength;
            chunkRoot = root.transform;
            // Deterministic per chunk: a chunk rebuilt later looks identical, and two
            // chunks never share a layout. Biome builders fold this into their own
            // seeds so their dressing varies chunk to chunk instead of replaying.
            chunkSeed = index * 7919 + biomeIndex * 104729 + 17;
            Random.InitState(chunkSeed);

            BuildRoad(biomeIndex);
            BuildEnvironment(biomeIndex);

            if (ProfileChunks)
            {
                LogChunkCost(root, Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)], segStart);
            }

            ClearRoadCorridor(chunkRoot);

            // Restore the world-wide context: anything built after this (traffic, the
            // player rig, the camera) must not inherit the chunk's parent or its range.
            chunkRoot = null;
            segStart = 0f;
            segEnd = WorldLength;
        }

        /// Geometry changes at a zone seam, but lighting crossfades over the last stretch
        /// of the outgoing zone so the biome change reads as travelling into somewhere
        /// new rather than a cut.
        private const float ZoneBlend = 320f;
        private Light sunLight;
        private ColorAdjustments zoneGrading;
        private Tonemapping zoneTonemap;
        private Bloom zoneBloom;

        /// Global brightness lift on every biome's post exposure. The post pipeline
        /// (ACES tonemapping, vignette, SSAO) renders measurably darker than the
        /// pre-post build ever did; this claws back an even amount across all biomes
        /// without re-tuning each mood one by one. Trimmed down from 0.45 - stacked on
        /// top of Hollywood/Red Canyon's already-high sun intensity it blew both out.
        private const float GlobalExposureLift = 0.2f;
        /// Was -20, stacked on top of every biome's own negative saturation (and
        /// Manhattan's -75) it flattened the whole game toward grayscale. Removed so
        /// biomes read with actual colour instead of looking washed out.
        private const float GlobalSaturationOffset = 0f;

        private float nextHitAndRunAt = -1f;

        /// Stages a hit-and-run every so often: a violator ahead rams a civilian, spins
        /// it into a wreck and bolts. The player sees the crash happen and gets a
        /// marked, personal quarry - the original game's "there's the bad man, GET HIM"
        /// moment, rather than ambient traffic noise.
        private void TryStageHitAndRun(float speedKph)
        {
            if (nextHitAndRunAt < 0f) nextHitAndRunAt = Time.unscaledTime + 8f;
            if (Time.unscaledTime < nextHitAndRunAt) return;
            if (speedKph < 55f) return;
            if (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive) return;

            TrafficCarController offender = null;
            TrafficCarController victim = null;
            var offenderGap = float.MaxValue;
            foreach (var traffic in TrafficCarController.All)
            {
                if (traffic == null || traffic.IsWreck || traffic.Direction < 0f) continue;
                if (!traffic.IsViolator || traffic.IsFleeing) continue;
                var gap = traffic.GapToPlayer;
                if (gap < 90f || gap > 230f || gap >= offenderGap) continue;
                // A civilian close beside the offender is the mark.
                TrafficCarController mark = null;
                foreach (var other in TrafficCarController.All)
                {
                    if (other == null || other == traffic || other.IsViolator || other.IsWreck) continue;
                    if (other.Direction < 0f) continue;
                    if (Mathf.Abs(other.GapToPlayer - gap) > 30f) continue;
                    if (Mathf.Abs(other.LaneOffset - traffic.LaneOffset) > 3.2f) continue;
                    mark = other;
                    break;
                }
                if (mark == null) continue;
                offender = traffic;
                victim = mark;
                offenderGap = gap;
            }

            nextHitAndRunAt = Time.unscaledTime + (offender == null ? 3f : Random.Range(13f, 21f));
            if (offender == null) return;

            victim.Crash(Mathf.Sign(victim.LaneOffset - offender.LaneOffset), 42f);
            CrashEffects.Active?.PlayAt(victim.transform.position + Vector3.up * 0.8f);
            if (RoadRageAudioBridge.Instance != null)
                RoadRageAudioBridge.Instance.PlayCrash(0.9f);
            var shove = offender.LaneOffset - victim.LaneOffset;
            offender.BeginHitAndRun(shove >= 0f ? 1f : -1f);
            GameState.Show("HIT & RUN AHEAD - GET HIM");
            Debug.Log($"RR_EVENT hitandrun staged t={Time.unscaledTime:0} offenderGap={offenderGap:0}m speedKmh={speedKph:0}");
        }

        private void BlendZoneLighting(float playerDistance)
        {
            var here = Mood(BiomeIndexAt(playerDistance));
            var intoNext = playerDistance - (ZoneIndexAt(playerDistance) * ZoneLength + ZoneLength - ZoneBlend);
            if (intoNext > 0f)
            {
                var next = Mood(BiomeIndexAt(playerDistance + ZoneLength));
                here = LerpMood(here, next, Mathf.Clamp01(intoNext / ZoneBlend));
            }

            var weather = WeatherSystem.EffectFor(activeWeather);
            RenderSettings.fogDensity = here.FogDensity * weather.FogDensityScale;
            RenderSettings.fogColor = Color.Lerp(here.Fog, weather.FogTint, weather.FogTintAmount);
            // ambientIntensity is only honoured when the ambient source is Skybox; in
            // Trilight Unity uses the three colours as given and ignores it entirely,
            // so setting it was a no-op. Fold the gain into the colours instead.
            var gain = here.AmbientIntensity * AmbientTrim;
            RenderSettings.ambientSkyColor = ScaleRgb(Color.Lerp(here.Sky, weather.FogTint, weather.FogTintAmount * 0.6f), gain);
            RenderSettings.ambientEquatorColor = ScaleRgb(Color.Lerp(here.Equator, weather.FogTint, weather.FogTintAmount * 0.4f), gain);
            RenderSettings.ambientGroundColor = ScaleRgb(here.Ground, gain);
            if (sunLight != null)
            {
                sunLight.color = here.SunColor;
                sunLight.intensity = here.SunIntensity * weather.SunScale;
            }
            ApplyRoadWetness(Mathf.Clamp01(here.RoadWetness + weather.WetnessAdd));
            // The grade has to ride with the mood: without this a zone crossing keeps
            // the start biome's saturation, contrast and exposure for the whole run.
            if (zoneGrading != null)
            {
                zoneGrading.postExposure.Override(here.PostExposure + weather.ExposureAdd + GlobalExposureLift);
                zoneGrading.contrast.Override(here.Contrast != 0f ? here.Contrast : 6f);
                zoneGrading.saturation.Override(
                    (here.Saturation != 0f ? here.Saturation : -2f) + GlobalSaturationOffset);
            }
            if (zoneTonemap != null)
                zoneTonemap.mode.Override(here.TonemapAces == 1 ? TonemappingMode.ACES : TonemappingMode.Neutral);
            // Same reasoning as the grade two blocks up: bloom is per-biome and has to move
            // with the mood, or the border zone keeps the bloom of whichever biome the run
            // started in.
            ApplyBloom(here);
        }

        private static BiomeMood LerpMood(BiomeMood a, BiomeMood b, float t) => new()
        {
            FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
            Fog = Color.Lerp(a.Fog, b.Fog, t),
            Sky = Color.Lerp(a.Sky, b.Sky, t),
            Equator = Color.Lerp(a.Equator, b.Equator, t),
            Ground = Color.Lerp(a.Ground, b.Ground, t),
            SunColor = Color.Lerp(a.SunColor, b.SunColor, t),
            SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
            PostExposure = Mathf.Lerp(a.PostExposure, b.PostExposure, t),
            BloomIntensity = Mathf.Lerp(a.BloomIntensity, b.BloomIntensity, t),
            BloomThreshold = Mathf.Lerp(a.BloomThreshold, b.BloomThreshold, t),
            RoadWetness = Mathf.Lerp(a.RoadWetness, b.RoadWetness, t),
            AmbientIntensity = Mathf.Lerp(a.AmbientIntensity, b.AmbientIntensity, t),
            Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
            TonemapAces = t >= 0.5f ? b.TonemapAces : a.TonemapAces,
        };

        /// Landmark at a zone seam: an overpass you drive under, on concrete piers, with a
        /// sign gantry just before it. Enclosed biomes get a tunnel portal instead.

        /// Final safety net: nothing may overhang the driving corridor.
        ///
        /// Placement helpers enforce clearance at spawn, but many call sites then run
        /// NormalizeModelHeight, which rescales the mesh and silently invalidates that
        /// clearance. A Kowloon tenement normalised to 34 m tall became 47 m WIDE with its
        /// centre 12 m off the road - so it spanned the carriageway and the chase camera
        /// drove through the inside of a building. Re-checking at every call site is
        /// error-prone, so the whole chunk is swept once after it is built.
        ///
        /// Objects are pushed outward by measured bounds; anything that cannot be pushed a
        /// sane distance is removed rather than left blocking the view.
        internal static int corridorPushed;
        internal static int corridorRemoved;

        private void ClearRoadCorridor(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                if (!TryGetCombinedBounds(child.gameObject, out var bounds)) continue;
                // Ground and road ribbons legitimately span the corridor.
                var n = child.name;
                if (n.Contains("Ground") || n.Contains("Road") || n.Contains("Ribbon") ||
                    n.Contains("Asphalt") || n.Contains("Shoulder") || n.Contains("Dash") ||
                    n.Contains("Paint") || n.Contains("Verge") || n.Contains("Walkway") ||
                    n.Contains("Sidewalk") || n.Contains("Gateway") || n.Contains("Portal") ||
                    n.Contains("Tunnel") || n.Contains("Ceiling") || n.Contains("Overpass") ||
                    n.Contains("Bridge") || n.Contains("Wire") || n.Contains("Floor") || n.Contains("Terrain"))
                    continue;
                // The guard rail is placed from the measured road width and belongs on the
                // shoulder line. World-axis bounds on a bend would push each section by a
                // different amount and leave the rail jagged.
                if (n == "Forest Guard Rail" || n == "Forest Cliff" || n == "Route Lake") continue;
                // B500 posts and signs stand just behind the rail on purpose.
                if (n.StartsWith("B500 ") || n.StartsWith("Canal ")) continue;

                var distance = Mathf.Clamp(bounds.center.z, segStart - 20f, segEnd + 20f);
                var centre = RoadPath.Center(distance);
                var right = RoadPath.Right(distance);
                var corridor = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + 1.5f;

                var minP = float.PositiveInfinity;
                var maxP = float.NegativeInfinity;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    var proj = Vector3.Dot(corner - centre, right);
                    minP = Mathf.Min(minP, proj);
                    maxP = Mathf.Max(maxP, proj);
                }

                // Straddling the road entirely, or wholly clear? Nothing to do for clear.
                if (minP >= corridor || maxP <= -corridor) continue;

                var side = Vector3.Dot(bounds.center - centre, right) >= 0f ? 1f : -1f;
                var push = side > 0f ? corridor - minP : -(maxP + corridor);
                if (Mathf.Abs(push) > 60f)
                {
                    Destroy(child.gameObject);
                    corridorRemoved++;
                    continue;
                }
                child.position += right * push;
                corridorPushed++;
            }
        }

        /// Scene-wide airborne check. Chunk-local probing found nothing because traffic,
        /// effects and the player rig are parented outside the chunk roots.
        private System.Collections.IEnumerator SkyAudit()
        {
            yield return new WaitForSeconds(2.5f);
            foreach (var rend in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var b = rend.bounds;
                var roadY = RoadPath.Center(b.center.z).y;
                if (b.min.y - roadY < 15f) continue;
                var path = rend.name;
                for (var t = rend.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Debug.Log($"RR_SKY '{path}' baseY={b.min.y - roadY:0} size={b.size:0.0} z={b.center.z:0}");
            }
            Debug.Log("RR_SKY audit complete");
        }

        /// Distant silhouettes must not live in chunks. Chunks reach only 900 m ahead
        /// (6 x 150 m), so a "horizon" ridge built per chunk does not exist until the
        /// player is 900 m away and then visibly pops in - which is what makes the
        /// skyline appear as you drive rather than sitting still on the horizon.
        ///
        /// This rig is built once per biome and rides with the player, so its contents
        /// stay at a fixed distance ahead and never enter or leave view. Real mountains
        private void UpdateStreaming(float playerDistance)
        {
            var centre = Mathf.FloorToInt(playerDistance / ChunkLength);

            for (var i = centre - ChunksBehind; i <= centre + ChunksAhead; i++)
                BuildChunk(i);

            stale.Clear();
            foreach (var pair in liveChunks)
                if (pair.Key < centre - ChunksBehind || pair.Key > centre + ChunksAhead)
                    stale.Add(pair.Key);
            foreach (var key in stale)
            {
                Destroy(liveChunks[key]);
                liveChunks.Remove(key);
            }
        }

        private readonly List<int> stale = new();

        private void BuildEnvironment(int biomeIndex)
        {
            switch (biomeIndex)
            {
                case 1: BuildSnowStation(); break;
                case 2: BuildSewerTunnel(); break;
                case 3: BuildTireDistrict(); break;
                case 4: BuildAlienBiomass(); break;
                case 5: BuildNeonCity(); break;
                case 6: BuildRedCanyon(); break;
				case 7: BuildBrooklynPhotorealPass(); break;
				case 8: BuildManhattanPhotorealPass(); break;
				case 9: BuildHollywoodPhotorealPass(); break;
                case CanalTownIndex: BuildCanalTown(); break;
                default: BuildForest(); break;
            }
        }

        // ------------------------------------------------------------ CANAL TOWN

        /// CANAL TOWN: an old Asian canal quarter, built from the Asian Canal Environment
        /// (Leartes Studios) exported from Unreal and linked by Road Rage > Link Asian
        /// Canal Pack (see CanalPack). The road runs along the canal. On the left, a
        /// pavement and a terrace of two-storey houses assembled from the kit's 2 m
        /// modules - stone and timber walls, windows and doors, awnings, pitched tiled
        /// roofs - with shop stalls, lanterns and laundry. On the right, a quay wall
        /// drops to the water, and a second terrace faces the road across the canal.
        private const int CanalTownIndex = 10;
        private const float CanalModule = 2f;
        private const float CanalPavement = 4f;   // kerb to house fronts, street side
        private const float CanalBank = 1.5f;     // kerb to the quay edge, canal side
        private const float CanalWidth = 14f;
        private const float CanalQuay = 3f;       // far quay, water to house fronts
        private const float CanalWaterLevel = -1.3f;

        private static CanalPack canalPack;
        private static Material canalQuayStone;
        private static Material canalWaterMaterial;
        private static bool canalWarned;
        private static bool canalPackLoaded;

        private static CanalPack Canal
        {
            get
            {
                if (!canalPackLoaded)
                {
                    canalPackLoaded = true;
                    canalPack = Resources.Load<CanalPack>("Biomes/AsianCanalPack");
                }
                return canalPack != null && canalPack.Meshes.Length > 0 ? canalPack : null;
            }
        }

        private static float CanalEdge(float distance) => RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth;
        private static float CanalStreetFacade(float d) => CanalEdge(d) + CanalPavement;
        private static float CanalQuayEdge(float d) => CanalEdge(d) + CanalBank;
        private static float CanalFarEdge(float d) => CanalQuayEdge(d) + CanalWidth;
        private static float CanalFarFacade(float d) => CanalFarEdge(d) + CanalQuay;

        private Material CanalMaterial(string name, string fallback)
        {
            var m = Canal?.FindMaterial(name);
            return m != null ? m : materials[fallback];
        }

        /// Ground either side, with the canal cut out of the right-hand side.
        private void BuildCanalGround(Material fallback)
        {
            _ = fallback;
            var paving = CanalMaterial("M_StoneFloorWet_01", "Kowloon Ground");
            var half = RoadPath.HalfWidthAt(segStart);
            BuildRibbon("Left CANAL TOWN Ground", -150f, -1.0f, -0.02f, paving, sampleStep: 6f, relative: true);
            BuildRibbon("Right CANAL TOWN Ground", 1.0f, CanalQuayEdge(segStart) / half, -0.02f, paving, sampleStep: 6f, relative: true);
            BuildRibbon("Far CANAL TOWN Ground", CanalFarEdge(segStart) / half, 150f, -0.02f, paving, sampleStep: 6f, relative: true);
        }

        private void BuildCanalPavements()
        {
            var cobble = CanalMaterial("M_CobbleStone_01B", "Sidewalk");
            var half = RoadPath.HalfWidthAt(segStart);
            BuildRibbon("Canal Curb Left", -1.03f, -1.0f, 0.12f, materials["City Asphalt Trim"], relative: true);
            BuildRibbon("Canal Curb Right", 1.0f, 1.03f, 0.12f, materials["City Asphalt Trim"], relative: true);
            BuildRibbon("Canal Pavement Left", -CanalStreetFacade(segStart) / half, -1.03f, 0.12f, cobble, relative: true);
            BuildRibbon("Canal Pavement Right", 1.03f, CanalQuayEdge(segStart) / half, 0.12f, cobble, relative: true);
        }

        private static readonly string[] CanalGroundWalls =
            { "SM_Wall4x2_01", "SM_Wall4x2_02", "SM_Wall4x2_04", "SM_Wall4x2_05", "SM_Wall4x2_Window_01", "SM_Wall4x2_Window_02" };
        private static readonly string[] CanalUpperWalls =
            { "SM_Wall4x2_Window_01", "SM_Wall4x2_Window_02", "SM_Wall4x2_Window_03", "SM_Wall4x2_03", "SM_Wall4x2_03_RED", "SM_Wall4x2_06" };
        private static readonly string[] CanalRoofs = { "SM_Roof_01_Straight", "SM_Roof_02_Straight" };
        private static readonly string[] CanalAwnings = { "SM_SmallRoof_01", "SM_SmallRoof_02", "SM_SmallRoof_03" };
        private static readonly string[] CanalColumns = { "SM_Column_01_RED", "SM_Column_02_RED", "SM_Column_03", "SM_Column_04_RED" };
        private static readonly string[] CanalLaundry =
            { "SM_ClothesHanged_01", "SM_ClothesHanged_02", "SM_ClothesHanged_03", "SM_ClothesHanged_4", "SM_ClothesHanged_6" };
        private static readonly string[] CanalStreetProps =
        {
            "SM_Barrel_01", "SM_Barrel_01_Ropes_01", "SM_Crate_01", "SM_SacksPacked_01", "SM_SacksPacked_02", "SM_Pot_01",
            "SM_Pot_02", "SM_Pot_04", "SM_Basket_01", "SM_Basket_02", "SM_Bucket_01", "SM_Tub_01", "SM_Wheelcart_01",
        };

        private enum CanalAlign { Min, Center, Max }

        /// Places one kit piece in a frame standing on the road at (distance, lateral),
        /// its +z pointing away from the road. The piece is measured, not trusted: its
        /// bounds are centred on the frame across the road axis, stood on the frame's
        /// floor, and put in front of, behind or across the frame's line in depth - so
        /// Unreal pivots, wherever they are, do not matter. highAway turns a sloped piece
        /// so its high side (a roof ridge, an awning's wall edge) is away from the road.
        private GameObject CanalPiece(string mesh, float distance, float lateral, int side, float height,
            CanalAlign depthAlign, float depthOffset = 0f, bool highAway = false, string label = "Canal Piece",
            Transform parent = null, float yaw = 0f)
        {
            var pack = Canal;
            var prefab = pack?.Find(mesh, out _);
            if (prefab == null) return null;
            // Measured in a frame at the origin, then moved into place.
            var frame = new GameObject(label).transform;
            var piece = Instantiate(prefab, frame, false);
            if (highAway && pack.HighOf(prefab).z < 0f)
                piece.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * piece.transform.localRotation;
            if (yaw != 0f)
                piece.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * piece.transform.localRotation;
            if (TryGetCombinedBounds(piece, out var b))
            {
                var z = depthAlign == CanalAlign.Min ? b.min.z : depthAlign == CanalAlign.Max ? b.max.z : b.center.z;
                piece.transform.localPosition -= new Vector3(b.center.x, b.min.y, z - depthOffset);
            }
            frame.SetParent(parent != null ? parent : chunkRoot, false);
            var outward = side * RoadPath.Right(distance);
            outward.y = 0f;
            frame.SetPositionAndRotation(RoadPath.Point(distance, lateral, height),
                Quaternion.LookRotation(outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.right * side));
            return frame.gameObject;
        }

        private void BuildCanalTown()
        {
            Random.InitState(88321 ^ chunkSeed);
            if (Canal == null)
            {
                if (canalWarned) return;
                canalWarned = true;
                Debug.LogWarning("CANAL TOWN needs the Asian Canal pack: export it from Unreal into Assets/AsianCanal " +
                                 "and run Road Rage > Link Asian Canal Pack.");
                return;
            }
            var street = new GameObject("Canal Street").transform;
            street.SetParent(chunkRoot, false);
            var water = new GameObject("Canal Water Side").transform;
            water.SetParent(chunkRoot, false);

            BuildCanalWater();
            if (CanalHasUsableRows())
            {
                // The artist's own houses, exported whole from the showcase map.
                BuildCanalRows(-1, CanalStreetFacade, 0.37f, street);
                BuildCanalRows(1, CanalFarEdge, 0f, water);
            }
            else
            {
                BuildCanalTerrace(-1, CanalStreetFacade, true, street);
                BuildCanalTerrace(1, CanalFarFacade, false, water);
            }
            BuildCanalQuay(water);
        }

        private void BuildCanalWater()
        {
            var half = RoadPath.HalfWidthAt(segStart);
            // Dark, still, reflective water. The pack's MuddyCanal textures are the canal
            // bed; its water is an Unreal shader that does not export.
            if (canalWaterMaterial == null)
            {
                canalWaterMaterial = new Material(materials["Mountain Lake"]) { name = "Canal Water" };
                canalWaterMaterial.color = new Color(0.035f, 0.06f, 0.055f);
                if (canalWaterMaterial.HasProperty("_BaseColor"))
                    canalWaterMaterial.SetColor("_BaseColor", new Color(0.035f, 0.06f, 0.055f));
            }
            var waterMaterial = canalWaterMaterial;
            EnableProbeReflections(BuildRibbon("Canal Water", CanalQuayEdge(segStart) / half, CanalFarEdge(segStart) / half,
                CanalWaterLevel, waterMaterial, sampleStep: 6f, relative: true));
            // Quay walls from the water up to the street, both banks. Double-sided: a
            // ribbon wall faces one way, and each bank is seen from the other.
            if (canalQuayStone == null)
            {
                canalQuayStone = new Material(CanalMaterial("M_StoneWall_03", "Sewer Concrete")) { name = "Canal Quay Stone" };
                canalQuayStone.SetFloat("_Cull", 0f);
            }
            var stone = canalQuayStone;
            BuildWallRibbon("Canal Quay Wall Near", CanalQuayEdge(segStart), CanalWaterLevel - 0.5f, 0.12f, stone);
            BuildWallRibbon("Canal Quay Wall Far", CanalFarEdge(segStart), CanalWaterLevel - 0.5f, 0.0f, stone);
        }

        /// How far below the water line a row's lowest point sits: the foot of its canal
        /// wall is under water, not standing on it.
        private const float CanalRowSink = 0.6f;
        private static bool canalRowReported;

        /// Rows of houses from the showcase map laid end to end along the road, their
        /// canal side facing it. Rows follow each other on a fixed grid of their own
        /// length, so a row crossing a chunk seam is built once, by the chunk holding its
        /// middle. phase shifts the street side against the canal side so the two banks
        /// do not mirror each other.
        private void BuildCanalRows(int side, System.Func<float, float> facadeAt, float phase, Transform parent)
        {
            var pack = Canal;
            for (var r = 0; r < pack.Rows.Length; r++)
            {
                if (pack.Rows[r] == null) continue;
                // Measured once per row prefab, turned to face the road.
                var length = CanalRowLength(r);
                if (length < 5f) continue;
                var start = Mathf.Floor((segStart - phase * length) / length) * length + phase * length;
                for (var d0 = start; d0 < segEnd; d0 += length * pack.Rows.Length)
                {
                    var mid = d0 + length * (r + 0.5f);
                    if (mid < segStart || mid >= segEnd) continue;
                    PlaceCanalRow(r, mid, side, facadeAt(mid), parent);
                }
            }
        }

        private readonly Dictionary<int, float> canalRowLengths = new();

        /// A row is one bank of houses: a few tens of metres deep. Deeper than this, the
        /// export is the whole showcase level (both banks, the ground, the bamboo), which
        /// cannot be repeated along the road: it overlaps itself and costs thousands of
        /// draw calls per chunk.
        private const float CanalRowMaxDepth = 45f;
        /// Parts smaller than this (cups, lanterns, bamboo stems) are left out of rows:
        /// at driving speed they are invisible and they are most of the draw calls.
        private const float CanalRowMinPart = 1.5f;
        /// One bank of houses is a few hundred parts at most; the showcase level has 561
        /// objects before its foliage.
        private const int CanalRowMaxParts = 400;
        private static readonly string[] CanalRowProps =
            { "bamboo", "plant", "grass", "ivy", "cloth", "rope", "cable", "wire", "cup", "bottle", "debris", "trash",
              "leaf", "leaves", "fog", "actor", "decal", "flower", "pot" };
        private static bool canalLevelWarned;

        private bool CanalRowUsable(int r)
        {
            var pack = Canal;
            return pack.Rows[r] != null && CanalRowLength(r) >= 5f;
        }

        private bool CanalHasUsableRows()
        {
            for (var r = 0; r < Canal.Rows.Length; r++)
                if (CanalRowUsable(r)) return true;
            return false;
        }

        /// Bounds of the parts still switched on (the sky and backdrop parts are off).
        private static bool ActiveBounds(GameObject item, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var r in item.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return found;
        }

        private float CanalRowLength(int r)
        {
            if (canalRowLengths.TryGetValue(r, out var cached)) return cached;
            var probe = PlaceCanalRow(r, 0f, 1, 0f, null, measureOnly: true);
            var b = default(Bounds);
            var length = probe != null && ActiveBounds(probe, out b) ? b.size.x : 0f;
            // Every object of the level counts, whether or not the row would draw it.
            var parts = Canal.Rows[r].GetComponentsInChildren<Renderer>(true).Length;
            if (length > 0f && (b.size.z > CanalRowMaxDepth || b.size.x > 400f || parts > CanalRowMaxParts))
            {
                if (!canalLevelWarned)
                {
                    canalLevelWarned = true;
                    Debug.LogWarning($"RR_CANAL '{Canal.Rows[r].name}' is {b.size.x:0} x {b.size.z:0} m with {parts} parts: that is the whole " +
                                     "level, not one bank of houses, so CANAL TOWN does not repeat it. In Unreal select only " +
                                     "the houses along ONE side of the canal (about 60-120 m long, under 40 m deep), File > " +
                                     "Export Selected into Assets/AsianCanal/Assemblies, one file per row, and run Road Rage > " +
                                     "Link Asian Canal Pack.");
                }
                length = 0f;
            }
            if (probe != null) DestroyImmediate(probe);
            canalRowLengths[r] = length;
            return length;
        }

        private GameObject PlaceCanalRow(int r, float distance, int side, float facade, Transform parent, bool measureOnly = false)
        {
            var pack = Canal;
            var frame = new GameObject("Canal Row").transform;
            var row = Instantiate(pack.Rows[r], frame, false);
            foreach (var renderer in row.GetComponentsInChildren<Renderer>(true))
            {
                var size = renderer.bounds.size.magnitude;
                var n = renderer.name.ToLowerInvariant();
                if (System.Array.IndexOf(pack.RowSkip, renderer.name) >= 0 || size > 600f || size < CanalRowMinPart ||
                    System.Array.Exists(CanalRowProps, n.Contains))
                {
                    renderer.enabled = false;
                    continue;
                }
                // Only the houses themselves cast shadows; trim, signs and awnings do not.
                if (size < 6f) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var l in row.GetComponentsInChildren<Light>(true)) l.gameObject.SetActive(false);
            foreach (var c in row.GetComponentsInChildren<Collider>(true)) Destroy(c);
            // Canal side towards the road (frame -z), long side along the road (frame x).
            var front = r < pack.RowFronts.Length ? pack.RowFronts[r] : Vector3.back;
            row.transform.localRotation = Quaternion.FromToRotation(front, Vector3.back) * row.transform.localRotation;
            if (!ActiveBounds(row, out var b))
            {
                DestroyImmediate(frame.gameObject);
                return null;
            }
            row.transform.localPosition -= new Vector3(b.center.x, b.min.y - (CanalWaterLevel - CanalRowSink), b.min.z);
            if (measureOnly) return frame.gameObject;

            if (!canalRowReported)
            {
                canalRowReported = true;
                Debug.Log($"RR_CANAL row '{pack.Rows[r].name}' {b.size.x:0}x{b.size.y:0}x{b.size.z:0} m, front {front}");
            }
            frame.SetParent(parent != null ? parent : chunkRoot, false);
            var outward = side * RoadPath.Right(distance);
            outward.y = 0f;
            frame.SetPositionAndRotation(RoadPath.Point(distance, side * facade, 0f),
                Quaternion.LookRotation(outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.right * side));
            // The row never moves: merge its parts that share a material into a few
            // batches, as the rest of the chunk does.
            var parts = new List<GameObject>();
            foreach (var renderer in row.GetComponentsInChildren<MeshRenderer>(false))
                if (renderer.enabled && renderer.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null &&
                    mf.sharedMesh.isReadable)
                    parts.Add(renderer.gameObject);
            if (parts.Count > 1) StaticBatchingUtility.Combine(parts.ToArray(), frame.gameObject);
            return frame.gameObject;
        }

        /// A terrace of houses, 3 to 6 modules each, facing the road.
        private void BuildCanalTerrace(int side, System.Func<float, float> facadeAt, bool streetSide, Transform parent)
        {
            var d = Mathf.Ceil(segStart / CanalModule) * CanalModule;
            var plaster = CanalMaterial("M_PlasterOld_01", "Kowloon Ground");
            while (d < segEnd - CanalModule * 0.5f)
            {
                var modules = Mathf.Min(Random.Range(3, 7), Mathf.FloorToInt((segEnd - d) / CanalModule));
                if (modules <= 0) break;
                var roof = CanalRoofs[Random.Range(0, CanalRoofs.Length)];
                var upperStyle = CanalUpperWalls[Random.Range(0, CanalUpperWalls.Length)];
                var shops = streetSide && Random.value < 0.6f;
                var door = Random.Range(0, modules);
                var houseStart = d;
                for (var m = 0; m < modules; m++, d += CanalModule)
                {
                    var c = d + CanalModule * 0.5f;
                    var f = facadeAt(c);
                    var ground = m == door ? null : CanalGroundWalls[Random.Range(0, CanalGroundWalls.Length)];
                    if (ground != null)
                        CanalPiece(ground, c, side * f, side, 0f, CanalAlign.Min, 0f, false, "Canal Wall", parent);
                    else
                    {
                        CanalPiece("SM_Door1_01", c, side * f, side, 0f, CanalAlign.Min, 0f, false, "Canal Door", parent);
                        CanalPiece("SM_WoodPanel1x2_01_RED", c, side * f, side, 3f, CanalAlign.Min, 0f, false, "Canal Wall", parent);
                    }
                    CanalPiece(Random.value < 0.75f ? upperStyle : CanalUpperWalls[Random.Range(0, CanalUpperWalls.Length)],
                        c, side * f, side, 4f, CanalAlign.Min, 0f, false, "Canal Wall", parent);
                    CanalPiece("SM_Beamx2_01", c, side * f, side, 3.9f, CanalAlign.Max, 0.05f, false, "Canal Beam", parent);
                    // Pitched roof: the front slope overhangs the facade, the back slope
                    // meets it at the ridge.
                    var roofMesh = Canal.Find(roof, out var roofSize);
                    CanalPiece(roof, c, side * f, side, 8f, CanalAlign.Min, -0.45f, true, "Canal Roof", parent);
                    CanalPiece(roof, c, side * f, side, 8f, CanalAlign.Min, -0.45f + roofSize.z, false, "Canal Roof", parent,
                        Canal.HighOf(roofMesh).z < 0f ? 0f : 180f);
                    if (!streetSide) continue;

                    if (shops && m != door && Random.value < 0.8f)
                    {
                        CanalPiece(CanalAwnings[Random.Range(0, CanalAwnings.Length)], c, side * f, side, 3.2f,
                            CanalAlign.Max, 0f, true, "Canal Awning", parent);
                        if (Random.value < 0.35f)
                            CanalPiece("SM_Lantern_02", c, side * (f - 0.7f), side, 2.3f, CanalAlign.Center, 0f, false, "Canal Lantern", parent);
                    }
                    if (Random.value < 0.22f)
                        CanalPiece(CanalLaundry[Random.Range(0, CanalLaundry.Length)], c, side * (f - 0.35f), side, 5.6f,
                            CanalAlign.Center, 0f, false, "Canal Laundry", parent);
                    if (Random.value < 0.08f)
                        CanalPiece("SM_Banner_01", c, side * (f - 0.25f), side, 4.4f, CanalAlign.Center, 0f, false, "Canal Banner", parent);
                    if (Random.value < 0.3f)
                        CanalPiece("SM_WallBase1x2_02", c, side * f, side, 0f, CanalAlign.Max, 0f, false, "Canal Plinth", parent);
                    if (Random.value < 0.35f)
                        CanalPiece(CanalStreetProps[Random.Range(0, CanalStreetProps.Length)], c + Random.Range(-0.5f, 0.5f),
                            side * (f - Random.Range(0.8f, 1.6f)), side, 0.12f, CanalAlign.Center, 0f, false, "Canal Prop", parent,
                            Random.Range(0f, 360f));
                }
                // Columns at the house ends, the body behind the facade, and now and
                // then a market stall on the pavement.
                if (streetSide)
                {
                    var column = CanalColumns[Random.Range(0, CanalColumns.Length)];
                    CanalPiece(column, houseStart, side * (facadeAt(houseStart) - 0.25f), side, 0f, CanalAlign.Center, 0f, false, "Canal Column", parent);
                    if (Random.value < 0.18f)
                    {
                        var at = houseStart + modules * CanalModule * 0.5f;
                        CanalPiece(Random.value < 0.5f ? "SM_Stand_01" : "SM_Stand_02", at, side * facadeAt(at), side, 0.12f,
                            CanalAlign.Max, -0.2f, false, "Canal Stall", parent);
                    }
                }
                var mid = houseStart + modules * CanalModule * 0.5f;
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Canal House Body";
                Destroy(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = plaster;
                body.transform.SetParent(parent, false);
                var outward = side * RoadPath.Right(mid);
                outward.y = 0f;
                var rot = Quaternion.LookRotation(outward.normalized);
                body.transform.SetPositionAndRotation(RoadPath.Point(mid, side * (facadeAt(mid) + 2.1f), 4f), rot);
                body.transform.localScale = new Vector3(modules * CanalModule - 0.05f, 8f, 3.6f);

                // An alley between houses now and then.
                if (Random.value < 0.15f) d += CanalModule;
            }
        }

        /// The canal's edge on the street side: a low stone parapet, stone lanterns,
        /// steps down to the water, and a lion guarding the way now and then.
        private void BuildCanalQuay(Transform parent)
        {
            for (var d = Mathf.Ceil(segStart / CanalModule) * CanalModule; d < segEnd; d += CanalModule)
            {
                var edge = CanalQuayEdge(d);
                var step = Mathf.RoundToInt(d / CanalModule);
                CanalPiece("SM_StoneFence_01_Long", d, edge - 0.15f, 1, 0.12f, CanalAlign.Center, 0f, false, "Canal Parapet", parent);
                if (step % 6 == 0)
                    CanalPiece("SM_Toro_01", d, edge - 1.0f, 1, 0.12f, CanalAlign.Center, 0f, false, "Canal Toro", parent);
                if (step % 97 == 13)
                    CanalPiece("SM_LionStatue_01", d, CanalFarEdge(d) + CanalQuay * 0.5f, 1, 0f, CanalAlign.Center, 0f, false, "Canal Lion", parent, 180f);
                // Lanterns along the far quay.
                if (step % 8 == 3)
                    CanalPiece("SM_Toro_01", d, CanalFarEdge(d) + 1.2f, 1, 0f, CanalAlign.Center, 0f, false, "Canal Toro", parent);
            }
        }

        private void BuildSnowStation()
        {
            Random.InitState(71203 ^ chunkSeed);

            // Continuous snow banks hugging the shoulder, then drifts receding into a
            // ridge line - without these the biome is a flat white plane with props on it.
            ScatterBand(7f, 13.5f, 21f, (d, l, s) =>
                PlaceBiomeModelOnRoad("IceStation", Random.value > 0.5f ? "SM_snow_01" : "SM_ice_02",
                    materials["Snow Ground"], d, l, -0.15f, new Vector3(0f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(0.55f, 0.95f), "Verge Snow Bank"));
            ScatterBand(10f, 22f, 45f, (d, l, s) =>
                PlaceBiomeModelOnRoad("IceStation", Random.value > 0.6f ? "SM_ice_02" : "SM_snow_01",
                    materials["Snow Ground"], d, l, -0.2f, new Vector3(0f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(0.9f, 1.6f), "Snow Drift"));
            ScatterBand(14f, 48f, 95f, (d, l, s) =>
                PlaceBiomeModelOnRoad("IceStation", "SM_snow_01", materials["Snow Ground"],
                    d, l, -0.4f, new Vector3(0f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(1.8f, 3.4f), "Snow Ridge"));
            // Mountain silhouette so the horizon closes off.
            ScatterBand(30f, 130f, 210f, (d, l, s) =>
                PrimitiveOnRoad(PrimitiveType.Sphere, "Snow Mountain", d, l, 2f,
                    new Vector3(Random.Range(90f, 160f), Random.Range(30f, 62f), Random.Range(80f, 140f)),
                    materials["Snow Ground"], Vector3.zero, false));

            for (var z = SegBegin(-8f, 22f); z < segEnd; z += 22f)
            {
                for (var side = -1; side <= 1; side += 2)
                {
                    var distance = z + Random.Range(-5f, 5f);
                    PlaceBiomeModelOnRoad("IceStation", ((int)(z / 22f) + side) % 2 == 0 ? "SM_snow_01" : "SM_ice_02", materials["Snow Ground"],
                        distance, side * Random.Range(15f, 23f), -0.12f,
                        new Vector3(0f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(0.72f, 1.12f), "Snow Bank");
                    PlaceBiomeModelOnRoad("IceStation", "SM_snow_01", materials["Snow Ground"],
                        distance + Random.Range(-9f, 9f), side * Random.Range(38f, 63f), -0.2f,
                        new Vector3(0f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(1.15f, 1.8f), "Distant Snow Ridge");
                }

                if (((int)z / 22) % 4 != 1) continue;
                var sideSign = ((int)z / 22) % 8 < 4 ? 1f : -1f;
                PlaceBiomeModelOnRoad("IceStation", "SM_base_building_01", materials["Ice Station"],
                    z + 8f, sideSign * 28f, 0f, new Vector3(0f, sideSign > 0f ? -90f : 90f, 0f), Vector3.one * 0.5f, "Ice Station Base");
                PlaceBiomeModelOnRoad("IceStation", "SM_building_top_04", materials["Ice Station"],
                    z + 8f, sideSign * 28f, 9f, new Vector3(0f, sideSign > 0f ? -90f : 90f, 0f), Vector3.one * 0.5f, "Ice Station Tower");
                PlaceBiomeModelOnRoad("IceStation", "SM_antenna_alone", materials["Ice Station"],
                    z + 8f, sideSign * 28f, 16f, Vector3.zero, Vector3.one * 0.62f, "Station Antenna");
                PlaceBiomeModelOnRoad("IceStation", "SM_shuttle_closed", materials["Ice Ship"],
                    z + 22f, -sideSign * 24f, 0.1f, new Vector3(0f, sideSign > 0f ? 25f : -25f, 0f), Vector3.one * 0.72f, "Parked Shuttle");
                CreateStreetLamp(z - 3f, sideSign * 12.8f, new Color(0.52f, 0.78f, 1f));
                CreateStreetLamp(z + 17f, -sideSign * 12.8f, new Color(0.52f, 0.78f, 1f));
            }
        }

        private void BuildSewerTunnel()
        {
            BuildWallRibbon("Sewer Left Curved Wall", -17.6f, -0.05f, 10.2f, materials["Sewer Concrete"]);
            BuildWallRibbon("Sewer Right Curved Wall", 17.6f, -0.05f, 10.2f, materials["Sewer Concrete"]);
            BuildRibbon("Sewer Curved Ceiling", -17.8f, 17.8f, 10.2f, materials["Sewer Concrete"], -45f, WorldLength + 45f, 7f);

            // Wall furniture mounted safely flush against tunnel walls (zero lane intrusion)
            ScatterBand(10f, 17.0f, 17.5f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_pipe_03", materials["Sewer Pipe"],
                    d, l, Random.Range(2.5f, 6.5f), new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one * Random.Range(0.6f, 0.95f), "Wall Pipe", true));
            ScatterBand(18f, 17.1f, 17.6f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_pillar", materials["Sewer Concrete"],
                    d, l, 0f, new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one * Random.Range(0.85f, 1.15f), "Tunnel Pillar", true));
            ScatterBand(34f, 17.2f, 17.7f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_arch", materials["Sewer Concrete"],
                    d, l, 0f, new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one, "Side Arch", true));

            for (var z = SegBegin(-12f, 18f); z < segEnd; z += 18f)
            {
                if (((int)(z / 18f)) % 2 == 0)
                {
                    PlaceBiomeModelOnRoad("Sewers", "SM_pipe_03", materials["Sewer Pipe"],
                        z + 5f, -17.2f, 3.2f, new Vector3(0f, 90f, 0f), Vector3.one * 0.78f, "Wall-Mounted Sewer Pipe", true);
                    CreateLocalLight(RoadPath.Point(z, ((int)(z / 18f)) % 4 == 0 ? -10.8f : 10.8f, 6.3f),
                        new Color(0.2f, 1f, 0.56f), 13f, 14f);
                }
            }
        }

        private void BuildTireDistrict()
        {
            Random.InitState(1908 ^ chunkSeed);
            BuildRibbon("Left City Sidewalk", -16.2f, -RoadPath.HalfWidth - RoadPath.ShoulderWidth, 0.065f, materials["Sidewalk"]);
            BuildRibbon("Right City Sidewalk", RoadPath.HalfWidth + RoadPath.ShoulderWidth, 16.2f, 0.065f, materials["Sidewalk"]);

            // Industrial yard clutter along the sidewalk, and a second building row behind
            // the frontage so the street has depth rather than a single facade line.
            var yardProps = new[] { "SM_TireShelf", "SM_AirCompressor", "SM_Workbench", "SM_TireMachine" };
            ScatterBand(8.5f, 12f, 15f, (d, l, s) =>
            {
                var prop = PlaceBiomeModelOnRoad("TireRepair", yardProps[Random.Range(0, yardProps.Length)],
                    materials["Garage Equipment"], d, l, 0.05f,
                    new Vector3(-90f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Yard Clutter");
                if (prop != null) NormalizeModelHeight(prop, Random.Range(1.4f, 2.6f), 0.05f);
                return prop;
            });
            ScatterBand(13f, 15f, 19f, (d, l, s) =>
            {
                var junk = PlaceBiomeModelOnRoad("CyberpunkCity",
                    Random.value > 0.5f ? "Crates/SM_crate_01" : "Trashbag/SM_trashbag_group_01",
                    Random.value > 0.5f ? materials["Cyber Crate"] : materials["Cyber Trash"],
                    d, l, 0.05f, new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Street Junk");
                if (junk != null) NormalizeModelHeight(junk, Random.Range(0.8f, 1.6f), 0.05f);
                return junk;
            });
            // Industrial factories, workshops, and mid-rise office warehouses
            var industrialBuildings = new[]
            {
                "factory_building_big",
                "factory_building_small",
                "office_building_1_with_base",
                "office_building_2_with_base",
                "office_building_3_with_base",
                "office_building_4_with_base",
                "mid_house_1",
                "mid_house_2",
                "mid_house_3",
                "mid_house_4",
                "mid_house_5"
            };

            // Three building rows: frontage factories, mid block workshops, and skyline factories
            ScatterBand(16f, 22f, 32f, (d, l, s) =>
            {
                var bName = industrialBuildings[Random.Range(0, industrialBuildings.Length)];
                var front = PlaceBiomeModelOnRoad("DemoCity", bName,
                    materials["City Concrete"], d, l, 0f,
                    new Vector3(0f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Industrial Frontage");
                if (front != null) NormalizeModelHeight(front, Random.Range(14f, 28f));
                return front;
            });
            ScatterBand(20f, 36f, 65f, (d, l, s) =>
            {
                var bName = industrialBuildings[Random.Range(0, industrialBuildings.Length)];
                var back = PlaceBiomeModelOnRoad("DemoCity", bName,
                    materials["City Concrete"], d, l, 0f,
                    new Vector3(0f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Industrial Back Block");
                if (back != null) NormalizeModelHeight(back, Random.Range(20f, 42f));
                return back;
            });
            ScatterBand(26f, 68f, 130f, (d, l, s) =>
            {
                var bName = industrialBuildings[Random.Range(0, industrialBuildings.Length)];
                var far = PlaceBiomeModelOnRoad("DemoCity", bName,
                    materials["City Concrete"], d, l, 0f,
                    new Vector3(0f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Industrial Skyline");
                if (far != null) NormalizeModelHeight(far, Random.Range(28f, 58f));
                return far;
            });
            ScatterBand(22f, 15.5f, 17.5f, (d, l, s) =>
                CreateStreetLampAt(d, l, new Color(1f, 0.86f, 0.62f)));
            ScatterBand(26f, 15f, 18f, (d, l, s) =>
            {
                var fence = PlaceBiomeModelOnRoad("Synthwave", "Fence/SM_fence", materials["Garage Door"],
                    d, l, 0f, new Vector3(-90f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Yard Fence");
                if (fence != null) NormalizeModelSpan(fence, 9f, 0f);
                return fence;
            });

            // Aligned block pattern for consistent street frontage
            for (var z = SegBegin(4f, 32f); z < segEnd; z += 32f)
            {
                var block = Mathf.FloorToInt(z / 32f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var facing = side > 0f ? -90f : 90f;
                    var nearName = industrialBuildings[BlockHash(block, side) % industrialBuildings.Length];
                    var nearDistance = z + (side > 0 ? 5f : -4f) + Random.Range(-3f, 3f);
                    var nearBuilding = PlaceBiomeModelOnRoad("DemoCity", nearName, materials["City Concrete"],
                        nearDistance, side * Random.Range(24f, 32f), 0f,
                        new Vector3(0f, facing, 0f), Vector3.one, "Factory Frontage");
                    if (nearBuilding != null)
                    {
                        NormalizeModelHeight(nearBuilding, Random.Range(14f, 30f));
                        EnsureOutsideRoad(nearBuilding, nearDistance, side);
                    }

                    if (block % 2 == 0)
                    {
                        var farName = industrialBuildings[BlockHash(block, side * 3) % industrialBuildings.Length];
                        var farDistance = z + Random.Range(-12f, 12f);
                        var skyline = PlaceBiomeModelOnRoad("DemoCity", farName, materials["City Concrete"],
                            farDistance, side * Random.Range(46f, 68f), 0f,
                            new Vector3(0f, facing, 0f), Vector3.one, "Factory Skyline");
                        if (skyline != null)
                        {
                            NormalizeModelHeight(skyline, Random.Range(26f, 52f));
                            EnsureOutsideRoad(skyline, farDistance, side);
                        }
                    }

                    CreateStreetLamp(z + (side > 0 ? 9f : -7f), side * 12.6f, new Color(1f, 0.88f, 0.72f));
                }
                if (block % 4 == 1)
                {
                    var side = block % 8 < 4 ? 1f : -1f;
                    var facing = side > 0f ? -90f : 90f;
                    var billboard = PlaceBiomeModelOnRoad("Synthwave", "Advertisements/SM_advertisement_03", materials["City Sign"],
                        z + 12f, side * 18f, 4f, new Vector3(-90f, facing, 0f), Vector3.one, "Neon Roadside Billboard");
                    if (billboard != null)
                    {
                        NormalizeModelHeight(billboard, 7.5f, 2.5f);
                        EnsureOutsideRoad(billboard, z + 12f, side);
                    }
                }

                if (block % 3 != 0) continue;
                var garageSide = block % 2 == 0 ? 1f : -1f;
                var garageFacing = garageSide > 0f ? -90f : 90f;
                PrimitiveOnRoad(PrimitiveType.Cube, "Industrial Repair Garage", z + 5f, garageSide * 24f, 3.6f,
                    new Vector3(16f, 7.2f, 18f), materials["Garage Wall"], Vector3.zero);
                PrimitiveOnRoad(PrimitiveType.Cube, "Garage Roof", z + 5f, garageSide * 24f, 7.35f,
                    new Vector3(19f, 0.45f, 20f), materials["Garage Door"], Vector3.zero);
                PlaceBiomeModelOnRoad("TireRepair", "SM_WallDoor_003", materials["Garage Wall"],
                    z, garageSide * 15.2f, 0f, new Vector3(-90f, garageFacing, 0f), Vector3.one * 1.1f, "Repair Shop Front");
                PlaceBiomeModelOnRoad("TireRepair", "SM_MetalDoor", materials["Garage Door"],
                    z + 1f, garageSide * 15f, 0.2f, new Vector3(-90f, garageFacing, 0f), Vector3.one * 1.1f, "Metal Garage Door");
                PlaceBiomeModelOnRoad("TireRepair", "SM_TireShelf", materials["Garage Shelf"],
                    z + 8f, garageSide * 15.5f, 0.15f, new Vector3(-90f, garageFacing, 0f), Vector3.one, "Tire Display");
                PlaceBiomeModelOnRoad("TireRepair", "SM_TireMachine", materials["Garage Equipment"],
                    z + 18f, -garageSide * 14.8f, 0.1f, new Vector3(-90f, -garageFacing, 0f), Vector3.one, "Tire Machine");
                CreateLocalLight(RoadPath.Point(z + 1f, garageSide * 12.2f, 5f), new Color(1f, 0.48f, 0.18f), 10f, 17f);
            }
        }

        private void BuildAlienBiomass()
        {
            Random.InitState(73119 ^ chunkSeed);
            var organisms = new[]
            {
                "SM_alien_organism_01", "SM_alien_organism_03", "SM_alien_organism_06",
                "SM_alien_organism_09", "SM_alien_organism_12"
            };

            // Infestation should crowd the roadside and thin out with distance.
            ScatterBand(6f, 13.5f, 22f, (d, l, s) =>
                PlaceBiomeModelOnRoad("AlienBiomass", organisms[Random.Range(0, organisms.Length)],
                    Random.value > 0.5f ? materials["Alien Organic A"] : materials["Alien Organic B"],
                    d, l, 0.1f, new Vector3(-90f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(0.9f, 1.5f), "Alien Growth"));
            ScatterBand(9f, 22f, 44f, (d, l, s) =>
                PlaceBiomeModelOnRoad("AlienBiomass", organisms[Random.Range(0, organisms.Length)],
                    Random.value > 0.5f ? materials["Alien Organic A"] : materials["Alien Organic B"],
                    d, l, 0.05f, new Vector3(-90f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(1.2f, 2.1f), "Alien Growth Cluster"));
            ScatterBand(13f, 46f, 90f, (d, l, s) =>
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_rock_01", materials["Alien Rock"],
                    d, l, 0f, new Vector3(-90f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(1.1f, 2.4f), "Alien Rock Formation"));
            ScatterBand(8f, 13f, 30f, (d, l, s) =>
                PrimitiveOnRoad(PrimitiveType.Sphere, "Biomass Carpet", d, l, 0.14f,
                    new Vector3(Random.Range(2.5f, 5.5f), 0.3f, Random.Range(3f, 8f)),
                    Random.value > 0.5f ? materials["Alien Organic A"] : materials["Alien Organic B"],
                    new Vector3(0f, Random.Range(0f, 360f), 0f)));

            for (var z = SegBegin(-5f, 18f); z < segEnd; z += 18f)
            {
                for (var side = -1; side <= 1; side += 2)
                {
                    var index = Mathf.Abs(((int)z / 18) + (side > 0 ? 2 : 0)) % organisms.Length;
                    var organicMaterial = index % 2 == 0 ? materials["Alien Organic A"] : materials["Alien Organic B"];
                    PlaceBiomeModelOnRoad("AlienBiomass", organisms[index], organicMaterial,
                        z + Random.Range(-4f, 4f), side * Random.Range(14f, 22f), 0.1f,
                        new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(1.05f, 1.5f), "Alien Growth");
                    PlaceBiomeModelOnRoad("AlienBiomass", "SM_alien_organism_12", materials["Alien Organic A"],
                        z + Random.Range(-8f, 8f), side * Random.Range(25f, 36f), 0.05f,
                        new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(1.15f, 1.75f), "Alien Growth Cluster");
                    PrimitiveOnRoad(PrimitiveType.Sphere, "Biomass Carpet", z + Random.Range(-5f, 5f),
                        side * Random.Range(14f, 19f), 0.14f, new Vector3(Random.Range(2f, 4.5f), 0.28f, Random.Range(3f, 7f)),
                        organicMaterial, new Vector3(0f, Random.Range(0f, 360f), 0f));
                }

                if (((int)z / 18) % 3 != 1) continue;
                var facilitySide = ((int)z / 18) % 6 < 3 ? 1f : -1f;
                var facing = facilitySide > 0f ? -90f : 90f;
                PrimitiveOnRoad(PrimitiveType.Cube, "Biomass Research Wing", z + 8f, facilitySide * 24f, 3.3f,
                    new Vector3(15f, 6.6f, 17f), materials["Alien Facility"], Vector3.zero);
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_module_wall_01", materials["Alien Facility"],
                    z + 2f, facilitySide * 15f, 0f, new Vector3(-90f, facing, 0f), Vector3.one * 1.15f, "Infected Facility Wall");
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_module_wall_04", materials["Alien Facility"],
                    z + 9f, facilitySide * 15f, 0f, new Vector3(-90f, facing, 0f), Vector3.one * 1.15f, "Infected Facility Wall");
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_door_closed", materials["Alien Facility"],
                    z + 5f, facilitySide * 14.8f, 0f, new Vector3(-90f, facing, 0f), Vector3.one * 1.08f, "Containment Door");
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_base_module_01", materials["Alien Facility"],
                    z + 20f, -facilitySide * 23f, 0f, new Vector3(-90f, -facing, 0f), Vector3.one, "Research Module");
                PlaceBiomeModelOnRoad("AlienBiomass", "SM_rock_01", materials["Alien Rock"],
                    z - 7f, -facilitySide * 27f, 0f, new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one * 0.72f, "Alien Rock");

                CreateLocalLight(RoadPath.Point(z + 4f, facilitySide * 12.2f, 4.5f), new Color(0.25f, 1f, 0.48f), 14f, 19f);
                CreateLocalLight(RoadPath.Point(z + 18f, -facilitySide * 12.2f, 3.2f), new Color(0.82f, 0.18f, 1f), 11f, 17f);
            }
        }

        private void BuildNeonCity()
        {
            Random.InitState(60814 ^ chunkSeed);
            // Sidewalk ribbons flanking outside the 6-lane carriageway and shoulder (16.8m to 24.0m)
            BuildRibbon("Left Neon Sidewalk", -24.0f, -16.8f, 0.08f, materials["Sidewalk"]);
            BuildRibbon("Right Neon Sidewalk", 16.8f, 24.0f, 0.08f, materials["Sidewalk"]);
            BuildRibbon("Left Kerb Glow", -17.0f, -16.8f, 0.12f, materials["City Neon"]);
            BuildRibbon("Right Kerb Glow", 16.8f, 17.0f, 0.12f, materials["City Neon"]);

            // Distant skyline buildings far off in the horizon (65m to 130m out)
            ScatterBand(16f, 65f, 130f, (d, l, s) =>
            {
                var far = PlaceBiomeModelOnRoad("Synthwave", $"Buildings/SM_building_{Random.Range(1, 13):00}",
                    materials["City Skyline"], d, l, 0f,
                    new Vector3(-90f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Skyline Block");
                if (far != null) NormalizeModelHeight(far, Random.Range(35f, 90f));
                return far;
            });

            var towers = new[]
            {
                "Buildings/SM_building_01", "Buildings/SM_building_02", "Buildings/SM_building_03",
                "Buildings/SM_building_04", "Buildings/SM_building_05", "Buildings/SM_building_06",
                "Buildings/SM_building_07", "Buildings/SM_building_08", "Buildings/SM_building_09",
                "Buildings/SM_building_10", "Buildings/SM_building_11", "Buildings/SM_building_12"
            };
            var domes = new[]
            {
                "Buildings/SM_dome_building", "Buildings/SM_dome_building_02",
                "Buildings/SM_dome_building_03", "Buildings/SM_dome_building_04"
            };
            var advertisements = new[]
            {
                "Advertisements/SM_advertisement_01", "Advertisements/SM_advertisement_03",
                "Advertisements/SM_advertisement_05"
            };
            var palms = new[] { "Tree/SM_palm_tree_01", "Tree/SM_palm_tree_02", "Tree/SM_palm_tree_03" };
            var neonPalette = new[]
            {
                new Color(1f, 0.18f, 0.62f), new Color(0.18f, 0.86f, 1f),
                new Color(0.72f, 0.25f, 1f), new Color(1f, 0.62f, 0.12f)
            };

            for (var z = SegBegin(0f, 24f); z < segEnd; z += 24f)
            {
                var block = Mathf.FloorToInt(z / 24f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var facing = side > 0f ? -90f : 90f;
                    var isNyc = block % 2 == 0;
                    var frontageMesh = isNyc
                        ? NycVariants[BlockHash(block, side * 7) % NycVariants.Length]
                        : towers[BlockHash(block, side) % towers.Length];
                    var frontagePack = isNyc ? "Buildings" : "Synthwave";
                    var frontageDistance = z + (side > 0 ? 4f : -5f) + Random.Range(-2.5f, 2.5f);
                    var tower = PlaceBiomeModelOnRoad(frontagePack, frontageMesh, materials["City Concrete"],
                        frontageDistance, side * Random.Range(28.0f, 36.0f), 0f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Neon Tower");
                    if (tower != null)
                    {
                        NormalizeModelHeight(tower, Random.Range(28f, 58f));
                    }

                    var skylineName = block % 3 == 0
                        ? domes[BlockHash(block, side * 3) % domes.Length]
                        : towers[BlockHash(block, side * 5) % towers.Length];
                    var skylineDistance = z + Random.Range(-11f, 11f);
                    var skyline = PlaceBiomeModelOnRoad("Synthwave", skylineName, materials["City Skyline"],
                        skylineDistance, side * Random.Range(55f, 90f), 0f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Neon Skyline");
                    if (skyline != null)
                    {
                        NormalizeModelHeight(skyline, Random.Range(38f, 82f));
                    }

                    // Street Lamps sitting safely on the sidewalk
                    var lampDistance = z + (side > 0 ? 11f : -7f);
                    var lamp = PlaceBiomeModelOnRoad("Synthwave", "Street_lamp/SM_street_lamp", materials["City Asphalt Trim"],
                        lampDistance, side * 18.5f, 0f, new Vector3(-90f, facing, 0f), Vector3.one, "City Street Lamp");
                    if (lamp != null)
                    {
                        NormalizeModelHeight(lamp, 8.4f);
                        CreateLocalLight(RoadPath.Point(lampDistance, side * 18.5f, 7.6f),
                            neonPalette[BlockHash(block, side * 7) % neonPalette.Length], 10f, 20f);
                    }

                    // Boulevard Palms sitting safely on the outer sidewalk
                    if (block % 2 == 0)
                    {
                        var palmDistance = z + (side > 0 ? 17f : -15f);
                        var palm = PlaceBiomeModelOnRoad("Synthwave", palms[BlockHash(block, side * 9) % palms.Length],
                            materials["City Palm"], palmDistance, side * 20.2f, 0f,
                            new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Boulevard Palm");
                        if (palm != null) NormalizeModelHeight(palm, Random.Range(9f, 13f));
                    }
                }

                if (block % 3 == 1)
                {
                    var side = block % 6 < 3 ? 1f : -1f;
                    var facing = side > 0f ? -90f : 90f;
                    var signDistance = z + 8f;
                    var sign = PlaceBiomeModelOnRoad("Synthwave", "Road_sign/SM_road_sign", materials["City Sign"],
                        signDistance, side * 18.5f, 0f, new Vector3(-90f, facing, 0f), Vector3.one, "Neon Road Sign");
                    if (sign != null) NormalizeModelHeight(sign, 5.6f);

                    var billboardDistance = z + 18f;
                    var billboard = PlaceBiomeModelOnRoad("Synthwave", advertisements[BlockHash(block, 13) % advertisements.Length],
                        materials["City Billboard"], billboardDistance, -side * 24.5f, 4f,
                        new Vector3(-90f, -facing, 0f), Vector3.one, "Neon Billboard");
                    if (billboard != null)
                    {
                        NormalizeModelHeight(billboard, 9f, 3.2f);
                        CreateLocalLight(RoadPath.Point(billboardDistance, -side * 24.5f, 7f),
                            neonPalette[BlockHash(block, 15) % neonPalette.Length], 12f, 22f);
                    }
                }

                // Urban Bus Stops & Commercial Shopfronts (sitting safely on sidewalk)
                if (block % 4 == 2)
                {
                    var side = block % 8 < 4 ? 1f : -1f;
                    var stopDistance = z + 6f;
                    // Was "Buildings/DemoCity/bus_stop", which does not exist - the DemoCity
                    // pack ships no bus stop - so every fourth block logged a missing-model
                    // warning and left its bench standing beside nothing. Neon City's own
                    // pack has a transit shelter that was sitting unreferenced.
                    //
                    // The -90 on X is not decoration: every CyberpunkCity mesh in this file
                    // is placed that way because the pack is authored Z-up, and the old call
                    // passed 0 because DemoCity is not.
                    var stop = PlaceBiomeModelOnRoad("CyberpunkCity", "Tramstop/SM_Tram_stop",
                        materials["Cyber Props"], stopDistance, side * 19.8f, 0.14f,
                        new Vector3(-90f, side > 0f ? -90f : 90f, 0f), Vector3.one, "Neon Tram Stop");
                    if (stop != null) NormalizeModelHeight(stop, 3.2f, 0.14f);

                    var benchDistance = z + 12f;
                    var bench = PlaceBiomeModelOnRoad("Buildings", "DemoCity/bench",
                        materials["City Props"], benchDistance, side * 19.5f, 0.14f,
                        new Vector3(0f, side > 0f ? -90f : 90f, 0f), Vector3.one, "City Bench");
                    if (bench != null) NormalizeModelHeight(bench, 1.0f, 0.14f);
                }

                if (block % 7 == 3) BuildCityOverpass(z + 12f, block);
                DressNeonSidewalk(z + 3f, block % 2 == 0 ? 1f : -1f, block);
            }
        }

			private void ApplyCityPhotorealMood(bool brooklyn)
			{
				RenderSettings.ambientMode = AmbientMode.Trilight;
				RenderSettings.fog = true;

				// Ambient colours, ambient intensity, fog and the key light used to be
				// set here as well. BlendZoneLighting rewrites all of those every frame
				// from the biome mood, so these assignments only survived until the next
				// frame and the two definitions had been silently disagreeing ever since
				// the Manhattan mood was restored on top of the photoreal pass. The mood
				// is the single source of truth now; what is left here is the part
				// nothing else owns.
				TuneReflectionProbe(DefaultProbeIntensity, new Vector3(60f, 20f, 60f), 3f);

				var quality = QualitySettings.GetQualityLevel();
				if (quality < 3)
					QualitySettings.SetQualityLevel(3, true);
			}

			private void BuildBrooklynPhotorealPass()
			{
				BuildKowloonNights();
				ApplyCityPhotorealMood(brooklyn: true);
				ApplyCitySurfaceMaterialOverrides(brooklyn: true);
				ApplyCityPhotorealSignature(brooklyn: true);
				ApplyCityDepthPass(brooklyn: true);
				ApplyCityRoadToneProfile(brooklyn: true);
			}

			private void BuildManhattanPhotorealPass()
			{
				BuildCyberSprawl();
				// Only in daylight. This was unconditional, and it is called per chunk
				// build - after BuildLighting has already applied the biome mood, and
				// again on every chunk the streamer adds. So it overwrote the ambient
				// colours, the fog and every directional light with hardcoded overcast
				// values, and re-overwrote them as you drove: Manhattan was daylight
				// whatever the switch said, and ManhattanNightMood never survived to be
				// seen. Night now leaves BuildLighting's result standing.
				if (ManhattanDaylight) ApplyManhattanDaylightMood();
			}

			private void ApplyManhattanDaylightMood()
			{
				// Desaturated overcast NYC — all RGB channels kept close together, no vivid blues
				RenderSettings.ambientMode = AmbientMode.Trilight;
				RenderSettings.ambientIntensity = 1.15f;
				RenderSettings.ambientSkyColor    = new Color(0.50f, 0.52f, 0.56f);
				RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
				RenderSettings.ambientGroundColor  = new Color(0.22f, 0.22f, 0.23f);
				RenderSettings.ambientLight        = new Color(0.16f, 0.16f, 0.17f);
				RenderSettings.fog = true;
				RenderSettings.fogMode    = FogMode.ExponentialSquared;
				RenderSettings.fogColor   = new Color(0.62f, 0.63f, 0.65f);
				RenderSettings.fogDensity = 0.0018f;

				var sceneLights = Object.FindObjectsByType<Light>();
				for (var i = 0; i < sceneLights.Length; i++)
				{
					var sceneLight = sceneLights[i];
					if (sceneLight.type != LightType.Directional || !sceneLight.isActiveAndEnabled) continue;
					sceneLight.color = new Color(1f, 0.98f, 0.95f);
					sceneLight.intensity = 1.30f;
					sceneLight.shadowStrength = 0.70f;
					sceneLight.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
				}

				TuneReflectionProbe(0.90f, new Vector3(72f, 28f, 72f), 4f);

				// Road — dark asphalt, almost no saturation
				if (materials.TryGetValue("Road", out var road))
				{
					if (road.HasProperty("_BaseColor"))
						road.SetColor("_BaseColor", new Color(0.32f, 0.32f, 0.34f));
					if (road.HasProperty("_Smoothness")) road.SetFloat("_Smoothness", 0.18f);
					if (road.HasProperty("_Metallic")) road.SetFloat("_Metallic", 0.02f);
					if (road.HasProperty("_NormalScale")) road.SetFloat("_NormalScale", 0.85f);
					if (road.HasProperty("_OcclusionStrength")) road.SetFloat("_OcclusionStrength", 1.05f);
				}

				// Sidewalk — NYC limestone/concrete grey
				if (materials.TryGetValue("Sidewalk", out var sidewalk))
				{
					if (sidewalk.HasProperty("_BaseColor"))
						sidewalk.SetColor("_BaseColor", new Color(0.58f, 0.58f, 0.60f));
					if (sidewalk.HasProperty("_Smoothness")) sidewalk.SetFloat("_Smoothness", 0.14f);
					if (sidewalk.HasProperty("_Metallic")) sidewalk.SetFloat("_Metallic", 0.03f);
					if (sidewalk.HasProperty("_NormalScale")) sidewalk.SetFloat("_NormalScale", 0.75f);
				}

				// Building concrete — neutral warm-grey, no blue tint
				if (materials.TryGetValue("City Concrete", out var concrete))
				{
					if (concrete.HasProperty("_BaseColor"))
						concrete.SetColor("_BaseColor", new Color(0.55f, 0.54f, 0.53f));
				}

				// Windows — tinted glass but not neon-bright; keep channel spread tight
				if (materials.TryGetValue("City Windows", out var windows))
				{
					if (windows.HasProperty("_BaseColor"))
						windows.SetColor("_BaseColor", new Color(0.45f, 0.50f, 0.56f));
					if (windows.HasProperty("_Smoothness")) windows.SetFloat("_Smoothness", 0.82f);
					if (windows.HasProperty("_Metallic")) windows.SetFloat("_Metallic", 0.65f);
					if (windows.HasProperty("_EmissionColor"))
						windows.SetColor("_EmissionColor", new Color(0.12f, 0.14f, 0.18f, 1f));
				}

				// Skyline towers — concrete grey silhouettes
				if (materials.TryGetValue("City Skyline", out var skyline))
				{
					if (skyline.HasProperty("_BaseColor"))
						skyline.SetColor("_BaseColor", new Color(0.50f, 0.50f, 0.52f));
				}

				// Street hardware — matte iron/steel
				if (materials.TryGetValue("City Neon", out var neon))
				{
					if (neon.HasProperty("_BaseColor"))
						neon.SetColor("_BaseColor", new Color(0.58f, 0.56f, 0.54f));
					if (neon.HasProperty("_Smoothness")) neon.SetFloat("_Smoothness", 0.45f);
					if (neon.HasProperty("_EmissionColor"))
						neon.SetColor("_EmissionColor", new Color(0.08f, 0.07f, 0.06f, 1f));
				}

				if (materials.TryGetValue("City Asphalt Trim", out var trim))
				{
					if (trim.HasProperty("_BaseColor"))
						trim.SetColor("_BaseColor", new Color(0.40f, 0.40f, 0.42f));
				}

				// Billboard — slightly warm; still readable but not glowing purple
				if (materials.TryGetValue("City Billboard", out var billboard))
				{
					if (billboard.HasProperty("_EmissionColor"))
						billboard.SetColor("_EmissionColor", new Color(0.55f, 0.48f, 0.62f));
				}

				// DemoCity office tower facades — light grey aluminium cladding; kill the
				// HDR cyan emission that was set at material creation, it fights desaturation.
				if (materials.TryGetValue("Demo Highrise", out var highrise))
				{
					if (highrise.HasProperty("_BaseColor"))
						highrise.SetColor("_BaseColor", new Color(0.68f, 0.68f, 0.70f));
					if (highrise.HasProperty("_EmissionColor"))
						highrise.SetColor("_EmissionColor", Color.black);
					highrise.DisableKeyword("_EMISSION");
				}

				// DemoCity glass — reflective but unsaturated
				if (materials.TryGetValue("Demo Windows", out var demoWin))
				{
					if (demoWin.HasProperty("_BaseColor"))
						demoWin.SetColor("_BaseColor", new Color(0.44f, 0.48f, 0.52f));
					if (demoWin.HasProperty("_Smoothness")) demoWin.SetFloat("_Smoothness", 0.85f);
					if (demoWin.HasProperty("_Metallic")) demoWin.SetFloat("_Metallic", 0.70f);
				}

				if (materials.TryGetValue("Car Orange", out var paint))
				{
					if (paint.HasProperty("_Metallic")) paint.SetFloat("_Metallic", 0.50f);
					if (paint.HasProperty("_Smoothness")) paint.SetFloat("_Smoothness", 0.78f);
				}

				if (materials.TryGetValue("Hideout Vehicle PBR", out var vehicle))
				{
					if (vehicle.HasProperty("_Smoothness")) vehicle.SetFloat("_Smoothness", 0.62f);
					if (vehicle.HasProperty("_Metallic")) vehicle.SetFloat("_Metallic", 0.58f);
				}

				var quality = QualitySettings.GetQualityLevel();
				if (quality < 3) QualitySettings.SetQualityLevel(3, true);
			}

			private void DesaturateManhattanCar()
			{
				// The Synty atlas texture is vivid teal — _BaseColor only multiplies it,
				// so we must clear the texture entirely and set a solid neutral colour.
				foreach (var key in new[] { "Street Racer Atlas", "Street Racer Chassis" })
				{
					if (!materials.TryGetValue(key, out var mat)) continue;
					// Remove the coloured atlas so _BaseColor is the only colour source.
					mat.mainTexture = null;
					if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
					if (mat.HasProperty("_BaseColor"))
						mat.SetColor("_BaseColor", new Color(0.38f, 0.38f, 0.40f));
					if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
					if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);
					// Kill emission so headlight bleed doesn't re-tint the body.
					if (mat.HasProperty("_EmissionColor"))
						mat.SetColor("_EmissionColor", Color.black);
					mat.DisableKeyword("_EMISSION");
				}
			}

			private void ApplyCitySurfaceMaterialOverrides(bool brooklyn)
			{
				if (materials.TryGetValue("Road", out var road))
				{
					if (road.HasProperty("_BaseColor"))
						road.SetColor("_BaseColor", brooklyn ? new Color(0.68f, 0.69f, 0.72f) : new Color(0.48f, 0.50f, 0.53f));
					if (road.HasProperty("_Smoothness")) road.SetFloat("_Smoothness", brooklyn ? 0.28f : 0.38f);
					if (road.HasProperty("_Metallic")) road.SetFloat("_Metallic", 0.04f);
				}

				if (materials.TryGetValue("Sidewalk", out var sidewalk))
				{
					if (sidewalk.HasProperty("_BaseColor"))
						sidewalk.SetColor("_BaseColor", brooklyn ? new Color(0.60f, 0.62f, 0.66f) : new Color(0.68f, 0.70f, 0.73f));
					if (sidewalk.HasProperty("_Smoothness")) sidewalk.SetFloat("_Smoothness", brooklyn ? 0.40f : 0.35f);
					if (sidewalk.HasProperty("_Metallic")) sidewalk.SetFloat("_Metallic", 0.16f);
				}

				if (materials.TryGetValue("City Neon", out var neon))
				{
					if (neon.HasProperty("_BaseColor")) neon.SetColor("_BaseColor",
						brooklyn ? new Color(0.20f, 0.45f, 0.72f) : new Color(1.0f, 0.88f, 0.65f));
					if (neon.HasProperty("_Smoothness")) neon.SetFloat("_Smoothness", 0.85f);
					if (neon.HasProperty("_Metallic")) neon.SetFloat("_Metallic", 0.58f);
				}
			}

			private void ApplyCityPhotorealSignature(bool brooklyn)
			{
				if (materials.TryGetValue("City Neon", out var neon))
				{
					if (neon.HasProperty("_EmissionColor"))
					{
						neon.SetColor("_EmissionColor", brooklyn
							? new Color(0.32f, 0.58f, 1f, 1f)
							: new Color(0.58f, 0.34f, 1.08f, 1f));
						// Belt and braces. The keyword is enabled where the material is
						// built, but this write is the reason the material has an emission
						// colour at all, and it should not depend on somewhere else having
						// switched it on first - that is exactly how it came to be setting
						// a colour nothing ever read.
						neon.EnableKeyword("_EMISSION");
						neon.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
					}
				}

				if (materials.TryGetValue("Hideout Vehicle PBR", out var vehicle))
				{
					if (vehicle.HasProperty("_EmissionColor"))
						vehicle.SetColor("_EmissionColor", brooklyn
							? new Color(0.04f, 0.05f, 0.06f, 1f)
							: new Color(0.14f, 0.16f, 0.22f, 1f));
				}

				TuneReflectionProbe(brooklyn ? 1.06f : 1.22f, brooklyn
					? new Vector3(58f, 18f, 58f)
					: new Vector3(64f, 24f, 64f));
			}

			private void ApplyCityRoadToneProfile(bool brooklyn)
			{
				if (materials.TryGetValue("Road", out var road))
				{
					if (road.HasProperty("_NormalScale"))
						road.SetFloat("_NormalScale", 0.82f);
					if (road.HasProperty("_OcclusionStrength"))
						road.SetFloat("_OcclusionStrength", brooklyn ? 0.52f : 1.05f);
				}

				if (materials.TryGetValue("Car Orange", out var paint))
				{
					if (paint.HasProperty("_Metallic")) paint.SetFloat("_Metallic", 0.62f);
					if (paint.HasProperty("_Smoothness")) paint.SetFloat("_Smoothness", 0.85f);
				}

				if (materials.TryGetValue("Hideout Vehicle PBR", out var vehicle))
				{
					if (vehicle.HasProperty("_Smoothness")) vehicle.SetFloat("_Smoothness", 0.72f);
					if (vehicle.HasProperty("_Metallic")) vehicle.SetFloat("_Metallic", 0.72f);
				}

				if (materials.TryGetValue("Sidewalk", out var sidewalk))
				{
					if (sidewalk.HasProperty("_NormalScale"))
						sidewalk.SetFloat("_NormalScale", 0.72f);
				}
			}

			private void ApplyCityDepthPass(bool brooklyn)
			{
				ApplyFogDivergence(brooklyn);
				RenderSettings.fog = true;
				RenderSettings.fogMode = FogMode.ExponentialSquared;
				ApplyAmbientOffset(brooklyn);

				// Previous 0.22/0.16 was 50x fogDensity and made horizon 100% fog at 50m (flat surface bug).
				var horizonBias = brooklyn ? 0.0045f : 0.0042f;
				RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, horizonBias, 0.35f);
			}

			private void ApplyFogDivergence(bool brooklyn)
			{
				// Manhattan was 0.0048: at 200 m that is ~60% fog and at 400 m ~95%, so
				// the whole skyline rendered as flat black silhouettes with no visible
				// windows. 0.0028 keeps towers readable to ~500 m while still hazing.
				RenderSettings.fogDensity = brooklyn ? 0.0032f : 0.0028f;
				// Slightly lifted night haze - a real city glows with light pollution
				// rather than fading to pure black.
				RenderSettings.fogColor = brooklyn
					? new Color(0.34f, 0.38f, 0.48f)
					: new Color(0.07f, 0.10f, 0.17f);
			}

			private void ApplyAmbientOffset(bool brooklyn)
			{
				RenderSettings.ambientLight = brooklyn
					? new Color(0.11f, 0.12f, 0.14f)
					: new Color(0.07f, 0.08f, 0.12f);
			}

			private void BuildKowloonNights()
			{
            Random.InitState(88214 ^ chunkSeed);

            // Ground-level pavement market stalls and shelves along the sidewalk
            ScatterBand(12f, 13.5f, 16.5f, (d, l, s) =>
            {
                var stall = PlaceBiomeModelOnRoad("HongKong",
                    Random.value > 0.5f ? "Markets/SM_market_empty" : "Markets/SM_shelf",
                    materials["Kowloon Market Detail"], d, l, 0.14f,
                    new Vector3(-90f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Pavement Stall");
                if (stall != null) NormalizeModelHeight(stall, Random.Range(1.6f, 2.4f), 0.14f);
                return stall;
            });

            // Back tenements behind frontages
            ScatterBand(16f, 26f, 52f, (d, l, s) =>
            {
                var back = PlaceBiomeModelOnRoad("HongKong", $"Buildings_modules/SM_building_0{Random.Range(1, 6)}",
                    materials["Kowloon Skyline"], d, l, 0f,
                    new Vector3(-90f, s > 0 ? -90f : 90f, 0f), Vector3.one, "Back Tenement");
                if (back != null) NormalizeModelHeight(back, Random.Range(22f, 42f));
                return back;
            });

            var facades = new[]
            {
                "Buildings_modules/SM_building_01", "Buildings_modules/SM_building_02",
                "Buildings_modules/SM_building_03", "Buildings_modules/SM_building_04",
                "Buildings_modules/SM_building_05"
            };
            var streetModules = new[]
            {
                "Street_module/SM_street_module_02", "Street_module/SM_street_module_04",
                "Street_module/SM_street_module_06", "Street_module/SM_street_module_09"
            };
            var signs = new[]
            {
                "Signs/SM_sign_04", "Signs/SM_sign_05", "Signs/SM_sign_06",
                "Signs/SM_sign_08", "Signs/SM_sign_01"
            };
            var signGlow = new[]
            {
                new Color(1f, 0.24f, 0.20f), new Color(1f, 0.72f, 0.18f),
                new Color(0.28f, 1f, 0.72f), new Color(1f, 0.32f, 0.62f)
            };

            for (var z = SegBegin(0f, 18f); z < segEnd; z += 18f)
            {
                var block = Mathf.FloorToInt(z / 18f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var facing = side > 0f ? -90f : 90f;
                    var facadeDistance = z + (side > 0 ? 2f : -3f);
                    var facadeLateral = side * Random.Range(15.5f, 19.5f);
                    var facade = PlaceBiomeModelOnRoad("HongKong", facades[BlockHash(block, side) % facades.Length],
                        materials["Kowloon Building"], facadeDistance, facadeLateral, 0f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Kowloon Facade");
                    if (facade != null)
                    {
                        NormalizeModelHeight(facade, Random.Range(20f, 38f));
                        EnsureOutsideRoad(facade, facadeDistance, side);
                    }

                    var moduleDistance = z + (side > 0 ? 10f : -9f);
                    var module = PlaceBiomeModelOnRoad("HongKong", streetModules[BlockHash(block, side * 3) % streetModules.Length],
                        materials["Kowloon Street Detail"], moduleDistance, side * Random.Range(15.0f, 18.0f), 0f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Kowloon Street Front");
                    if (module != null)
                    {
                        NormalizeModelHeight(module, Random.Range(10f, 18f));
                        EnsureOutsideRoad(module, moduleDistance, side);
                    }

                    // Wall-mounted glowing neon signs attached to building facades
                    var signDistance = z + (side > 0 ? 6f : -5f);
                    var sign = PlaceBiomeModelOnRoad("HongKong", signs[BlockHash(block, side * 5) % signs.Length],
                        materials["Kowloon Sign"], signDistance, side * 13.6f, 4.5f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Wall Mounted Neon Sign", false);
                    if (sign != null)
                    {
                        NormalizeModelHeight(sign, Random.Range(2.4f, 4.0f), 4.5f);
                        CreateLocalLight(RoadPath.Point(signDistance, side * 12.0f, 4.8f),
                            signGlow[BlockHash(block, side * 7) % signGlow.Length], 10f, 15f);
                    }

                    // Grounded sidewalk street lamps
                    var lamp = PlaceBiomeModelOnRoad("HongKong", "Lamp/SM_lamp", materials["Kowloon Props"],
                        z + 14f, side * 12.8f, 0.14f, new Vector3(-90f, facing, 0f), Vector3.one, "Street Lamp");
                    if (lamp != null)
                    {
                        NormalizeModelHeight(lamp, 6.4f, 0.14f);
                        CreateLocalLight(RoadPath.Point(z + 14f, side * 12.8f, 5.5f), new Color(1f, 0.82f, 0.55f), 9f, 15f);
                    }
                }
            }
        }

        /// A glazed pedestrian bridge across the avenue, the Gimbels kind.
        ///
        /// Built from primitives rather than from the pack bridge meshes. Those exist -
        /// CyberpunkCity/Bridge/SM_bridge and SM_double_bridge, Synthwave/Bridge/SM_bridge
        /// - but they are LFS-stored, so their span, pivot and orientation cannot be
        /// measured from the repository, and a bridge that lands a pier on the
        /// carriageway or falls short of the far pavement is worse than no bridge. A slab
        /// and two glazed sides are fully determined by numbers in this file, the way the
        /// storefront awnings and the sewer tunnel walls already are. Swapping in a pack
        /// mesh is a good upgrade once somebody can look at one.
        ///
        /// Deck height clears everything that drives under it: the tallest traffic in the
        /// game is a semi at about 4.2 m, and this sits at 14 m, which is also below the
        /// point where the frontage buildings stop.
        private void BuildManhattanSkybridge(float z)
        {
            const float deckHeight = 12.5f;
            const float deckThickness = 1.4f;
            const float deckLength = 9f;
            // Both facade lines plus enough to bury the ends in the buildings, so no gap
            // can open between the bridge and the wall it meets.
            const float span = FrontageSetback * 2f + 3f;

            var trim = materials["City Asphalt Trim"];
            PrimitiveOnRoad(PrimitiveType.Cube, "Manhattan Skybridge", z, 0f, deckHeight,
                new Vector3(span, deckThickness, deckLength), trim, Vector3.zero, false);

            // Glazed sides, lit from the same window material the towers use, so a night
            // avenue gets a bright band across it rather than a black slab.
            var glazing = materials.TryGetValue("City Windows", out var windows) ? windows : trim;
            for (var edge = -1; edge <= 1; edge += 2)
                PrimitiveOnRoad(PrimitiveType.Cube, "Manhattan Skybridge Glazing",
                    z + edge * (deckLength * 0.5f - 0.13f), 0f,
                    deckHeight + deckThickness * 0.5f + 1.1f,
                    new Vector3(span, 3.4f, 0.4f), glazing, Vector3.zero, false);

            // Piers, outside the carriageway and inboard of the pavement furniture, so
            // the bridge reads as carried rather than as a plank laid across the gap.
            for (var pier = -1; pier <= 1; pier += 2)
                PrimitiveOnRoad(PrimitiveType.Cube, "Manhattan Skybridge Pier", z,
                    pier * (FrontageSetback - 1.6f), deckHeight * 0.5f,
                    new Vector3(1.8f, deckHeight, 1.8f), trim, Vector3.zero, false);

            // Roof, so it reads as enclosed from below rather than as a floating floor.
            PrimitiveOnRoad(PrimitiveType.Cube, "Manhattan Skybridge Roof", z, 0f,
                deckHeight + 3.6f, new Vector3(span, 0.5f, deckLength), trim, Vector3.zero, false);

            CreateLocalLight(RoadPath.Point(z, 0f, deckHeight + 1.2f),
                new Color(1f, 0.94f, 0.82f), 16f, 9f);
        }

        /// An open plaza where a frontage building would have stood.
        ///
        /// The variety work gives the street more meshes and more colours, but a canyon
        /// with no break in it still reads as a corridor - so every seventh block on a
        /// side stops building and opens out instead. Paving back to the facade line,
        /// trees and benches around the edge, a fountain in the middle.
        ///
        /// Everything here is a primitive or a prop already in the biome, and the paving
        /// sits at the sidewalk's own height rather than the road's, so a plaza cannot
        /// float or sink where the two differ.
        private void BuildManhattanPlaza(float distance, float side, int block)
        {
            const float pavingHeight = 0.07f;
            const float depth = 17f;
            const float width = 20f;
            // Centre of the plot, measured from the kerb back to behind the facade line.
            var centreLateral = side * (RoadPath.HalfWidth + RoadPath.ShoulderWidth + depth * 0.5f);

            PrimitiveOnRoad(PrimitiveType.Cube, "Manhattan Plaza Paving", distance, centreLateral,
                pavingHeight, new Vector3(depth, 0.14f, width), materials["Sidewalk"],
                Vector3.zero, false);

            // Fountain: a basin and a plinth. Two cylinders read as one from a car, and
            // the biome has nothing else with a curve in it at street level.
            var stone = materials["City Concrete"];
            PrimitiveOnRoad(PrimitiveType.Cylinder, "Manhattan Plaza Fountain", distance,
                centreLateral, pavingHeight + 0.45f, new Vector3(6.5f, 0.45f, 6.5f), stone,
                Vector3.zero, false);
            PrimitiveOnRoad(PrimitiveType.Cylinder, "Manhattan Plaza Fountain", distance,
                centreLateral, pavingHeight + 1.5f, new Vector3(1.6f, 1.1f, 1.6f), stone,
                Vector3.zero, false);

            // Trees and benches around the edge, on the block hash so a plaza is the same
            // every time its chunk is rebuilt.
            var props = materials["City Props"];
            for (var i = 0; i < 4; i++)
            {
                var along = distance + (i < 2 ? -1f : 1f) * (width * 0.32f);
                var across = centreLateral + side * (i % 2 == 0 ? -depth * 0.3f : depth * 0.3f);

                var tree = PlaceBiomeModelOnRoad("Buildings", "DemoCity/tree_1",
                    materials["City Palm"], along, across, pavingHeight,
                    new Vector3(0f, (BlockHash(block, i * 13) & 0x7fffffff) % 360, 0f), Vector3.one,
                    "Manhattan Plaza Tree", false);
                if (tree != null)
                    NormalizeModelHeight(tree, 6.5f + (BlockHash(block, i * 7) & 0x7fffffff) % 3, pavingHeight);

                var bench = PlaceBiomeModelOnRoad("Buildings", "DemoCity/bench", props,
                    along, across - side * 2.6f, pavingHeight,
                    new Vector3(0f, side > 0f ? -90f : 90f, 0f), Vector3.one,
                    "Manhattan Plaza Bench", false);
                if (bench != null) NormalizeModelHeight(bench, 1.0f, pavingHeight);
            }

            // Lamps at the two street-side corners, matching the avenue's own lighting so
            // a plaza is not a dark hole in a lit street.
            for (var corner = -1; corner <= 1; corner += 2)
            {
                var lampAlong = distance + corner * width * 0.4f;
                var lampAcross = side * 17.5f;
                var lamp = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/lampost2",
                    materials["City Asphalt Trim"], lampAlong, lampAcross, pavingHeight,
                    new Vector3(0f, side > 0f ? -90f : 90f, 0f), Vector3.one, "NYC Street Lamp");
                if (lamp != null)
                {
                    NormalizeModelHeight(lamp, 7.5f, pavingHeight);
                    CreateLocalLight(RoadPath.Point(lampAlong, lampAcross, 6.8f),
                        new Color(1f, 0.95f, 0.85f), 8f, 14f);
                }
            }
        }

        /// Per-chunk tallies for the city builder, so a run says what it built instead of
        /// leaving it to be judged from a screenshot. Three rounds of "is it there?" went
        /// by on guesswork; a count is two lines and settles it.
        private int builtSkybridges, builtPlazas, builtFrontages, tintedWalls;

        private void BuildCyberSprawl()
        {
            Random.InitState(41903 ^ chunkSeed);
            builtSkybridges = builtPlazas = builtFrontages = tintedWalls = 0;

            // Ground-level NYC sidewalk clutter: fire hydrants, newspaper boxes, parking meters, chairs
            //
            // Thinned to 16 m once on the theory that the props were what Manhattan was
            // submitting, then put back: RR_PLACEMENT measured this band at 139 renderers,
            // 1.2% of the biome, and the thinning bought 0.9% of the renderer count in
            // total. The submissions were never here.
            ScatterBand(10f, 17.6f, 21.4f, (d, l, s) =>
            {
                var pick = Random.value;
                var propName = pick > 0.65f ? "Buildings/NYCBlock6/Fireplug"
                             : pick > 0.45f ? "Buildings/NYCBlock6/Newspapers"
                             : pick > 0.25f ? "Buildings/NYCBlock6/Parkimeter"
                             : "Buildings/NYCBlock6/Chairs";
                var junk = PlaceBiomeModelOnRoad("Buildings", propName,
                    materials["City Props"], d, l, 0.14f, new Vector3(0f, Random.Range(0f, 360f), 0f), Vector3.one, "NYC Street Furniture");
                if (junk != null) NormalizeModelHeight(junk, Random.Range(1.1f, 1.8f), 0.14f);
                return junk;
            });

            // Parked cars along the kerb.
            //
            // An empty kerb is the strongest single tell that a street was generated. They
            // sit at 14.9 m: outside the carriageway edge at 13.5 and inside the kerb at
            // 16.2, which is the parking lane, and clear of the outermost traffic lane at
            // 0.85 x half width. Facing along the road rather than across it, and not
            // height-normalised - these are the game's own vehicles and already the right
            // size, which is the whole reason for using them rather than a prop.
            ScatterBand(15f, 14.6f, 15.2f, (d, l, s) =>
            {
                var parked = ParkedCars[(BlockHash(Mathf.FloorToInt(d / 15f), Mathf.RoundToInt(s) * 23) & 0x7fffffff) % ParkedCars.Length];
                return PlaceBiomeModelOnRoad("Vehicles", parked, materials["City Props"],
                    d, l, 0.05f, new Vector3(0f, s > 0 ? 180f : 0f, 0f), Vector3.one,
                    "Parked Car", false);
            });

            // Pavement clutter. All of this shipped with the NYCBlock6 pack and none of it
            // was ever placed: bin bags, wire baskets, bollards and a sewer grate. It is
            // the small stuff at ankle height that stops a pavement reading as a ramp.
            ScatterBand(13f, 17.8f, 21.2f, (d, l, s) =>
            {
                var junk = PavementClutter[(BlockHash(Mathf.FloorToInt(d / 13f), Mathf.RoundToInt(s) * 29) & 0x7fffffff) % PavementClutter.Length];
                var piece = PlaceBiomeModelOnRoad("Buildings", junk, materials["City Props"],
                    d, l, 0.14f, new Vector3(0f, Random.Range(0f, 360f), 0f), Vector3.one,
                    "NYC Pavement Clutter");
                if (piece != null) NormalizeModelHeight(piece, Random.Range(0.6f, 1.15f), 0.14f);
                return piece;
            });

            // The skyscraper and frontage rosters that used to live here are gone.
            // Picking a mesh from a list and scaling it to a target height is what
            // produced 93x-wide frontages and 167 m-deep towers; BuildingCatalogue
            // measures every candidate instead and PlaceBuildingOnPlot picks by fit.

            // Street trees, in front of the frontage line rather than behind it. Manhattan
            // reads as canyon walls without something breaking the vertical, and a tree is
            // the cheapest thing that does it at eye level.
            ScatterBand(18f, 17.4f, 18.6f, (d, l, s) =>
            {
                var tree = PlaceBiomeModelOnRoad("Buildings", "DemoCity/tree_1",
                    materials["City Palm"], d, l, 0.14f,
                    new Vector3(0f, Random.Range(0f, 360f), 0f), Vector3.one, "Street Tree");
                if (tree != null) NormalizeModelHeight(tree, Random.Range(5f, 9f), 0.14f);
                return tree;
            });

            var nycRooftops = new[]
            {
                "Buildings/NYCBlock6/roof00", "Buildings/NYCBlock6/roof01",
                "Buildings/NYCBlock6/roof02", "Buildings/NYCBlock6/roof03",
                "Buildings/NYCBlock6/roof04", "Buildings/NYCBlock6/roof05",
                "Buildings/NYCBlock6/roof06", "Buildings/NYCBlock6/roof07",
                "Buildings/NYCBlock6/roof08"
            };

            for (var z = SegBegin(0f, 22f); z < segEnd; z += 22f)
            {
                var block = Mathf.FloorToInt(z / 22f);

                // One skybridge every five blocks, spanning the avenue overhead.
                if (block % 5 == 3) { BuildManhattanSkybridge(z); builtSkybridges++; }

                for (var side = -1; side <= 1; side += 2)
                {
                    var facing = side > 0f ? -90f : 90f;
                    var frontDistance = z + (side > 0 ? 3f : -4f);

                    // 1. Street frontage, on a plot, meeting a continuous facade line.
                    // Facade picked per plot, so neighbours differ the way a real street
                    // does. The hash is the block's, so a chunk rebuilt later is identical.
                    var frontageHash = BlockHash(block, side * 7);
                    // Plot width varies by block rather than being 30 m everywhere. It
                    // feeds the scale clamp, so the same mesh comes out a different size
                    // on a different lot - which is what a real street does, and what a
                    // fixed width made impossible. Narrow enough a range that the facade
                    // line still reads as continuous.
                    var frontagePlot = 26f + (frontageHash >> 3 & 0x7fffffff) % 5 * 2f;

                    // Every seventh block on a side opens out instead of building up. A
                    // canyon with no break in it reads as a corridor however many
                    // different buildings line it, so the variety work above needs
                    // somewhere for the eye to rest as much as it needs more meshes.
                    // Offset by side so the two pavements never open opposite each other
                    // and leave the street with no walls at all.
                    if ((block + (side > 0 ? 0 : 3)) % 7 == 4)
                    {
                        BuildManhattanPlaza(frontDistance, side, block);
                        builtPlazas++;
                        continue;
                    }

                    var frontage = PlaceBuildingOnPlot(BuildingClass.Frontage, FacadeMaterial(frontageHash),
                        "NYC Street Frontage", frontDistance, side,
                        frontageLine: FrontageSetback, plotWidth: frontagePlot, hash: frontageHash);
                    if (frontage != null) builtFrontages++;

                    // Every frontage gets a shop; the awning over it is decided inside.
                    BuildStorefrontAwning(frontage, frontDistance, side, BlockHash(block, side * 17));

                    // 2. Iconic NYC Rooftop Water Tanks & HVAC units
                    // Decoration sitting at 35 m and normalised down to 4.5-8.5 m, so it
                    // reads as silhouette texture at best. First thing to go on a budget.
                    if (block % 2 == 0 && RichDetailBudget)
                    {
                        var roofMesh = nycRooftops[BlockHash(block, side * 11) % nycRooftops.Length];
                        var roofProp = PlaceBiomeModelOnRoad("Buildings", roofMesh,
                            materials["City Asphalt Trim"], frontDistance, side * Random.Range(23.5f, 29.5f), 35f,
                            new Vector3(0f, facing, 0f), Vector3.one, "NYC Rooftop Water Tank");
                        if (roofProp != null)
                        {
                            NormalizeModelHeight(roofProp, Random.Range(4.5f, 8.5f), 35f);
                            CombineChildRenderers(roofProp);
                        }
                    }

                    // Rooftop water tanks on frontage buildings
                    if (block % 3 == 0)
                    {
                        var roofMesh = nycRooftops[BlockHash(block, side * 11) % nycRooftops.Length];
                        var roofProp = PlaceBiomeModelOnRoad("Buildings", roofMesh,
                            materials["City Asphalt Trim"], frontDistance, side * Random.Range(23.5f, 29.5f), 28f,
                            new Vector3(0f, facing, 0f), Vector3.one, "NYC Rooftop Water Tank");
                        if (roofProp != null)
                        {
                            NormalizeModelHeight(roofProp, Random.Range(3.5f, 6.5f), 28f);
                            CombineChildRenderers(roofProp);
                        }
                    }

                    // 3. Towering Background Manhattan Midtown Skyscrapers (65m to 160m)
                    //
                    // Thinned rather than dropped on a low budget. Measured cost: the
                    // NYCVariants prefabs stack a mean of 5.1 sections of 18-25k triangles
                    // each, so one placed tower is around 100k triangles, and RR_MESH found
                    // building_8_middle and building_6_middle alone carrying 55% of
                    // Manhattan's 25.3M triangles across 668 instances. Nothing here has an
                    // LODGroup, so a background tower costs the same at 200 m as at 20 m.
                    //
                    // These sit at FrontageSetback + 26 m - behind the frontage line, read
                    // as skyline rather than as street - so halving them is the cheapest
                    // large cut available. Keeping every other block preserves the ragged
                    // skyline; dropping them entirely leaves a visible flat horizon.
                    if (RichDetailBudget || block % 2 == 0)
                    {
                        var towerDistance = z + Random.Range(-10f, 10f);
                        // Salted differently from the frontage so a block's tower and its
                        // street building are not cut from the same stone.
                        var towerHash = BlockHash(block, side * 3);
                        var towerPlot = 40f + (towerHash >> 3 & 0x7fffffff) % 5 * 2f;
                        PlaceBuildingOnPlot(BuildingClass.MidBlock, FacadeMaterial(towerHash + 2),
                            "Manhattan Midtown Skyscraper", towerDistance, side,
                            frontageLine: FrontageSetback + 26f, plotWidth: towerPlot, hash: towerHash);
                    }

                    // 4. NYC Street Lamposts with warm amber glow
                    var lampDistance = z + (side > 0 ? 10f : -7f);
                    var lamp = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/lampost2", materials["City Asphalt Trim"],
                        lampDistance, side * 17.5f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Street Lamp");
                    if (lamp != null)
                    {
                        NormalizeModelHeight(lamp, 7.5f, 0.14f);
                        CreateLocalLight(RoadPath.Point(lampDistance, side * 17.5f, 6.8f),
                            new Color(1f, 0.95f, 0.85f), 8f, 14f);
                    }

                    // Traffic lights at intersections. Every second block rather than
                    // every fourth: at 22 m blocks that is a signal every 44 m, which is
                    // roughly a Manhattan cross street, and a lit avenue with a signal
                    // every 88 m read as a road with occasional furniture on it.
                    if (block % 2 == 1)
                    {
                        var trafficLight = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/Trafficlight", materials["City Props"],
                            z + 12f, side * 17.2f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Traffic Light");
                        if (trafficLight != null) NormalizeModelHeight(trafficLight, 6.5f, 0.14f);
                    }

                    // Bus shelters and advertising billboards
                    if (block % 5 == 2)
                    {
                        var shelter = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/Busstop", materials["City Props"],
                            z + 16f, side * 18.6f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Bus Shelter");
                        if (shelter != null) NormalizeModelHeight(shelter, 3.4f, 0.14f);
                    }
                    else if (block % 4 == 3)
                    {
                        var panelName = Random.value > 0.5f ? "NYCBlock6/Panel00" : "NYCBlock6/Panel01";
                        var panel = PlaceBiomeModelOnRoad("Buildings", panelName, materials["City Billboard"],
                            z + 16f, side * 19.8f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Street Billboard");
                        if (panel != null) NormalizeModelHeight(panel, 4.2f, 0.14f);
                    }

                    // Benches every few blocks
                    if (block % 6 == 0)
                    {
                        var bench = PlaceBiomeModelOnRoad("Buildings", "DemoCity/bench",
                            materials["City Props"], z + 8f, side * 19.2f, 0.14f,
                            new Vector3(0f, facing, 0f), Vector3.one, "City Bench");
                        if (bench != null) NormalizeModelHeight(bench, 1.0f, 0.14f);
                    }
                }
            }

            Debug.Log($"RR_CITY chunk built {builtFrontages} frontage(s), {builtSkybridges} " +
                      $"skybridge(s), {builtPlazas} plaza(s), {tintedWalls} tinted wall material(s)");
        }

        /// Cyberpunk and DemoCity street clutter dressed onto NEON CITY's sidewalks.
        private void DressNeonSidewalk(float distance, float side, int block)
        {
            var facing = side > 0f ? -90f : 90f;
            var pick = BlockHash(block, 21) % 4;
            var clutter = pick == 0 ? "Trashbag/SM_trashbag_group_01"
                : pick == 1 ? "Crates/SM_crate_01"
                : pick == 2 ? "Trashcan/SM_trashcan_01"
                : "Buildings/DemoCity/bench";
            var clutterMaterial = pick == 0 ? materials["Cyber Trash"]
                : pick == 1 ? materials["Cyber Crate"] : materials["Cyber Props"];
            var piece = PlaceBiomeModelOnRoad("CyberpunkCity", clutter, clutterMaterial,
                distance, side * 15.4f, 0.05f, new Vector3(-90f, facing, 0f), Vector3.one, "Sidewalk Clutter");
            if (piece != null) NormalizeModelHeight(piece, Random.Range(0.8f, 1.5f), 0.05f);

            // Highway US Speed Limit Signs
            if (block % 6 == 0)
            {
                var sign = PlaceBiomeModelOnRoad("Props", "Signs/Sign Post 1", materials["City Props"],
                    distance + 4f, side * 12.8f, 0.05f, new Vector3(0f, facing + 90f, 0f), Vector3.one, "Speed Limit Sign");
                if (sign != null) NormalizeModelHeight(sign, 3.2f, 0.05f);
            }

            if (block % 2 != 0) return;
            var aircon = PlaceBiomeModelOnRoad("CyberpunkCity", "Aircon/SM_aircon_01", materials["Cyber Props"],
                distance + 6f, side * 16.2f, 2.6f, new Vector3(-90f, facing, 0f), Vector3.one, "Wall Aircon");
            if (aircon != null) NormalizeModelHeight(aircon, 1.1f, 2.6f);
        }

        private void BuildCityOverpass(float distance, int block)
        {
            var overpass = PlaceBiomeModelOnRoad("Synthwave", block % 2 == 0 ? "Bridge/SM_bridge" : "Arch/SM_arch",
                materials["City Concrete"], distance, 0f, 0f, new Vector3(-90f, 0f, 0f), Vector3.one,
                "City Overpass", false);
            if (overpass == null) return;
            NormalizeModelSpan(overpass, 54f, block % 2 == 0 ? 12.5f : 0.1f);
            CreateLocalLight(RoadPath.Point(distance, -9f, 10f), new Color(0.24f, 0.9f, 1f), 14f, 24f);
            CreateLocalLight(RoadPath.Point(distance, 9f, 10f), new Color(1f, 0.2f, 0.66f), 14f, 24f);
        }

        private void BuildRedCanyon()
        {
            Random.InitState(30517 ^ chunkSeed);

            var cliffs = new[]
            {
                "Cliff/SM_rock_01", "Cliff/SM_rock_02", "Cliff/SM_rock_03", "Cliff/SM_rock_04",
                "Cliff/SM_rock_05", "Cliff/SM_rock_06", "Cliff/SM_rock_07"
            };
            var stones = new[]
            {
                "Stones/SM_stone_01", "Stones/SM_stone_02", "Stones/SM_stone_03",
                "Stones/SM_stone_04", "Stones/SM_stone_05"
            };
            var palms = new[]
            {
                "Tree/SM_palm_tree_01", "Tree/SM_palm_tree_02", "Tree/SM_palm_tree_03",
                "Tree/SM_palm_tree_04", "Tree/SM_palm_tree_05", "Tree/SM_palm_tree_06"
            };
            var groundCover = new[] { "Grass/SM_grass_01", "Grass/SM_grass_Clamp", "Grass/SM_plant_01_Group" };

            // Layer 1: Roadside Desert Scrub and Talus Boulders along the verge
            ScatterBand(5.5f, 9.2f, 12.5f, (d, l, s) =>
                PlaceBiomeModelOnRoad("RedCanyon", groundCover[Random.Range(0, groundCover.Length)],
                    materials["Canyon Grass"], d, l, 0.05f, new Vector3(-90f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(0.75f, 1.45f), "Desert Scrub"));

            ScatterBand(6.5f, 10.0f, 13.5f, (d, l, s) =>
                PlaceBiomeModelOnRoad("RedCanyon", stones[Random.Range(0, stones.Length)],
                    materials["Canyon Stone"], d, l, 0.05f, new Vector3(-90f, Random.Range(0f, 360f), 0f),
                    Vector3.one * Random.Range(0.6f, 1.3f), "Verge Boulder"));

            // Layer 2: Tight Continuous Canyon Wall corridor
            for (var z = SegBegin(-8f, 12f); z < segEnd; z += 12f)
            {
                var step = Mathf.FloorToInt(z / 12f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var wallDistance = z + (side > 0 ? 2f : -2f) + Random.Range(-2.5f, 2.5f);
                    var wallMaterial = (step + (side > 0 ? 1 : 0)) % 2 == 0 ? materials["Canyon Cliff A"] : materials["Canyon Cliff B"];
                    var wall = PlaceBiomeModelOnRoad("RedCanyon", cliffs[Mathf.Abs(step * 3 + (side > 0 ? 1 : 4)) % cliffs.Length],
                        wallMaterial, wallDistance, side * Random.Range(14.0f, 18.5f), -0.5f,
                        new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Canyon Wall");
                    if (wall != null)
                    {
                        NormalizeModelHeight(wall, Random.Range(24f, 44f), -0.5f);
                        EnsureOutsideRoad(wall, wallDistance, side);
                    }

                    // Layer 3: Distant Monument Valley / Mesas
                    if (step % 2 == 0)
                    {
                        var mesaDistance = z + Random.Range(-10f, 10f);
                        var mesa = PlaceBiomeModelOnRoad("RedCanyon", cliffs[Mathf.Abs(step * 5 + (side > 0 ? 6 : 2)) % cliffs.Length],
                            materials["Canyon Cliff B"], mesaDistance, side * Random.Range(48f, 92f), -1.5f,
                            new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Distant Mesa");
                        if (mesa != null) NormalizeModelHeight(mesa, Random.Range(55f, 110f), -1.5f);
                    }
                }

                // Oasis vegetation nestled in rock alcoves
                if (step % 3 == 1)
                {
                    var side = step % 6 < 3 ? 1f : -1f;
                    var oasisDistance = z + 4f;
                    var palm = PlaceBiomeModelOnRoad("RedCanyon", palms[Mathf.Abs(step) % palms.Length],
                        materials["Palm Bark"], oasisDistance, side * Random.Range(13.5f, 17f), 0f,
                        new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Canyon Palm");
                    if (palm != null) NormalizeModelHeight(palm, Random.Range(8.5f, 14f));

                    var bush = PlaceBiomeModelOnRoad("RedCanyon", "Tree/SM_Tree_Bush", materials["Palm Bark"],
                        z + 8f, -side * Random.Range(13.0f, 16.5f), 0f,
                        new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "Canyon Bush");
                    if (bush != null) NormalizeModelHeight(bush, Random.Range(2.8f, 4.8f));
                }
            }
        }

        /// One multiplier over every street light, so brightness is a single number rather
        /// than fourteen call sites. The intensities the callers pass were authored when
        /// these lights last worked; this respects them at 1.0 and exists to be turned
        /// once, looked at, and left alone.
        private const float StreetLightGain = 1f;

        /// Street lamps, sign glow, garage and facility lights.
        ///
        /// This method was an empty body. Fourteen call sites across every biome computed
        /// a world position, a colour, an intensity and a range, and handed them to
        /// nothing - so Manhattan at night had no street lighting whatsoever and every
        /// facade outside the directional light's reach fell to whatever the ambient term
        /// gave it. That is the "everywhere is black" of it.
        ///
        /// They were removed because they read as flat discs on the road. Two things about
        /// this build make that likely and both are addressed here rather than by deleting
        /// the lights again: the renderer is Forward with an additional-lights-per-object
        /// limit of 4, so beyond four the nearest ones simply pop in and out as you drive;
        /// and additional-light shadows are enabled project-wide, so each point light was
        /// rendering a shadow cubemap for a lamp post.
        private void CreateLocalLight(Vector3 position, Color color, float intensity, float range)
        {
            var holder = Adopt(new GameObject("Local Light"));
            holder.transform.position = position;

            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity * StreetLightGain;
            light.range = range;
            // A lamp post does not need to cast shadows, and with additional-light shadows
            // on in the pipeline asset, letting it would mean a cubemap render apiece.
            light.shadows = LightShadows.None;
            // Off until the pool decides it is one of the nearest, so the budget is never
            // exceeded even for a frame.
            light.enabled = false;

            LocalLights.Register(light);
        }

        /// ScatterBand wants a GameObject-returning spawn; the lamp builder returns void.
        private GameObject CreateStreetLampAt(float distance, float lateral, Color color)
        {
            CreateStreetLamp(distance, lateral, color);
            return null;
        }

        private void CreateStreetLamp(float distance, float lateral, Color color)
        {
            var root = Adopt(new GameObject("Roadside Street Lamp"));
            root.transform.position = RoadPath.Point(distance, lateral, 0f);
            root.transform.rotation = RoadPath.Rotation(distance);
            var pole = Primitive(PrimitiveType.Cylinder, "Lamp Pole", Vector3.zero,
                new Vector3(0.10f, 2.8f, 0.10f), materials["Car Dark"], root.transform);
            pole.transform.localPosition = new Vector3(0f, 2.8f, 0f);
            var fixture = Primitive(PrimitiveType.Sphere, "Lamp Fixture", Vector3.zero,
                new Vector3(0.34f, 0.22f, 0.34f), materials["City Neon"], root.transform);
            fixture.transform.localPosition = new Vector3(0f, 5.72f, 0f);
            EnsureOutsideRoad(root, distance, Mathf.Sign(lateral));
            CreateLocalLight(root.transform.TransformPoint(new Vector3(0f, 5.55f, 0f)), color, 7f, 14f);
        }

        /// Density multiplier for scatter passes. Lowering this on weaker hardware thins
        /// every biome uniformly instead of needing per-biome mobile variants.
        /// Foliage thinning. The mobile figure is not a guess at a nice-looking density:
        /// Gate A measured the device at under 1 FPS with the desktop set, and screen
        /// coverage of alpha-tested foliage is the thing that has to come down.
        private float ScatterDensity =>
            !RichDetailBudget ? 0.34f
            : QualitySettings.GetQualityLevel() <= 1 ? 0.55f
            // 1.45 on desktop, up from 1.0. The budget for this was measured, not guessed:
            // Greenwood runs at 341 fps average with 7.4 ms of GPU in a 9.9 ms frame, and
            // the bush swap took 5.5M triangles out of it. The mobile and low tiers are
            // untouched - that 0.34 is the figure Gate A arrived at after measuring the
            // device at under 1 fps, and screen coverage of alpha-tested foliage is still
            // what has to come down there.
            : 1.45f;

        /// Scatters a prop along a lateral band beside the road. Biomes read as real when
        /// props sit in overlapping depth bands (verge / near / mid / far) rather than as a
        /// single line, so this exists to be called several times per biome with different
        /// bands rather than open-coding each loop.
        /// Segment currently being built. The streamer sets these before invoking a biome
        /// builder so the same builder can fill any stretch of an endless road.
        private float segStart;
        private float segEnd = WorldLength;

        /// While a chunk is building, everything spawned is parented here so the whole
        /// segment can be torn down in one Destroy when it falls behind the player.
        private Transform chunkRoot;

        private T Adopt<T>(T created) where T : Object
        {
            if (chunkRoot == null || created == null) return created;
            var go = created as GameObject ?? (created as Component)?.gameObject;
            if (go != null && go.transform.parent == null) go.transform.SetParent(chunkRoot, true);
            return created;
        }

        /// First multiple of `step` (offset by `phase`) at or after the segment start.
        /// Anchoring to absolute distance rather than the segment keeps props in the same
        /// world positions no matter which chunk builds them, so nothing shifts at seams.
        /// Deterministic scramble for block-derived picks. Depends only on its inputs,
        /// so a rebuilt chunk chooses identically, but consecutive blocks land on
        /// unrelated entries instead of cycling on a short visible period.
        private static int BlockHash(int block, int salt)
        {
            unchecked
            {
                var h = block * 374761393 + salt * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & int.MaxValue;
            }
        }

        private float SegBegin(float phase, float step)
        {
            var k = Mathf.Ceil((segStart - phase) / step);
            return phase + k * step;
        }

        private void ScatterBand(float step, float nearLateral, float farLateral,
            System.Func<float, float, float, GameObject> spawn, float jitter = 0.6f, float startZ = -20f)
        {
            // Floor was 3 m, which silently ignored the dense undergrowth bands. The
            // forest kits' ground cover is 20-192 tris, so sub-2 m spacing is affordable
            // on desktop; ScatterDensity still thins it on mobile.
            // Every chunk used identical band geometry, so a 150 m tile repeated down the
            // whole zone - the "looping" that reads as the same street over and over even
            // though the biome itself has not changed. Modulate each band per chunk:
            // spacing, lateral offset and width all shift, and bands occasionally drop out
            // entirely, so consecutive chunks stop being the same layout with new seeds.
            var v = new System.Random(chunkSeed ^ Mathf.RoundToInt(nearLateral * 31f + farLateral * 7f));
            var densityMul = 0.65f + (float)v.NextDouble() * 0.75f;
            // Outward only: an inward shift could drag a roadside band onto the
            // carriageway, which is how props ended up in the traffic lanes.
            var shift = (float)v.NextDouble() * (farLateral - nearLateral) * 0.35f;
            var widen = 0.8f + (float)v.NextDouble() * 0.55f;
            if (v.NextDouble() < 0.02) return;   // occasional gap: a clearing, a vacant lot

            // Never inside the carriageway. Every band here is written as an absolute
            // distance from the centreline, chosen against whatever the road was when it
            // was written - and the road just went to three lanes each way in every biome,
            // which moved the kerb from 4.5 m to 13.5 m on the country roads. Twenty-two
            // of these bands would now start on the tarmac.
            //
            // Clamped rather than dropped: a band pushed to the kerb still dresses the
            // roadside, where skipping it would empty whole biomes of their undergrowth.
            // The width is preserved, so a band that was 3 m deep stays 3 m deep.
            var clearance = RoadPath.ClearanceAt(segStart) + 1.5f;
            if (nearLateral < clearance)
            {
                farLateral += clearance - nearLateral;
                nearLateral = clearance;
            }

            var near = Mathf.Max(1f, nearLateral + shift);
            var far = Mathf.Max(near + 1f, nearLateral + shift + (farLateral - nearLateral) * widen);

            var spacing = Mathf.Max(1.6f, step / Mathf.Max(0.2f, ScatterDensity * densityMul));
            for (var z = SegBegin(startZ, spacing); z < segEnd; z += spacing)
            for (var side = -1; side <= 1; side += 2)
            {
                // Independent per-side dropout breaks the mirrored look as well.
                if (v.NextDouble() < 0.02) continue;
                var distance = z + Random.Range(-spacing * jitter, spacing * jitter);
                var lateral = side * Random.Range(near, far);
                spawn(distance, lateral, side);
            }
        }

        private GameObject ScatterModel(string resourceName, Material material, float distance,
            float lateral, float height, float minScale, float maxScale, string label)
        {
            var model = Model(resourceName, material);
            if (model == null) return null;
            model.name = label;
            model.transform.position = RoadPath.Point(distance, lateral, height + RealGroundUnder(distance, lateral, 1.5f));
            model.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            model.transform.localScale = Vector3.one * Random.Range(minScale, maxScale);
            EnsureOutsideRoad(model, distance, Mathf.Sign(lateral));
            SettleOnRealGround(model, distance, lateral, 1.5f);
            return model;
        }

        /// Nine tree models across the two forest kits. Broadleaf and pine are mixed by
        /// weight rather than picked uniformly - a stand that is half conifer reads as
        /// planted, not wild.
        private static readonly string[] BroadleafTrees =
        {
            "RunicForest|Tree/SM_Forest_tree_01",
            "RunicForest|Tree/SM_central_tree",
            "ForestVillage|Vegetation/Update/SM_tree_01_Update",
            "ForestVillage|Vegetation/Update/SM_tree_02_Update",
        };
        private static readonly string[] PineTrees =
        {
            "RunicForest|Tree/SM_pine_tree_01",
            "RunicForest|Tree/SM_pine_tree_02",
            "ForestVillage|Vegetation/SM_pine_tree_02",
            "ForestVillage|Vegetation/SM_pine_tree_03",
            "ForestVillage|Vegetation/SM_pine_tree_04",
            "ForestVillage|Vegetation/SM_pine_tree_05",
        };
        private static readonly string[] ForestPlants =
        {
            "RunicForest|Vegetation/SM_plant_01",
            "RunicForest|Vegetation/SM_plant_02",
            "RunicForest|Vegetation/SM_plant_ground",
            "RunicForest|Vegetation/SM_plant_ground_02",
            "RunicForest|Vegetation/SM_bush_01",
            "RunicForest|Vegetation/SM_bush_02",
            "RunicForest|Flowers/SM_dead_grass",
            "ForestVillage|Vegetation/SM_plant",
            "ForestVillage|Vegetation/SM_plant1",
        };

        /// The undergrowth band used to be one hardcoded mesh, ForestVillage's SM_bush,
        /// and it was 31.7% of Greenwood on its own: 409 instances at 15.8k triangles
        /// each, 6446k of the biome's 20357k. Its FBX is 1923 KB and imports with no
        /// LODs; every other bush in the project is 25-70 KB. It was carrying scanned
        /// density into a 1.6-3.2 m prop scattered every 5.5 m and read past at speed,
        /// where none of that silhouette survives.
        ///
        /// These two are already in ForestPlants, so Greenwood has been drawing them all
        /// along one band over - same biome, same "Forest Undergrowth" material, already
        /// right by eye. The swap is 25 KB of mesh where 1923 KB was.
        private static readonly string[] ForestBushes =
        {
            "RunicForest|Vegetation/SM_bush_01",
            "RunicForest|Vegetation/SM_bush_02",
        };

        private static readonly string[] JungleFronds =
        {
            "JungleRuins|Plants/SM_plant_02", "JungleRuins|Plants/SM_plant_03",
            "JungleRuins|Plants/SM_plant_08", "JungleRuins|Plants/SM_plant_13",
            "JungleRuins|Plants/SM_plant_15", "JungleRuins|Plants/SM_plant_16",
        };

        private GameObject SpawnForestPiece(string entry, float distance, float lateral, float height,
            float minHeight, float maxHeight, string label)
        {
            // Nothing planted in front of a cliff face - but behind it, the forest
            // carries on over the rock, as it does above a real cutting. A bare top
            // and back slope read as a pale slab.
            var onCliff = false;
            var cliffY = 0f;
            if (InCliffZone(distance, lateral))
            {
                var line = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + CliffOffset;
                if (Mathf.Abs(lateral) < line + 10f || !CliffTop(distance, lateral, out cliffY)) return null;
                onCliff = true;
            }
            if (!RouteAllows(label, distance, lateral)) return null;
            if (InHotelGrounds(distance, lateral)) return null;
            if (InTestAssetGround(distance, lateral)) return null;
            var split = entry.Split('|');
            var external = split[0] == "External";
            var blackForest = external || split[0] == "BlackForest";
            GameObject model;
            if (external)
            {
                var pool = split[1].StartsWith("y") ? ExternalYoungTrees
                    : split[1].StartsWith("b") ? ExternalBroadleaf
                    : ExternalTrees;
                var prefab = pool.Length > 0 ? pool[int.Parse(split[1].TrimStart('y', 'b')) % pool.Length] : null;
                model = prefab != null ? Adopt(Instantiate(prefab)) : null;
            }
            else
            {
                var material = !blackForest ? materials["Forest Undergrowth"]
                    : split[1].StartsWith("SM_log") || split[1].StartsWith("SM_stump") ? materials["B500 Props"]
                    : BlackForestTint();
                model = BiomeModel(split[0], split[1], material);
            }
            if (model == null) return null;
            model.name = label;
            model.transform.position = RoadPath.Point(distance, lateral, height + (onCliff ? 0f : RealGroundUnder(distance, lateral, 0.9f)));
            if (onCliff)
            {
                var p = model.transform.position;
                model.transform.position = new Vector3(p.x, cliffY + height - 0.3f, p.z);
            }
            // The kit meshes lie on their backs (Z up); the Black Forest ones are
            // exported Y up and stand straight whatever the road's grade.
            model.transform.rotation = blackForest
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : RoadPath.Rotation(distance) * Quaternion.Euler(-90f, Random.Range(0f, 360f), 0f);
            model.transform.localScale = Vector3.one;
            NormalizeModelHeight(model, Random.Range(minHeight, maxHeight), height);
            if (!onCliff && OverRealTerrain)
            {
                var placedAt = model.transform.position.y;
                if (!RaycastGround(model.transform.position, 0.9f, out var groundY))
                {
                    plantTally.NoGround++;
                    Destroy(model);
                    return null;
                }
                // How far the ray moved it from where the terrain maths put it. Metres
                // apart only where ground strips still cross (the top one is often a
                // sliver seen edge-on from the road), and a piece stood there hung in
                // the air or sank from the road's point of view: it is left out.
                var shift = Mathf.Abs(groundY + height - placedAt);
                if (shift > MaxPlantingShift)
                {
                    plantTally.Ambiguous++;
                    Destroy(model);
                    return null;
                }
                plantTally.OnRay++;
                plantTally.Shift = Mathf.Max(plantTally.Shift, shift);
                StandOn(model, groundY + height);
            }
            if (external) ThinExternalTree(model, lateral);
            else ThinForestPiece(model, lateral, label);
            return model;
        }

        // ------------------------------------------------------------ the real B500

        /// Whether a forest piece may stand here, going by what really lines the B500
        /// at this point (ESA WorldCover, baked into the route by
        /// Tools/Terrain/build_b500_road.py). The forest scatter fills everything with
        /// trees; the real road runs through forest most of the way but opens onto the
        /// Grinden heath at Schliffkopf, ski meadows at Unterstmatt and Alexanderschanze,
        /// the hotel clearings at Buehlerhoehe and Ruhestein, Kniebis, and the
        /// Mummelsee. Without cover data everything but the heath dressing is allowed.
        private static bool RouteAllows(string label, float distance, float lateral)
        {
            var heath = label.StartsWith("Heath");
            var cover = RoadPath.Route != null ? RoadPath.Route.CoverAt(distance, lateral) : 0;
            switch (cover)
            {
                case 0: return !heath;
                case RoadRoute.CoverWater: return false;
                case RoadRoute.CoverForest: return !heath;
            }
            // Open ground or a clearing: heath and meadow with trees standing along it
            // and in groups - not a bare field. A built-up patch (a hotel, a car park)
            // has more trees round it than open heath does.
            if (heath) return cover == RoadRoute.CoverOpen || Random.value < 0.5f;
            var built = cover == RoadRoute.CoverBuilt;
            if (label == "Forest Tree" || label == "Forest Understory") return Random.value < (built ? 0.35f : 0.12f);
            if (label.StartsWith("Forest Bush")) return Random.value < (built ? 0.5f : 0.35f);
            return true;
        }

        private static readonly string[] HeathPlants =
        {
            "BlackForest|SM_moor_grass", "BlackForest|SM_moor_grass", "BlackForest|SM_moor_grass",
            "BlackForest|SM_bilberry", "BlackForest|SM_bilberry", "BlackForest|SM_fern",
        };

        private static readonly string[] HeathRocks =
        {
            "RunicForest|Small_rocks/SM_rock_01",
            "RunicForest|Small_rocks/SM_rock_02",
            "RunicForest|Small_rocks/SM_rock_03",
        };

        /// The open stretches: moor grass and heather, granite blocks and a few
        /// wind-bent spruce, with nothing tall between the road and the horizon - the
        /// long views the Schwarzwaldhochstrasse is known for. Only planted where the
        /// real roadside is open (RouteAllows), so in forest these bands place nothing.
        private void BuildRouteOpenGround()
        {
            ScatterBand(1.6f, 8f, 60f, (d, l, s) =>
                SpawnForestPiece(HeathPlants[Random.Range(0, HeathPlants.Length)], d, l, 0.05f, 0.5f, 1.3f, "Heath Grass"));
            ScatterBand(4.5f, 60f, 150f, (d, l, s) =>
                SpawnForestPiece(HeathPlants[Random.Range(0, HeathPlants.Length)], d, l, 0.05f, 0.7f, 1.6f, "Heath Grass Far"));
            ScatterBand(15f, 10f, 100f, (d, l, s) =>
                SpawnForestPiece(HeathRocks[Random.Range(0, HeathRocks.Length)], d, l, -0.15f, 0.4f, 1.8f, "Heath Rock"));
            ScatterBand(24f, 18f, 170f, (d, l, s) =>
                SpawnForestPiece(Random.value < 0.5f ? "BlackForest|SM_spruce_young" : BlackForestTrees[Random.Range(0, BlackForestTrees.Length)],
                    d, l, 0f, 4f, 13f, "Heath Spruce"));
        }

        /// Water beside the road (the Mummelsee): a still, dark lake surface from
        /// where the water starts out to 260 m, level across its length. The ground
        /// ribbon dips under it (see BuildRibbon), so the shore is a slope, not a seam.
        private void BuildRouteLakes()
        {
            const float step = 5f;
            for (var side = -1; side <= 1; side += 2)
            {
                var runStart = float.NaN;
                for (var d = segStart - 2f; d <= segEnd + 2f + step; d += step)
                {
                    var water = d <= segEnd + 2f && !float.IsNaN(LakeInnerEdge(d, side));
                    if (water)
                    {
                        if (float.IsNaN(runStart)) runStart = d;
                        continue;
                    }
                    if (float.IsNaN(runStart)) continue;
                    BuildLake(runStart - step * 0.5f, d - step * 0.5f, side);
                    runStart = float.NaN;
                }
            }
        }

        /// Distance from the centreline at which water starts on this side, or NaN.
        private static float LakeInnerEdge(float distance, int side)
        {
            // One probe per cover band (12-40, 40-100, 100-220 m); water starts at the
            // band's inner edge.
            if (RoadPath.Route.CoverAt(distance, side * 20f) == RoadRoute.CoverWater)
                return Mathf.Max(RoadPath.ClearanceAt(distance) + 6f, 12f);
            if (RoadPath.Route.CoverAt(distance, side * 60f) == RoadRoute.CoverWater) return 40f;
            if (RoadPath.Route.CoverAt(distance, side * 140f) == RoadRoute.CoverWater) return 100f;
            return float.NaN;
        }

        private void BuildLake(float from, float to, int side)
        {
            const float outer = 260f;
            const int across = 8;
            // Level and shoreline from the whole lake, not this chunk's share of it, so
            // a lake crossing a chunk seam has one surface.
            var lakeFrom = from;
            var lakeTo = to;
            while (lakeFrom > from - 800f && !float.IsNaN(LakeInnerEdge(lakeFrom - 5f, side))) lakeFrom -= 5f;
            while (lakeTo < to + 800f && !float.IsNaN(LakeInnerEdge(lakeTo + 5f, side))) lakeTo += 5f;
            var level = float.MaxValue;
            var inner = float.MaxValue;
            for (var d = lakeFrom; d <= lakeTo; d += 5f)
            {
                level = Mathf.Min(level, RoadPath.Center(d).y);
                var edge = LakeInnerEdge(d, side);
                if (!float.IsNaN(edge)) inner = Mathf.Min(inner, edge);
            }
            if (inner == float.MaxValue) return;
            level -= 0.9f;
            var samples = Mathf.Max(2, Mathf.CeilToInt((to - from) / 5f) + 1);
            var vertices = new Vector3[samples * across];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(samples - 1) * (across - 1) * 6];
            var t = 0;
            for (var i = 0; i < samples; i++)
            {
                var d = Mathf.Lerp(from, to, i / (float)(samples - 1));
                for (var j = 0; j < across; j++)
                {
                    var lateral = side * Mathf.Lerp(inner, outer, j / (float)(across - 1));
                    var p = RoadPath.Point(d, lateral);
                    vertices[i * across + j] = new Vector3(p.x, level, p.z);
                    uv[i * across + j] = new Vector2(p.x * 0.02f, p.z * 0.02f);
                }
                if (i == samples - 1) continue;
                for (var j = 0; j < across - 1; j++)
                {
                    var v = i * across + j;
                    // Wound to face up on either side of the road.
                    if (side > 0)
                    {
                        triangles[t++] = v; triangles[t++] = v + across; triangles[t++] = v + across + 1;
                        triangles[t++] = v; triangles[t++] = v + across + 1; triangles[t++] = v + 1;
                    }
                    else
                    {
                        triangles[t++] = v; triangles[t++] = v + across + 1; triangles[t++] = v + across;
                        triangles[t++] = v; triangles[t++] = v + 1; triangles[t++] = v + across + 1;
                    }
                }
            }
            EnableProbeReflections(CreateMeshObject("Route Lake", vertices, triangles, uv, materials["Mountain Lake"]));
        }

        /// The ground dips under water, so a lake has a shore rather than hills
        /// showing through it.
        private static float LakeBasin(float distance, float lateral) =>
            RoadPath.Route != null && RoadPath.Route.CoverAt(distance, lateral) == RoadRoute.CoverWater ? -3f : 0f;

        private int lastPlaceIndex = -1;

        /// Names each stop on the B500 as the player passes it, both ways.
        private void AnnouncePlaces(float distance)
        {
            var route = RoadPath.Route;
            // During a staged run the stage clock names the stops.
            if (route == null || RoadRageB500Stages.Running) return;
            var u = route.Fold(distance);
            for (var i = 0; i < route.Places.Length; i++)
            {
                if (i == lastPlaceIndex || Mathf.Abs(route.Places[i].Distance - u) > 40f) continue;
                lastPlaceIndex = i;
                GameState.Show($"📍 {route.Places[i].Name.ToUpperInvariant()}");
                break;
            }
        }

        // ------------------------------------------------------------ B500 roadside

        private const float PlaceSignLead = 120f;
        private const float HotelSetback = 32f;

        /// What a driver on the real road sees besides the trees: white delineator
        /// posts every 50 m, the yellow B 500 route marker, a name sign before each
        /// stop, the yellow-and-red Westweg signposts where the trail crosses, stacked
        /// timber at the forest edge, and the big hotels at the clearings.
        private void BuildRouteProps()
        {
            var route = RoadPath.Route;
            var rail = RoadPath.HalfWidthAt(segStart) + RoadPath.ShoulderWidth + GuardRailOffset;

            // Leitpfosten, both sides, every 50 m of road.
            for (var d = Mathf.Ceil(segStart / 50f) * 50f; d < segEnd; d += 50f)
            for (var side = -1; side <= 1; side += 2)
                PlaceRouteProp("SM_leitpfosten", d, side * (RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 0.45f),
                    FacingOncoming(d), "B500 Leitpfosten");

            // Route marker on the right about every 3 km.
            for (var d = Mathf.Ceil(segStart / 3000f) * 3000f; d < segEnd; d += 3000f)
                PlaceRouteProp("SM_sign_b500", d + 40f, RoadPath.HalfWidthAt(d + 40f) + RoadPath.ShoulderWidth + GuardRailOffset + 1.0f,
                    FacingOncoming(d + 40f), "B500 Sign");

            // Place names and signposts, in whichever direction this pass runs.
            for (var i = 0; i < route.Places.Length; i++)
            {
                var place = route.Places[i].Distance;
                foreach (var d in PassesOf(place - PlaceSignLead, place + PlaceSignLead))
                    PlaceRouteProp($"SM_sign_place_{i:00}", d, RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 1.2f,
                        FacingOncoming(d), "B500 Place Sign");
                if (i == 0 || i == route.Places.Length - 1) continue;
                foreach (var d in PassesOf(place + 25f, place - 25f))
                {
                    var lateral = -(RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 2.5f);
                    if (!InCliffZone(d, lateral))
                        PlaceRouteProp("SM_signpost_hike", d, lateral, FacingOncoming(d) * Quaternion.Euler(0f, 30f, 0f),
                            "B500 Signpost");
                }
                if (HotelSide(i, out var side))
                    foreach (var d in PassesOf(place, place))
                    {
                        var lateral = side * (RoadPath.ClearanceAt(d) + HotelSetback);
                        if (!InCliffZone(d, lateral))
                            PlaceRouteProp("SM_hotel", d, lateral, Quaternion.LookRotation(Flat(-side * RoadPath.Right(d))),
                                "B500 Hotel", 0.4f);
                    }
            }

            BuildTrafficSigns(rail);

            // A trail crossing now and then between the stops.
            if (Random.value < 0.12f)
            {
                var d = Random.Range(segStart + 10f, segEnd - 10f);
                var side = Random.value < 0.5f ? -1 : 1;
                if (!InCliffZone(d, side * (rail + 2.5f)))
                    PlaceRouteProp("SM_signpost_hike", d, side * (RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 2.5f),
                        FacingOncoming(d) * Quaternion.Euler(0f, side * 30f, 0f), "B500 Signpost");
            }

            // Polter: cut timber stacked at the forest edge for the lorry, end grain to
            // the road.
            if (Random.value < 0.25f)
            {
                var d = Random.Range(segStart + 15f, segEnd - 15f);
                var side = Random.value < 0.5f ? -1 : 1;
                var lateral = side * (rail + Random.Range(4f, 9f));
                if (!InCliffZone(d, lateral) && route.CoverAt(d, lateral) != RoadRoute.CoverWater &&
                    !InHotelGrounds(d, lateral))
                    PlaceRouteProp("SM_woodpile", d, lateral,
                        Quaternion.LookRotation(Flat(side * RoadPath.Right(d))) * Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f),
                        "B500 Woodpile", 0.1f);
            }
        }

        /// Bends tighter than this (degrees of heading per 10 m, ~120 m radius) get
        /// chevron boards and a warning before them.
        private const float SharpTurn = 4.8f;

        /// Fixed speed cameras, as distances along the route (one pass).
        internal static IEnumerable<float> BlitzerSites()
        {
            var route = RoadPath.Route;
            if (route == null) yield break;
            for (var u = 2200f; u < route.Length - 500f; u += 4200f) yield return u;
        }

        /// German road signs, as on the real B500: red and white chevrons round the
        /// outside of every sharp bend, a bend warning and a 70 limit before it, deer
        /// and no-overtaking signs, and the grey Blitzer boxes.
        private void BuildTrafficSigns(float rail)
        {
            for (var d = Mathf.Ceil(segStart / 15f) * 15f; d < segEnd; d += 15f)
            {
                var turn = TurnAt(d);
                if (Mathf.Abs(turn) < SharpTurn) continue;
                var outside = turn > 0f ? -1 : 1;
                PlaceRouteProp(turn > 0f ? "SM_sign_chevron_r" : "SM_sign_chevron_l", d,
                    outside * (RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 0.7f),
                    FacingOncoming(d), "B500 Chevron");
            }

            // Where a sharp bend starts, warn 90 m before it.
            for (var d = Mathf.Ceil((segStart + 90f) / 15f) * 15f; d < segEnd + 90f; d += 15f)
            {
                var turn = TurnAt(d);
                if (Mathf.Abs(turn) < SharpTurn || Mathf.Abs(TurnAt(d - 15f)) >= SharpTurn ||
                    Mathf.Abs(TurnAt(d - 30f)) >= SharpTurn) continue;
                var at = d - 90f;
                PlaceRouteProp(turn > 0f ? "SM_sign_curve" : "SM_sign_curve_l", at, RoadPath.HalfWidthAt(at) + RoadPath.ShoulderWidth + GuardRailOffset + 1.1f,
                    FacingOncoming(at), "B500 Sign Curve");
                PlaceRouteProp("SM_sign_limit_70", at + 12f, RoadPath.HalfWidthAt(at + 12f) + RoadPath.ShoulderWidth + GuardRailOffset + 1.1f,
                    FacingOncoming(at + 12f), "B500 Sign Limit");
            }

            foreach (var site in BlitzerSites())
            foreach (var d in PassesOf(site, site))
                PlaceRouteProp("SM_blitzer", d, RoadPath.HalfWidthAt(d) + RoadPath.ShoulderWidth + GuardRailOffset + 1.3f,
                    FacingOncoming(d), "B500 Blitzer");

            // Now and then: deer crossing, no overtaking, back to 100 on a straight.
            var roll = Random.value;
            var at2 = Random.Range(segStart + 10f, segEnd - 10f);
            var mesh = roll < 0.10f ? "SM_sign_deer"
                : roll < 0.16f ? "SM_sign_nopass"
                : roll < 0.24f && Mathf.Abs(TurnAt(at2)) < 1f ? "SM_sign_limit_100"
                : null;
            if (mesh != null)
                PlaceRouteProp(mesh, at2, RoadPath.HalfWidthAt(at2) + RoadPath.ShoulderWidth + GuardRailOffset + 1.1f,
                    FacingOncoming(at2), "B500 Sign");
        }

        /// Heading change over 20 m of road, per 10 m, in degrees: positive turns right.
        private static float TurnAt(float distance)
        {
            var a = Flat(RoadPath.Rotation(distance - 10f) * Vector3.forward);
            var b = Flat(RoadPath.Rotation(distance + 10f) * Vector3.forward);
            return Vector3.SignedAngle(a, b, Vector3.up) * 0.5f;
        }

        private GameObject PlaceRouteProp(string mesh, float distance, float lateral, Quaternion rotation, string name,
            float sink = 0f)
        {
            var model = BiomeModel("BlackForest", mesh, materials["B500 Props"]);
            if (model == null) return null;
            model.name = name;
            model.transform.SetPositionAndRotation(RoadPath.Point(distance, lateral, -sink), rotation);
            model.transform.localScale = Vector3.one;
            return model;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Sign faces are the mesh's +Z; the player always drives towards +Z, so this
        /// turns them to the traffic coming up the road.
        private static Quaternion FacingOncoming(float distance) =>
            Quaternion.LookRotation(Flat(-(RoadPath.Rotation(distance) * Vector3.forward)));

        /// Road distances in this chunk where the route is at `outbound` on the way
        /// there, or at `inbound` on the way back - so a sign put ahead of a place is
        /// ahead of it in both directions.
        private List<float> PassesOf(float outbound, float inbound)
        {
            var found = new List<float>(2);
            var length = RoadPath.Route.Length;
            var period = 2f * length;
            if (outbound >= 0f && outbound <= length)
            {
                var d = outbound + period * Mathf.Ceil((segStart - outbound) / period);
                if (d < segEnd) found.Add(d);
            }
            if (inbound >= 0f && inbound <= length)
            {
                var back = period - inbound;
                var d = back + period * Mathf.Ceil((segStart - back) / period);
                if (d < segEnd && (found.Count == 0 || Mathf.Abs(found[0] - d) > 1f)) found.Add(d);
            }
            return found;
        }

        /// The hotel at a stop goes on whichever side the map shows buildings, else
        /// open ground; a stop with forest both sides has none. The first and last
        /// entries are the route's ends, not stops.
        private static bool HotelSide(int placeIndex, out int side)
        {
            side = 0;
            var route = RoadPath.Route;
            if (route == null || !route.HasCover || placeIndex <= 0 || placeIndex >= route.Places.Length - 1) return false;
            var place = route.Places[placeIndex].Distance;
            var lateral = RoadPath.ClearanceAt(place) + HotelSetback;
            foreach (var want in new[] { RoadRoute.CoverBuilt, RoadRoute.CoverOpen })
            for (var s = 1; s >= -1; s -= 2)
            {
                if (route.CoverAt(place, s * lateral) != want) continue;
                side = s;
                return true;
            }
            return false;
        }

        /// Keeps trees and undergrowth off the hotel and its forecourt.
        private static bool InHotelGrounds(float distance, float lateral)
        {
            var route = RoadPath.Route;
            if (route == null || !route.HasCover) return false;
            var u = route.Fold(distance);
            for (var i = 1; i < route.Places.Length - 1; i++)
            {
                if (Mathf.Abs(u - route.Places[i].Distance) > 22f) continue;
                if (!HotelSide(i, out var side) || lateral * side <= 0f) return false;
                var across = Mathf.Abs(lateral) - RoadPath.ClearanceAt(distance);
                return across > 4f && across < HotelSetback + 16f;
            }
            return false;
        }

        /// One chunk's planting, reported the frame after (RR_PLANT).
        private sealed class PlantTally
        {
            public int OnRay, NoGround, Ambiguous;
            public float Shift;
        }

        /// Metres the drawn ground may differ from the terrain maths under a forest piece
        /// before the spot is taken as one where ground strips cross, and left bare.
        private const float MaxPlantingShift = 6f;

        private static PlantTally plantTally = new();
        internal static int canopyKept;
        internal static int canopyRejected;

        private GameObject ForestTree(float distance, float lateral, float minHeight, float maxHeight)
        {
            // Pine-dominant, not broadleaf-dominant. This was 62% broadleaf, which gives a
            // rounded English wood; an alpine pass is a wall of tall narrow conifers with
            // the odd broadleaf in it. Flipped to 30% broadleaf.
            // On the B500 it is the Black Forest: spruce and fir, from an installed tree
            // pack where there is one (ExternalVegetation). No snags: the bare grey
            // trunk read as fake.
            var table = RoadPath.Route != null
                ? BlackForestTrees
                : Random.value < 0.30f ? BroadleafTrees : PineTrees;
            var entry = table[Random.Range(0, table.Length)];
            // The pack's trees are film-quality meshes; past the near bands the fog hides
            // what they add, and at 61 M triangles a frame they cost the frame rate.
            // The far forest keeps the light Black Forest trees.
            if (RoadPath.Route != null && table == BlackForestTrees && ExternalTrees.Length > 0 &&
                Mathf.Abs(lateral) < ExternalTreeReach)
                // The northern Black Forest is spruce and fir, with beech mixed in on
                // the lower slopes: about one tree in eight where the pack has them.
                entry = ExternalBroadleaf.Length > 0 && Random.value < 0.12f
                    ? "External|b" + Random.Range(0, ExternalBroadleaf.Length)
                    : "External|" + Random.Range(0, ExternalTrees.Length);
            var tree = SpawnForestPiece(entry, distance, lateral, 0f, minHeight, maxHeight, "Forest Tree");
            if (tree == null) return null;
            // The tree's real offset, not its sign. Passed Mathf.Sign(lateral) - always
            // +-1 m, "inside the road" - this moved every tree, wherever it was planted,
            // onto the edge line: the whole forest stood in one row along the rail with
            // empty ground behind it.
            // Pushed clear of the road sideways, a tree keeps its height; on a bank that
            // climbs away from the road that buried it to the crown. It follows the
            // ground it was pushed onto instead.
            KeepTrunkOffRoad(tree, distance, lateral);
            // Roots into the ground: the pack's trees are grounded by their bounds, which
            // reach a little below the trunk, and stood hovering.
            // Less now the ground under it is taken at the trunk's downhill side.
            if (RoadPath.Route != null) tree.transform.position += Vector3.down * 0.3f;
            if (!KeepCanopyOffRoad(tree, distance, Mathf.Sign(lateral)))
            {
                Destroy(tree);
                canopyRejected++;
                return null;
            }
            if (OverRealTerrain && RaycastGround(tree.transform.position, 0.9f, out var treeGround))
                // The pack's bounds reach a little below the trunk: 0.3 m into the ground.
                StandOn(tree, treeGround - 0.3f);
            canopyKept++;
            return tree;
        }

        private GameObject ForestPlant(float distance, float lateral, float minHeight, float maxHeight, string label)
        {
            if (RoadPath.Route == null)
                return SpawnForestPiece(ForestPlants[Random.Range(0, ForestPlants.Length)], distance, lateral, 0.06f,
                    minHeight, maxHeight, label);
            // Fern, bilberry and moor grass are knee height; the kit plants these bands
            // were sized for stood twice that. Scaling is uniform, so a fern taken to 2 m
            // would also be 8 m across.
            return SpawnForestPiece(BlackForestPlants[Random.Range(0, BlackForestPlants.Length)], distance, lateral,
                0.02f, minHeight * 0.5f, maxHeight * 0.55f, label);
        }

        private static GameObject[] externalTrees;
        private static GameObject[] externalYoungTrees;
        private static GameObject[] externalBroadleaf;

        /// Tree prefabs from an installed pack, linked by Road Rage > Link Installed
        /// Tree Pack; empty when there is none.
        private static GameObject[] ExternalTrees
        {
            get
            {
                if (externalTrees != null) return externalTrees;
                var registry = Resources.Load<ExternalVegetation>("Biomes/ExternalVegetation");
                var linked = registry != null && registry.Trees != null
                    ? System.Array.FindAll(registry.Trees, t => t != null)
                    : System.Array.Empty<GameObject>();
                // A pack imported without its render-pipeline support package has
                // shaders this project cannot draw, and every tree renders magenta.
                // Those are left out, so Greenwood falls back to its own trees.
                externalTrees = System.Array.FindAll(linked, RendersInThisPipeline);
                externalYoungTrees = registry != null && registry.YoungTrees != null
                    ? System.Array.FindAll(registry.YoungTrees, t => t != null && RendersInThisPipeline(t))
                    : System.Array.Empty<GameObject>();
                externalBroadleaf = registry != null && registry.Broadleaf != null
                    ? System.Array.FindAll(registry.Broadleaf, t => t != null && RendersInThisPipeline(t))
                    : System.Array.Empty<GameObject>();
                if (externalTrees.Length > 0)
                    Debug.Log($"RR_TREES Greenwood plants {externalTrees.Length} trees and {externalBroadleaf.Length} " +
                              $"broadleaf from {registry.Source}");
                if (externalTrees.Length < linked.Length)
                    Debug.LogWarning($"RR_TREES {linked.Length - externalTrees.Length} linked trees use shaders URP cannot " +
                                     "draw (they would be magenta) and are skipped. Import the pack's URP support " +
                                     "package (its 'HD and URP support' folder), then Road Rage > Link Installed Tree Pack.");
                return externalTrees;
            }
        }

        /// A forest chunk holds about a thousand pieces and six chunks are built ahead,
        /// and none of the kit meshes has LODs: every fern 800 m away was drawn, and
        /// drawn again into each shadow cascade. Each piece now culls once it is small
        /// on screen (ground cover sooner than trees), and only trees near the road cast
        /// shadows - undergrowth shadows are lost in the trees' own.
        private static void ThinForestPiece(GameObject piece, float lateral, string label)
        {
            if (piece.GetComponentInChildren<LODGroup>() != null) return;
            SlowLodFades();
            var tree = label.StartsWith("Forest Tree") || label.StartsWith("Forest Understory");
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            if (!tree || Mathf.Abs(lateral) > 30f)
                foreach (var r in renderers) r.shadowCastingMode = ShadowCastingMode.Off;
            var group = piece.AddComponent<LODGroup>();
            // Culled later than before, and faded out rather than switched off: over
            // real terrain a hillside is in view much further, and plants popped in.
            // Culled at a little over half the screen size it was: a knee-high fern
            // went at ~85 m and a young spruce at ~250 m, well inside the view down the road, so
            // bushes and trees kept appearing out of nothing ahead of the car.
            group.SetLODs(new[] { new LOD(tree ? 0.015f : 0.013f, renderers) { fadeTransitionWidth = 0.3f } });
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.RecalculateBounds();
        }

        /// A second to dissolve in or out rather than the default half second, so what
        /// still changes in the distance does so gradually instead of blinking.
        private static void SlowLodFades()
        {
            if (Mathf.Approximately(LODGroup.crossFadeAnimationDuration, 1f)) return;
            LODGroup.crossFadeAnimationDuration = 1f;
        }

        private static readonly Dictionary<Material, Material> tamedPackMaterials = new();

        /// The pack's trees were the one thing in the forest not graded like the rest:
        /// every material the game makes is desaturated and matte, and these came in as
        /// shipped - glossy leaves with specular highlights, reflections and strong
        /// back-lit translucency. In the sun the canopy glared and looked over-lit.
        /// Each material is copied once, matte and a little calmer, and shared.
        private static void TamePackTree(GameObject tree)
        {
            foreach (var renderer in tree.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null) continue;
                    if (!tamedPackMaterials.TryGetValue(source, out var tamed))
                    {
                        tamed = TamedPackMaterial(source);
                        tamedPackMaterials[source] = tamed;
                        tamedPackMaterials[tamed] = tamed;
                    }
                    if (tamed == source) continue;
                    shared[i] = tamed;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = shared;
            }
        }

        private static Material TamedPackMaterial(Material source)
        {
            var material = new Material(source) { name = source.name + " (Road Rage)" };
            var shader = material.shader;
            for (var p = 0; p < shader.GetPropertyCount(); p++)
            {
                var property = shader.GetPropertyName(p);
                var key = property.ToLowerInvariant();
                var type = shader.GetPropertyType(p);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                {
                    if (key.Contains("channel") || key.Contains("toggle")) continue;
                    if (key.Contains("smooth") || key.Contains("gloss"))
                        material.SetFloat(property, Mathf.Min(material.GetFloat(property), 0.12f));
                    else if (key.Contains("metallic"))
                        material.SetFloat(property, 0f);
                    else if (key.Contains("translucen") || key.Contains("transmission") || key.Contains("scatter"))
                        material.SetFloat(property, material.GetFloat(property) * 0.5f);
                    else if (property == "_SpecularHighlights" || property == "_EnvironmentReflections")
                        material.SetFloat(property, 0f);
                }
                else if (type == ShaderPropertyType.Color)
                {
                    var colour = material.GetColor(property);
                    Color calmer;
                    if (key.Contains("emission")) calmer = Color.black;
                    else if (key.Contains("spec")) calmer = colour * 0.3f;
                    else if (key.Contains("color") || key.Contains("colour") || key.Contains("tint"))
                        calmer = Desaturate(colour, 0.35f) * 0.82f;
                    else continue;
                    calmer.a = colour.a;
                    material.SetColor(property, calmer);
                }
            }
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.DisableKeyword("_EMISSION");
            return material;
        }

        /// How far from the road the pack's trees are planted, metres.
        private const float ExternalTreeReach = 45f;
        private static bool externalTreeReported;

        /// Keeps a pack tree affordable: its lower LODs take over at twice the usual
        /// screen size and it is culled once it is a sliver; only trees by the road cast
        /// shadows, and only from their full-detail LOD. A pack tree without an LODGroup
        /// gets one that culls it when small.
        private static void ThinExternalTree(GameObject tree, float lateral)
        {
            TamePackTree(tree);
            SlowLodFades();
            var near = Mathf.Abs(lateral) < 18f;
            var group = tree.GetComponentInChildren<LODGroup>();
            if (group != null)
            {
                // The detail steps used to come at twice the pack's screen size, so a
                // tree visibly swapped to a coarser model, and dropped its shadow with
                // its full-detail LOD, a short way ahead of the car. Now a little over
                // the pack's own sizes, cross-faded, and every model but the flat far
                // one keeps its shadow by the road.
                var lods = group.GetLODs();
                var previous = 1f;
                for (var i = 0; i < lods.Length; i++)
                {
                    var last = i == lods.Length - 1;
                    var height = lods[i].screenRelativeTransitionHeight * 1.25f;
                    if (last) height = Mathf.Max(height, 0.012f);
                    // Transitions must keep falling from one LOD to the next.
                    previous = lods[i].screenRelativeTransitionHeight = Mathf.Min(height, previous * 0.9f);
                    lods[i].fadeTransitionWidth = 0.25f;
                    foreach (var r in lods[i].renderers)
                        if (r != null && (last && i > 0 || !near)) r.shadowCastingMode = ShadowCastingMode.Off;
                }
                group.SetLODs(lods);
                if (group.fadeMode == LODFadeMode.None)
                {
                    group.fadeMode = LODFadeMode.CrossFade;
                    group.animateCrossFading = true;
                }
            }
            else
            {
                var renderers = tree.GetComponentsInChildren<Renderer>();
                if (!near)
                    foreach (var r in renderers) r.shadowCastingMode = ShadowCastingMode.Off;
                group = tree.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.012f, renderers) { fadeTransitionWidth = 0.3f } });
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;
                group.RecalculateBounds();
            }

            if (externalTreeReported) return;
            externalTreeReported = true;
            var triangles = 0L;
            foreach (var f in tree.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null)
                    for (var m = 0; m < f.sharedMesh.subMeshCount; m++) triangles += f.sharedMesh.GetIndexCount(m) / 3;
            Debug.Log($"RR_TREES pack tree '{tree.name}': {triangles} triangles over all LODs, " +
                      $"{group.lodCount} LODs, planted within {ExternalTreeReach} m of the road");
        }

        private static bool RendersInThisPipeline(GameObject prefab)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || !material.shader.isSupported ||
                    material.shader.name == "Hidden/InternalErrorShader")
                    return false;
            }
            return true;
        }

        /// Broadleaf trees (beech, oak ...) from the pack, mixed into the conifers.
        private static GameObject[] ExternalBroadleaf
        {
            get
            {
                if (externalTrees == null) _ = ExternalTrees;
                return externalBroadleaf ?? System.Array.Empty<GameObject>();
            }
        }

        private static GameObject[] ExternalYoungTrees
        {
            get
            {
                if (externalTrees == null) _ = ExternalTrees;
                return externalYoungTrees ?? System.Array.Empty<GameObject>();
            }
        }

        /// Young trees under the canopy: the pack's small and medium firs where it is
        /// installed, the Blender young spruce otherwise.
        private GameObject Understory(float distance, float lateral)
        {
            var entry = ExternalYoungTrees.Length > 0
                ? "External|y" + Random.Range(0, ExternalYoungTrees.Length)
                : "BlackForest|SM_spruce_young";
            var tree = SpawnForestPiece(entry, distance, lateral, 0f, 4f, 10f, "Forest Understory");
            if (tree != null) tree.transform.position += Vector3.down * 0.3f;
            return tree;
        }

        /// Young spruce stand in for bushes under the Black Forest canopy.
        private string ForestBush() => RoadPath.Route != null
            ? "BlackForest|SM_spruce_young"
            : ForestBushes[Random.Range(0, ForestBushes.Length)];

        private Material BlackForestTint()
        {
            var roll = Random.value;
            return materials[roll < 0.5f ? "Black Forest" : roll < 0.8f ? "Black Forest Dark" : "Black Forest Fresh"];
        }

        // ------------------------------------------------------------ test assets

        /// Models being tried out for Greenwood (Rodin, TRELLIS...), dropped into
        /// Assets/Resources/TestAssets/ - git-ignored, never committed. They stand beside
        /// the road from TestAssetStart past the start of the run, one every
        /// TestAssetSpacing metres, alternating sides and facing the road, on cleared
        /// ground. Each is scaled to TestAssetDefaultHeight, or to the height in its name:
        /// "farmhouse_h12" stands 12 m tall. The console lists what each one costs
        /// (RR_TESTASSET), in the order they stand.
        private const float TestAssetStart = 120f;
        private const float TestAssetSpacing = 40f;
        private const float TestAssetDefaultHeight = 9f;
        /// Half the widest a test model may be across, so it stays inside its clearing.
        private const float TestAssetMaxHalf = 12f;
        private static GameObject[] testAssets;

        private static GameObject[] TestAssets
        {
            get
            {
                if (testAssets != null) return testAssets;
                testAssets = Resources.LoadAll<GameObject>("TestAssets");
                System.Array.Sort(testAssets, (a, b) => string.CompareOrdinal(a.name, b.name));
                for (var i = 0; i < testAssets.Length; i++) ReportTestAsset(i, testAssets[i]);
                return testAssets;
            }
        }

        private float TestAssetDistance(int index) => startDistance + TestAssetStart + index * TestAssetSpacing;

        /// Out from the clearance line by the model's own size: a car just past the rail,
        /// where it is seen, a house well back.
        ///
        /// And its whole levelled plot (the model's half plus 4 m) beyond the first 6 m
        /// of bank, where RealGround eases the real ground back down to the road: a plot
        /// that reached into it was only level on its outer half, and a car on the left
        /// bank stood with its corners up to 4 m apart, tilted as far as it may go.
        private static float TestAssetLateral(int index, float distance)
        {
            var half = TestAssetHalf(TestAssets[index]);
            var clearOfEase = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + 6f + half + 4f;
            return (index % 2 == 0 ? -1f : 1f) *
                   Mathf.Max(RoadPath.ClearanceAt(distance) + 3f + half, clearOfEase);
        }

        private bool InTestAssetGround(float distance, float lateral)
        {
            if (RoadPath.Route == null) return false;
            var assets = TestAssets;
            for (var i = 0; i < assets.Length; i++)
            {
                var d = TestAssetDistance(i);
                var clearing = TestAssetHalf(assets[i]) + 4f;
                if (Mathf.Abs(distance - d) < clearing &&
                    Mathf.Abs(lateral - TestAssetLateral(i, d)) < clearing)
                    return true;
            }
            return false;
        }

        /// Clears the models loaded by an earlier run in the editor: with domain reload
        /// off, the list survived Play and the cost report never printed again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTestAssets()
        {
            testAssets = null;
            twoSided.Clear();
            innerReachCache.Clear();
            buildingPads.Clear();
            padsFor = float.NaN;
        }

        private static float padsFor = float.NaN;

        /// The level ground each test model will stand on, known before any ground is
        /// built so the terrain mesh and everything planted on it agree.
        private void PrepareBuildingPads()
        {
            if (RoadPath.Route == null || !RoadPath.Route.HasTerrain)
            {
                buildingPads.Clear();
                return;
            }
            if (padsFor == startDistance) return;
            padsFor = startDistance;
            buildingPads.Clear();
            var assets = TestAssets;
            for (var i = 0; i < assets.Length; i++)
            {
                var distance = TestAssetDistance(i);
                var lateral = TestAssetLateral(i, distance);
                var half = TestAssetHalf(assets[i]);
                // The mean of the real ground over the plot: as much cut as fill.
                var sum = 0f;
                var count = 0;
                const int steps = 4;
                for (var a = -steps; a <= steps; a++)
                for (var b = -steps; b <= steps; b++)
                {
                    sum += RealGround(distance + half * a / steps, lateral + half * b / steps);
                    count++;
                }
                buildingPads.Add(new BuildingPad
                {
                    Distance = distance, Lateral = lateral, Half = half + 4f, Height = sum / count,
                });
            }
        }

        /// Triangles the most detailed model of a test building may have.
        private const long TestAssetTriangleBudget = 60000;

        private static long Triangles(Renderer renderer)
        {
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return 0;
            var count = 0L;
            for (var m = 0; m < filter.sharedMesh.subMeshCount; m++) count += filter.sharedMesh.GetIndexCount(m) / 3;
            return count;
        }

        /// Rodin's LOD export starts at whatever the slider was on - 2 M triangles for the
        /// first house, a third of a frame's budget in one building. The detail levels
        /// heavier than the budget are dropped and the first affordable one leads.
        private static void KeepAffordableLods(GameObject model, string name)
        {
            var group = model.GetComponentInChildren<LODGroup>();
            if (group == null) return;
            var lods = group.GetLODs();
            if (lods.Length < 2) return;
            var counts = new long[lods.Length];
            var first = lods.Length - 1;
            for (var i = lods.Length - 1; i >= 0; i--)
            {
                foreach (var r in lods[i].renderers) counts[i] += Triangles(r);
                if (counts[i] <= TestAssetTriangleBudget) first = i;
            }
            Debug.Log($"RR_TESTASSET '{name}' LOD triangles: {string.Join(", ", counts)} -> leading with LOD{first}");
            if (first == 0) return;
            var kept = new LOD[lods.Length - first];
            for (var i = 0; i < kept.Length; i++)
            {
                kept[i] = lods[first + i];
                kept[i].screenRelativeTransitionHeight = lods[i].screenRelativeTransitionHeight;
            }
            // The last one kept took the first one's threshold - culled while it still
            // filled a good part of the screen, so the house popped in close up. It goes
            // only once it is a sliver, like everything else by the road.
            kept[kept.Length - 1].screenRelativeTransitionHeight = 0.01f;
            for (var i = 0; i < first; i++)
                foreach (var r in lods[i].renderers)
                {
                    if (r == null) continue;
                    r.enabled = false;
                    if (r.gameObject != model && r.transform.childCount == 0) Destroy(r.gameObject);
                }
            group.SetLODs(kept);
            group.RecalculateBounds();
        }

        private static readonly Dictionary<Material, Material> twoSided = new();

        /// Generated buildings are a thin one-sided shell: through a doorway, an open
        /// arcade or a gap in the mesh the inside faces were not drawn, and you saw
        /// straight through to the ground behind. Drawn from both sides, a gap shows the
        /// inside of the walls instead. (A dark block inside the shell was tried first
        /// and came out covering the whole building.)
        private static void DrawBothSides(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                for (var m = 0; m < shared.Length; m++)
                {
                    var source = shared[m];
                    if (source == null || !source.HasProperty("_Cull")) continue;
                    if (!twoSided.TryGetValue(source, out var both))
                    {
                        both = new Material(source) { name = source.name + " (both sides)" };
                        both.SetFloat("_Cull", 0f);
                        both.doubleSidedGI = true;
                        twoSided[source] = both;
                    }
                    shared[m] = both;
                }
                renderer.sharedMaterials = shared;
            }
        }

        /// Half the model's widest side once it stands at its height, within the clearing.
        private static float TestAssetHalf(GameObject asset)
        {
            var size = NativeSize(asset);
            if (size.y < 0.001f) return TestAssetMaxHalf;
            var scale = TestAssetHeight(asset.name) / size.y;
            return Mathf.Min(TestAssetMaxHalf, 0.5f * Mathf.Max(size.x, size.z) * scale);
        }

        /// From the meshes: a prefab that is not in the scene has no renderer bounds.
        private static Vector3 NativeSize(GameObject asset)
        {
            var size = Vector3.zero;
            foreach (var filter in asset.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    size = Vector3.Max(size, Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale));
            return size;
        }

        private void PlaceTestAssets()
        {
            if (RoadPath.Route == null) return;
            var assets = TestAssets;
            for (var i = 0; i < assets.Length; i++)
            {
                var distance = TestAssetDistance(i);
                if (distance < segStart || distance >= segEnd) continue;
                var lateral = TestAssetLateral(i, distance);
                var model = Adopt(Instantiate(assets[i]));
                model.name = $"Test Asset {i + 1} {assets[i].name}";
                KeepAffordableLods(model, assets[i].name);
                model.transform.position = Vector3.zero;
                // Its front (+Z) towards the road, turned towards the traffic coming up to
                // it: square to the road, a driver only ever saw the side. A model that
                // comes out facing another way names its turn: "_r90", "_r180", "_r270".
                var toRoad = -Mathf.Sign(lateral) * RoadPath.Right(distance);
                var toTraffic = Vector3.Slerp(toRoad, -RoadPath.Forward(distance), TestAssetTurn / 90f);
                model.transform.rotation = Quaternion.LookRotation(toTraffic) *
                                           Quaternion.Euler(0f, TestAssetRotation(assets[i].name), 0f);
                if (!TryGetCombinedBounds(model, out var bounds) || bounds.size.y < 0.001f) continue;
                model.transform.localScale *= TestAssetHeight(assets[i].name) / bounds.size.y;
                TryGetCombinedBounds(model, out bounds);
                var half = Mathf.Max(bounds.extents.x, bounds.extents.z);
                if (half > TestAssetMaxHalf)
                {
                    model.transform.localScale *= TestAssetMaxHalf / half;
                    half = TestAssetMaxHalf;
                }
                // On its levelled plot (PrepareBuildingPads), which RealGround now returns.
                var ground = RoadPath.Point(distance, lateral, MeshGround(distance, lateral));
                model.transform.position = ground;
                TryGetCombinedBounds(model, out bounds);
                // Centred on its spot and bedded into the ground by its size, whatever its
                // pivot: a floorless building shell 0.8 m, so its empty underside does not
                // show at the edges; a car a few centimetres, or it sat in the slope.
                var bed = Mathf.Clamp(bounds.size.y * 0.05f, 0.05f, 0.8f);
                model.transform.position += new Vector3(ground.x - bounds.center.x, ground.y - bounds.min.y - bed,
                    ground.z - bounds.center.z);
                var settled = SettleOnDrawnGround(model, ground, bed,
                    assets[i].name.StartsWith("car", System.StringComparison.OrdinalIgnoreCase));
                TryGetCombinedBounds(model, out bounds);
                DrawBothSides(model);
                Debug.Log($"RR_TESTASSET placed #{i + 1} '{assets[i].name}' {distance - startDistance:0} m from the start, " +
                          $"{(lateral < 0f ? "left" : "right")} {Mathf.Abs(lateral):0} m, " +
                          $"{bounds.size.x:0.#} x {bounds.size.y:0.#} x {bounds.size.z:0.#} m; {settled}");
            }
        }

        /// Steepest a car is tilted to lie on the ground under it.
        private const float TestAssetMaxTilt = 20f;

        /// The test model stood on the ground as it is actually drawn, found by rays at
        /// the corners of its footprint. The maths of the levelled plot (MeshGround) and
        /// the drawn ground part company where strips from further along the road cover
        /// the spot - inside a bend above all - and a car set by the maths sank into the
        /// bank on its uphill side. A car lies on the ground: tilted to the plane through
        /// its four corners, as its wheels would take it. A building stays level, its
        /// underside at the lowest corner, so no edge hangs in the air and the uphill
        /// side is cut into the slope.
        private static string SettleOnDrawnGround(GameObject model, Vector3 ground, float bed, bool isCar)
        {
            if (!TryGetCombinedBounds(model, out var bounds)) return "no bounds";
            var yaw = Quaternion.Euler(0f, model.transform.eulerAngles.y, 0f);
            // The footprint in the model's own frame, a little inside its outline: the
            // wheels, the corners of the walls.
            var local = Quaternion.Inverse(yaw);
            var halfX = 0f;
            var halfZ = 0f;
            for (var c = 0; c < 8; c++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                var inModel = local * (corner - bounds.center);
                halfX = Mathf.Max(halfX, Mathf.Abs(inModel.x));
                halfZ = Mathf.Max(halfZ, Mathf.Abs(inModel.z));
            }
            halfX *= 0.8f;
            halfZ *= 0.8f;
            var bottom = new Vector3(bounds.center.x, bounds.min.y + bed, bounds.center.z);
            var points = new Vector3[4];
            var low = float.PositiveInfinity;
            var high = float.NegativeInfinity;
            var missing = 0;
            for (var c = 0; c < 4; c++)
            {
                var at = bottom + yaw * new Vector3((c & 1) == 0 ? -halfX : halfX, 0f, (c & 2) == 0 ? -halfZ : halfZ);
                if (!Physics.Raycast(new Vector3(at.x, at.y + 400f, at.z), Vector3.down, out var hit, 1200f,
                        1 << PlantingGroundLayer, QueryTriggerInteraction.Ignore))
                {
                    missing++;
                    // A car needs all four for its tilt; a building stands on the rest.
                    if (isCar) return $"ground by maths (no drawn ground under corner {c})";
                    continue;
                }
                points[c] = hit.point;
                low = Mathf.Min(low, hit.point.y);
                high = Mathf.Max(high, hit.point.y);
            }
            if (missing == 4) return "ground by maths (no drawn ground under any corner)";
            var mean = (points[0].y + points[1].y + points[2].y + points[3].y) * 0.25f;
            var report = $"drawn ground {(isCar ? mean : low) - ground.y:+0.00;-0.00} m from the maths, " +
                         $"corners {high - low:0.00} m apart" + (missing > 0 ? $", {missing} corner(s) with no drawn ground" : "");
            if (isCar)
            {
                // Corners 0..3: back-left, back-right, front-left, front-right.
                var normal = Vector3.Cross(points[3] - points[0], points[1] - points[2]).normalized;
                if (normal.y < 0f) normal = -normal;
                var tilt = Vector3.Angle(Vector3.up, normal);
                if (tilt > TestAssetMaxTilt)
                    normal = Vector3.Slerp(Vector3.up, normal, TestAssetMaxTilt / tilt);
                var angle = Vector3.Angle(Vector3.up, normal);
                if (angle > 0.05f)
                    model.transform.RotateAround(bottom, Vector3.Cross(Vector3.up, normal).normalized, angle);
                model.transform.position += Vector3.up * (mean - bottom.y);
                return report + $", tilted {Mathf.Min(tilt, TestAssetMaxTilt):0.#} deg";
            }
            model.transform.position += Vector3.up * (low - bottom.y);
            return report + ", level on the lowest corner";
        }

        /// Degrees a test model turns from facing the road towards oncoming traffic.
        private const float TestAssetTurn = 30f;

        private static float TestAssetRotation(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"_r(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees)
                ? degrees
                : 0f;
        }

        private static float TestAssetHeight(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"_h(\d+(\.\d+)?)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var height) && height > 0.1f
                ? height
                : TestAssetDefaultHeight;
        }

        /// Triangles, materials and texture sizes: what decides whether a model can
        /// stand in the game by the dozen.
        private static void ReportTestAsset(int index, GameObject asset)
        {
            var triangles = 0L;
            var vertices = 0L;
            var size = NativeSize(asset);
            // With detail levels, what the full-detail one costs: summing every level
            // counts the same house three or four times.
            var group = asset.GetComponentInChildren<LODGroup>(true);
            var lods = group != null ? group.GetLODs() : null;
            var fullDetail = lods != null && lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers) : null;
            foreach (var filter in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (fullDetail != null && !fullDetail.Contains(filter.GetComponent<Renderer>())) continue;
                vertices += mesh.vertexCount;
                for (var m = 0; m < mesh.subMeshCount; m++) triangles += mesh.GetIndexCount(m) / 3;
            }
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (var renderer in asset.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material)) continue;
                foreach (var property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture != null) textures.Add(texture);
                }
            }
            var largest = 0;
            foreach (var texture in textures) largest = Mathf.Max(largest, Mathf.Max(texture.width, texture.height));
            var verdict = triangles <= 30000 ? "OK" : triangles <= 100000 ? "HEAVY - reduce before use" : "TOO HEAVY - reduce";
            var levels = lods != null ? $" (full detail of {lods.Length} LODs)" : "";
            Debug.Log($"RR_TESTASSET #{index + 1} '{asset.name}': {triangles:N0} triangles{levels}, {vertices:N0} vertices, " +
                      $"{materials.Count} materials, {textures.Count} textures up to {largest}px, native size " +
                      $"{size.x:0.##} x {size.y:0.##} x {size.z:0.##}, stands {TestAssetHeight(asset.name):0.#} m -> {verdict}");
        }

        private static readonly string[] BlackForestTrees =
        {
            "BlackForest|SM_spruce_01", "BlackForest|SM_spruce_02", "BlackForest|SM_spruce_03",
            "BlackForest|SM_spruce_04", "BlackForest|SM_spruce_01", "BlackForest|SM_spruce_03",
            "BlackForest|SM_fir_01", "BlackForest|SM_fir_02",
        };
        private static readonly string[] BlackForestPlants =
        {
            "BlackForest|SM_fern", "BlackForest|SM_fern", "BlackForest|SM_bilberry",
            "BlackForest|SM_bilberry", "BlackForest|SM_moor_grass",
        };

        private void BuildHollywoodPhotorealPass()
        {
            BuildHollywoodEstateSprawl();
            ApplyHillsPhotorealMood();
            ApplyHillsSurfaceMaterialOverrides();
            ApplyHillsPhotorealSignature();
            ApplyHillsDepthPass();
            ApplyHillsRoadToneProfile();
        }

        private void ApplyHillsSurfaceMaterialOverrides()
        {
            if (materials.TryGetValue("Road", out var road))
            {
                if (road.HasProperty("_BaseColor")) road.SetColor("_BaseColor", new Color(0.38f, 0.40f, 0.43f));
                if (road.HasProperty("_Smoothness")) road.SetFloat("_Smoothness", 0.35f);
                if (road.HasProperty("_NormalScale")) road.SetFloat("_NormalScale", 0.95f);
                if (road.HasProperty("_OcclusionStrength")) road.SetFloat("_OcclusionStrength", 0.85f);
            }

            if (materials.TryGetValue("Hills Concrete", out var concrete))
            {
                if (concrete.HasProperty("_NormalScale")) concrete.SetFloat("_NormalScale", 0.85f);
                if (concrete.HasProperty("_OcclusionStrength")) concrete.SetFloat("_OcclusionStrength", 0.75f);
            }

            if (materials.TryGetValue("Hills Pool", out var pool))
            {
                if (pool.HasProperty("_BaseColor")) pool.SetColor("_BaseColor", new Color(0.28f, 0.88f, 0.96f));
                if (pool.HasProperty("_Smoothness")) pool.SetFloat("_Smoothness", 0.98f);
                if (pool.HasProperty("_Metallic")) pool.SetFloat("_Metallic", 0.15f);
            }

            if (materials.TryGetValue("Hills Window Glass", out var glass))
            {
                if (glass.HasProperty("_BaseColor")) glass.SetColor("_BaseColor", new Color(0.08f, 0.11f, 0.15f));
                if (glass.HasProperty("_Metallic")) glass.SetFloat("_Metallic", 0.88f);
                if (glass.HasProperty("_Smoothness")) glass.SetFloat("_Smoothness", 0.95f);
            }

            if (materials.TryGetValue("Hills Leaves", out var leaves))
            {
                if (leaves.HasProperty("_BaseColor")) leaves.SetColor("_BaseColor", new Color(0.38f, 0.52f, 0.28f));
            }
        }

        private void ApplyHillsPhotorealMood()
        {
            if (sunLight != null)
            {
                sunLight.color = new Color(1f, 1f, 1f);
                sunLight.intensity = 1.45f;
                sunLight.shadows = LightShadows.Soft;
                sunLight.shadowStrength = 0.75f;
                sunLight.transform.rotation = Quaternion.Euler(58f, 35f, 0f);
            }
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.64f);
        }

        private void ApplyHillsPhotorealSignature()
        {
            TuneReflectionProbe(1.25f, new Vector3(68f, 24f, 68f));
        }

        private void ApplyHillsDepthPass()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0012f;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.94f);
        }

        private void ApplyHillsRoadToneProfile()
        {
            if (materials.TryGetValue("Car Orange", out var paint))
            {
                if (paint.HasProperty("_Metallic")) paint.SetFloat("_Metallic", 0.65f);
                if (paint.HasProperty("_Smoothness")) paint.SetFloat("_Smoothness", 0.88f);
            }

            if (materials.TryGetValue("Hills Ground", out var ground))
            {
                if (ground.HasProperty("_NormalScale")) ground.SetFloat("_NormalScale", 1.1f);
                if (ground.HasProperty("_OcclusionStrength")) ground.SetFloat("_OcclusionStrength", 0.8f);
            }
        }

        private void BuildHollywoodEstateSprawl()
        {
            Random.InitState(55217 ^ chunkSeed);
            const string pack = "HollywoodHills";
            var uphill = ((int)(SegBegin(0f, 900f) / 900f) % 2 == 0) ? -1f : 1f;

            var houses = new[]
            {
                "Houses/SM_house_01", "Houses/SM_house_02", "Houses/SM_house_03",
                "Houses/SM_house_04", "Houses/SM_house_05", "Houses/SM_garage"
            };
            var backdrop = new[]
            {
                "Background_buidings/SM_background_building_01", "Background_buidings/SM_background_building_02",
                "Background_buidings/SM_background_building_03", "Background_buidings/SM_background_building_04",
                "Background_buidings/SM_background_building_05", "Background_buidings/SM_background_building_06",
                "Background_buidings/SM_background_building_07"
            };
            var scrub = new[]
            {
                "Plants/SM_Plant_01", "Plants/SM_Plant_03", "Plants/SM_Plant_04",
                "Plants/SM_Plant_05", "Plants/SM_grass_01", "Vegetation/SM_bush_05",
                "Vegetation/SM_plant_02"
            };
            var rocks = new[]
            {
                "Cliff/SM_rock_01", "Cliff/SM_rock_02", "Cliff/SM_rock_03",
                "Cliff/SM_rock_04", "Cliff/SM_rock_05", "Cliff/SM_rock_06", "Cliff/SM_rock_07"
            };

            GameObject Piece(string mesh, Material mat, float d, float l, float h,
                float minH, float maxH, string label, bool clear = true)
            {
                var model = PlaceBiomeModelOnRoad(pack, mesh, mat, d, l, h,
                    new Vector3(-90f, l > 0f ? -90f : 90f, 0f), Vector3.one, label, clear);
                if (model == null) return null;
                NormalizeModelHeight(model, Random.Range(minH, maxH), 0.05f);
                return model;
            }

            // ---- Layer 1: Roadside Flora, Groundcover & Hillside Shrubs (Outside Curb) ----
            ScatterBand(3.5f, 11.2f, 13.8f, (d, l, s) =>
                Piece(scrub[Random.Range(0, scrub.Length)], materials["Hills Groundcover"],
                    d, l, 0f, 0.5f, 1.1f, "Verge Flora"));
            ScatterBand(5.0f, 14.0f, 22.0f, (d, l, s) =>
                Piece(scrub[Random.Range(0, scrub.Length)], materials["Hills Scrub"],
                    d, l, 0f, 0.9f, 1.8f, "Hillside Scrub"));

            // ---- Layer 2: Towering California Fan Palms (Clearance at 13.0m - 15.5m) ----
            ScatterBand(14f, 13.0f, 15.5f, (d, l, s) =>
            {
                var palmIdx = Random.Range(1, 7);
                var palm = PlaceBiomeModelOnRoad("RedCanyon", $"Tree/SM_palm_tree_0{palmIdx}", materials["Hills Leaves"],
                    d, l, 0f, new Vector3(-90f, Random.Range(0f, 360f), 0f), Vector3.one, "California Fan Palm");
                if (palm != null) NormalizeModelHeight(palm, Random.Range(11f, 16f), 0.05f);
                return palm;
            });

            // ---- Layer 3: Beverly Hills Street Lamps & Power Infrastructure (11.8m - 12.6m) ----
            ScatterBand(24f, 11.8f, 12.4f, (d, l, s) =>
            {
                var lamp = Piece("Lamp/SM_lamp", materials["Hills Metal"], d, l, 0f, 5.5f, 6.5f, "Street Lamp", false);
                if (lamp != null) CreateLocalLight(RoadPath.Point(d, l, 4.8f), new Color(1f, 0.92f, 0.75f), 2.2f, 9f);
                return lamp;
            }, 0.05f);
            ScatterBand(30f, 12.6f, 13.2f, (d, l, s) => s * uphill < 0 ? null :
                Piece("Electric_pole/SM_electric_pole_alone", materials["Hills Pole"],
                    d, l, 0f, 9.5f, 12.5f, "Power Pole", false));

            // ---- Layer 4: Uphill Estates & Modern Mansions (Every 16m block) ----
            for (var z = SegBegin(0f, 16f); z < segEnd; z += 16f)
            {
                var block = Mathf.FloorToInt(z / 16f);
                var side = uphill;
                var facing = side > 0f ? -90f : 90f;

                // Perimeter Stucco Walls & Gates along property border
                var wallDist = z + (side > 0 ? 2f : -2f);
                var wall = Piece(block % 3 == 0 ? "Wall/SM_wall_02" : "Wall/SM_wall_01", materials["Hills Concrete"],
                    wallDist, side * 12.8f, 0f, 1.1f, 1.4f, "Estate Wall", false);
                if (block % 4 == 0)
                {
                    Piece("Gate/SM_house_gate", materials["Hills Gate"], z + 7f, side * 12.6f, 0f, 2.0f, 2.5f, "Estate Gate", false);
                }

                // Modern Mansions (Staggered Front & Back Rows)
                var houseMesh = houses[BlockHash(block, (int)side) % houses.Length];
                var houseDist = z + 1f;
                var houseLat = side * Random.Range(18.5f, 25.5f);
                var houseMat = block % 2 == 0 ? materials["Hills Concrete"] : materials["Hills Concrete Dark"];
                var house = PlaceBiomeModelOnRoad(pack, houseMesh, houseMat, houseDist, houseLat, 0f,
                    new Vector3(-90f, facing, 0f), Vector3.one, "Hollywood Mansion");
                if (house != null)
                {
                    NormalizeModelHeight(house, Random.Range(7.5f, 12.5f), 0.05f);
                    EnsureOutsideRoad(house, houseDist, side);
                }

                // Upper Hillside Villas
                if (block % 2 == 0)
                {
                    var upperMesh = houses[BlockHash(block, 7) % houses.Length];
                    var upperDist = z + 8f;
                    var upperLat = side * Random.Range(32f, 44f);
                    var upper = PlaceBiomeModelOnRoad(pack, upperMesh, houseMat, upperDist, upperLat, 0f,
                        new Vector3(-90f, facing, 0f), Vector3.one, "Upper Hillside Villa");
                    if (upper != null) NormalizeModelHeight(upper, Random.Range(8f, 13f), 0.05f);
                }

                // Infinity Pools & Patio Terraces
                if (block % 3 == 1)
                {
                    var poolDist = z + 5f;
                    var poolLat = side * Random.Range(21f, 30f);
                    var pool = Piece("Pool/SM_pool", materials["Hills Pool"], poolDist, poolLat, 0f, 0.65f, 0.95f, "Hillside Pool");
                    Piece("Pool_props/SM_sunbath_01", materials["Hills Wood"], poolDist - 2.5f, poolLat + 3f, 0f, 0.6f, 0.8f, "Sunbed");
                    Piece("Pool_props/SM_umbrella", materials["Hills Metal"], poolDist - 2.5f, poolLat - 3f, 0f, 2.2f, 2.6f, "Pool Umbrella");
                }

                // Garden Trees & Shade Palms around property
                var treeDist = z + 11f;
                var treeLat = side * Random.Range(16f, 30f);
                Piece("tree/SM_tree", materials["Hills Bark"], treeDist, treeLat, 0f, 7.5f, 13f, "Garden Tree");
            }

            // ---- Layer 5: Downhill Villas, Terraces & Natural Sandstone Outcrops ----
            for (var z = SegBegin(0f, 18f); z < segEnd; z += 18f)
            {
                var block = Mathf.FloorToInt(z / 18f);
                var side = -uphill;
                var facing = side > 0f ? -90f : 90f;

                // Step-Down Valley Villas
                var villaDist = z + 2f;
                var villaLat = side * Random.Range(19.5f, 28f);
                var villaMesh = houses[BlockHash(block, (int)side * 3) % houses.Length];
                var villa = PlaceBiomeModelOnRoad(pack, villaMesh, materials["Hills Concrete"], villaDist, villaLat, 0f,
                    new Vector3(-90f, facing, 0f), Vector3.one, "Valley Villa");
                if (villa != null)
                {
                    NormalizeModelHeight(villa, Random.Range(7.5f, 11.5f), 0.05f);
                    EnsureOutsideRoad(villa, villaDist, side);
                }

                // Valley Garden Shade Trees
                Piece("tree/SM_tree", materials["Hills Bark"], z + 14f, side * Random.Range(17f, 34f), 0f, 7f, 12f, "Valley Tree");
            }

            // ---- Layer 6: Distant Los Angeles Downtown Basin Skyline (Valley Floor View) ----
            ScatterBand(45f, 220f, 420f, (d, l, s) => s * uphill > 0 ? null :
                Piece(backdrop[Random.Range(0, backdrop.Length)], materials["Hills Windows"],
                    d, l, -15f, 45f, 90f, "LA Basin Skyline", false), 0.8f);

            // Roadside clutter (Trash bins, crates, mailboxes)
            ScatterBand(28f, 10.2f, 12.5f, (d, l, s) =>
                Piece(Random.value > 0.5f ? "Trash_bin/SM_trash_bin" : "Crates/SM_crates_group_01",
                    materials["Hills Metal"], d, l, 0f, 0.9f, 1.4f, "Roadside Clutter"));
        }

        private GameObject globalHorizonSky;

        private void EnsureGlobalHorizonSky(int biomeIndex)
        {
            // Owns its own teardown. Destroy() is deferred to the end of the frame, so a
            // null guard after it returns early and the horizon is never rebuilt - which
            // is exactly how the ridge vanished on the first biome reload. DestroyImmediate
            // plus a cleared reference makes this method safe to call on every world build,
            // and the biome that is active now is always the biome the ridge belongs to.
            if (globalHorizonSky != null)
            {
                DestroyImmediate(globalHorizonSky);
                globalHorizonSky = null;
            }

            globalHorizonSky = new GameObject("Global Horizon Sky & Mountains");
            globalHorizonSky.AddComponent<GlobalHorizonFollower>();
            ApplyBiomeSky(biomeIndex);

            if (biomeIndex == 0 && GreenwoodHorizonMountains) // Greenwood
            {
                // A closed ring of real terrain, 420 m to 1 km out: the northern Black Forest
                // seen from the Acher valley below the Mummelsee and the Hornisgrinde
                // (Tools/Terrain/build_black_forest_ring.py). Every ridge stands at its real
                // angle above the horizon, brought closer to fit inside the fog; the valley
                // runs along the road. It has its own lighter aerial perspective
                // (RoadRage/Backdrop) so it reads through the road fog. The ranks of
                // SM_mountain below are only the fallback.
                if (!BuildMountainRing())
                {
                    // A ridge closing off the forest horizon. ForestVillage ships SM_mountain
                    // and the biome already builds a Forest Mountain material for it, and
                    // neither was ever placed - Greenwood ended at a wall of trees with sky
                    // above it, which is what makes a forest read as a corridor rather than a
                    // valley.
                    //
                    // Both flanks and a back rank, none of it across the road. Parented to the
                    // horizon follower, so the ridge holds its distance instead of sliding past
                    // - a mountain you drive level with is a rock.
                    // A range, not eight lumps.
                    //
                    // Eight isolated peaks at one distance read as scenery objects placed
                    // near a road. A mountain range reads as a range because ridgelines
                    // overlap: a near rank whose gaps are filled by a middle rank, and a far
                    // rank behind both that is mostly haze. Three depths, twenty-six peaks,
                    // and every one rotated differently so the same mesh does not repeat a
                    // recognisable profile along the skyline.
                    //
                    // The near rank is deliberately the shortest. Height falls with distance
                    // in a real range only because of perspective, and these are all drawn at
                    // a fixed offset from the camera - so making the far rank the tallest is
                    // what puts the big peaks behind the little ones instead of in front.
                    var ranks = new[]
                    {
                        // depth, count, height, spread
                        new Vector4(300f, 10f, 130f, 520f),
                        new Vector4(440f, 9f, 210f, 700f),
                        new Vector4(620f, 7f, 310f, 900f),
                    };
                    var peakIndex = 0;
                    for (var r = 0; r < ranks.Length; r++)
                    {
                        var depth = ranks[r].x;
                        var count = Mathf.RoundToInt(ranks[r].y);
                        for (var i = 0; i < count; i++)
                        {
                            var peak = BiomeModel("ForestVillage", "Mountains/SM_mountain",
                                materials["Forest Mountain"]);
                            if (peak == null) continue;
                            // Odd ranks are offset by half a step so the rank behind fills the
                            // gaps in the rank in front rather than hiding directly behind it.
                            var across = (i - (count - 1) * 0.5f + (r % 2 == 0 ? 0f : 0.5f))
                                         / Mathf.Max(1f, count - 1f) * ranks[r].w * 2f;
                            var along = depth + Mathf.Sin(i * 2.3f + r) * depth * 0.18f;
                            peak.name = $"Forest Horizon Mountain {peakIndex}";
                            peak.transform.SetParent(globalHorizonSky.transform, false);
                            peak.transform.localPosition = new Vector3(across, -30f, along);
                            peak.transform.localRotation = Quaternion.Euler(0f, peakIndex * 53f % 360f, 0f);
                            NormalizeModelHeight(peak, ranks[r].z * (0.82f + i % 4 * 0.12f), 0f);
                            peakIndex++;
                        }
                    }

                }

                // No mesh clouds: Greenwood's sky is a baked panorama (ApplyBiomeSky),
                // and sphere-cluster cumulus in front of it read as props hung in the air.
            }
            else if (biomeIndex == 9) // Hollywood Hills
            {
                // 1. Static Panoramic Mountains strictly on Left (-X) and Right (+X) flanks (NEVER across the road!)
                var mountainOffsets = new[]
                {
                    // Left Ridge Flank (X: -450m to -550m)
                    new Vector3(-480f, -25f, -320f),
                    new Vector3(-520f, -25f, 0f),
                    new Vector3(-480f, -25f, 320f),
                    // Right Ridge Flank (X: +450m to +550m)
                    new Vector3(480f, -25f, -320f),
                    new Vector3(520f, -25f, 0f),
                    new Vector3(480f, -25f, 320f),
                };

                for (var i = 0; i < mountainOffsets.Length; i++)
                {
                    var isRight = mountainOffsets[i].x > 0;
                    var mountain = BiomeModel("Mountains", "Free_Mountain", materials["Hills Landscape"])
                                ?? BiomeModel("HollywoodHills", "Mountain/SM_mountains", materials["Hills Landscape"]);
                    if (mountain != null)
                    {
                        mountain.name = $"Horizon Mountain {i}";
                        mountain.transform.SetParent(globalHorizonSky.transform, false);
                        mountain.transform.localPosition = mountainOffsets[i];
                        mountain.transform.localRotation = Quaternion.Euler(0f, isRight ? -90f : 90f, 0f);
                        NormalizeModelHeight(mountain, 120f, 0f);
                    }
                }

                // 2. Static Soft Fluffy Cumulus Clouds high in the sky dome (240m-350m altitude)
                var cloudOffsets = new[]
                {
                    new Vector3(-280f, 260f, 380f),
                    new Vector3(260f, 290f, 420f),
                    new Vector3(-120f, 320f, 550f),
                    new Vector3(180f, 280f, 250f),
                    new Vector3(-350f, 270f, 120f),
                    new Vector3(340f, 310f, -150f),
                    new Vector3(-200f, 300f, -300f),
                    new Vector3(220f, 260f, 600f),
                };

                for (var c = 0; c < cloudOffsets.Length; c++)
                {
                    var cloud = BuildCumulusCloudCluster($"Static Sky Cloud {c}", cloudOffsets[c], 55f + (c % 3) * 12f, materials["Hills Cloud"]);
                    if (cloud != null) cloud.transform.SetParent(globalHorizonSky.transform, false);
                }

                // 3. Iconic Hollywood Sign perched on the Right Mountain flank (+X: 380m)
                var signBasePos = new Vector3(380f, 55f, 220f);
                var letterMeshNames = new[]
                {
                    "Letters/SM_letter_H", "Letters/SM_letter_O", "Letters/SM_letter_L",
                    "Letters/SM_letter_L", "Letters/SM_letter_Y", "Letters/SM_letter_W",
                    "Letters/SM_letter_O", "Letters/SM_letter_O", "Letters/SM_letter_D"
                };
                var signRoot = new GameObject("Static Hollywood Sign");
                signRoot.transform.SetParent(globalHorizonSky.transform, false);
                signRoot.transform.localPosition = signBasePos;
                signRoot.transform.localRotation = Quaternion.Euler(0f, 235f, 0f);

                const float spacing = 16f;
                var startOffset = -(letterMeshNames.Length - 1) * spacing * 0.5f;
                for (var li = 0; li < letterMeshNames.Length; li++)
                {
                    var letter = BiomeModel("HollywoodHills", letterMeshNames[li], materials["Hills Sign"]);
                    if (letter != null)
                    {
                        letter.transform.SetParent(signRoot.transform, false);
                        letter.transform.localPosition = new Vector3(startOffset + li * spacing, 0f, 0f);
                        letter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                        NormalizeModelHeight(letter, 22f, 0f);
                    }
                }
            }
        }

        /// Greenwood's horizon: one ring mesh centred on the camera by the horizon
        /// follower. Returns false when the asset is missing so the old ranks are built.
        /// Off: from the road the ring read as a flat green stripe behind the trees
        /// rather than as mountains, and it was taken out on that feedback. The ring
        /// and its fallback stay here to bring back in a better form.
        private static readonly bool GreenwoodHorizonMountains = false;

        private bool BuildMountainRing()
        {
            var material = Resources.Load<Material>("Biomes/Mountains/M_mountain_ring");
            if (material == null) return false;
            var ring = BiomeModel("Mountains", "SM_mountain_ring", material);
            if (ring == null) return false;
            ring.name = "Forest Mountain Ring";
            ring.transform.SetParent(globalHorizonSky.transform, false);
            // The valley was built along the mesh's forward axis, which is the road's.
            // Sunk a little so the inner foothills start below the forest floor.
            ring.transform.localPosition = new Vector3(0f, -12f, 0f);
            ring.transform.localRotation = Quaternion.identity;
            foreach (var r in ring.GetComponentsInChildren<Renderer>())
            {
                // A kilometre-wide mesh in the shadow cascades costs a lot and adds
                // nothing at this distance.
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.sharedMaterial = material;
            }
            return true;
        }

        private Material skyMaterial;

        /// Greenwood gets a baked overcast panorama as its skybox (Tools/Sky/build_sky.py);
        /// the other biomes keep the fogged solid-colour background they were tuned for.
        /// A copy of the material is used because the horizon colour is updated every
        /// frame, and writing to the asset loaded from Resources would edit it on disk
        /// when playing in the editor.
        private void ApplyBiomeSky(int biomeIndex)
        {
            if (skyMaterial != null) Destroy(skyMaterial);
            skyMaterial = null;
            if (biomeIndex == 0)
            {
                var source = Resources.Load<Material>("Sky/M_sky_overcast");
                if (source == null)
                    Debug.LogWarning("Missing sky material Sky/M_sky_overcast; keeping solid background");
                // A shader that failed to compile draws the whole sky magenta; the fogged
                // solid background is better than that.
                else if (source.shader == null || !source.shader.isSupported)
                    Debug.LogWarning($"Sky shader '{(source.shader != null ? source.shader.name : "none")}' cannot draw on " +
                                     "this setup (see the red shader error in the Console); keeping solid background");
                else skyMaterial = new Material(source) { name = "Greenwood Overcast Sky" };
            }
            RenderSettings.skybox = skyMaterial;
            if (Camera.main != null) ApplySkyToCamera(Camera.main);
        }

        private void ApplySkyToCamera(Camera camera)
        {
            camera.clearFlags = skyMaterial != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (skyMaterial != null && camera.GetComponent<SkyHorizonSync>() == null)
                camera.gameObject.AddComponent<SkyHorizonSync>();
        }

        private GameObject BuildCumulusCloudCluster(string name, Vector3 center, float baseSize, Material material)
        {
            var root = new GameObject(name);
            root.transform.position = center;

            var puffOffsets = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(-0.45f, -0.12f, 0.15f),
                new Vector3(0.48f, -0.08f, -0.18f),
                new Vector3(-0.20f, 0.28f, 0.08f),
                new Vector3(0.24f, 0.22f, 0.12f),
                new Vector3(-0.68f, -0.18f, -0.12f),
                new Vector3(0.72f, -0.15f, 0.20f),
                new Vector3(0.0f, -0.18f, 0.35f)
            };
            var puffScales = new[]
            {
                1.0f, 0.82f, 0.85f, 0.74f, 0.76f, 0.60f, 0.65f, 0.70f
            };

            for (var i = 0; i < puffOffsets.Length; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = $"Puff_{i}";
                puff.transform.SetParent(root.transform, false);
                puff.transform.localPosition = puffOffsets[i] * baseSize;
                puff.transform.localScale = Vector3.one * (puffScales[i] * baseSize);
                var collider = puff.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                var renderer = puff.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }
            return Adopt(root);
        }

                private void BuildForest()
        {
            Random.InitState(40621 ^ chunkSeed);
            // The rock-face cliffs stood in for the cuttings a mountain road runs
            // through. Over the real terrain the real cuttings and slopes are there, and
            // the invented faces hung in the air where the real ground falls away.
            if (RoadPath.Route == null || !RoadPath.Route.HasTerrain)
                BuildCliffs(materials.TryGetValue("Forest Cliff", out var cliffMaterial) ? cliffMaterial : null);
            // The new cliff colliders have to be in the physics scene before the forest
            // is planted on them.
            // The planting rays need this chunk's ground colliders in the physics scene.
            if (cliffZones.Count > 0 || OverRealTerrain) Physics.SyncTransforms();
            plantTally = new PlantTally();
            StartCoroutine(ReportPlanting(segStart, plantTally, chunkRoot));

            // The kit's ground texture is bare dirt, so the forest floor has to be made
            // of meshes: pack undergrowth densely enough that the ground barely shows.
            ScatterBand(1.8f, 7.4f, 13f, (d, l, s) =>
                ForestPlant(d, l, 0.9f, 1.7f, "Verge Undergrowth"));
            ScatterBand(1.7f, 13f, 26f, (d, l, s) =>
                ForestPlant(d, l, 1.0f, 2.0f, "Undergrowth"));
            ScatterBand(2.3f, 26f, 55f, (d, l, s) =>
                ForestPlant(d, l, 1.2f, 2.4f, "Deep Undergrowth"));
            // The "Forest Grass" band that ran 7-20 m out is gone: a fourth dense layer of
            // plants over the same verge, it is what made the roadside read as a green
            // carpet. The grass clump is also out of ForestPlants; the undergrowth bands
            // above, ferns and bushes still carry the greenery.

            // Raised roadside grass-bank ribbons removed - they stacked into flat green
            // terraces/"layers" beside the road. The Forest Floor PBR ground plane already
            // covers this area, and the vertical grass tufts (above) supply the greenery.
            //
            // Guard rail along both shoulders. A mountain road has one, and it is the
            // single strongest cue that the road is cut into a slope rather than laid on
            // a field - it also gives the bends an edge to read against. One real-scale
            // W-beam rail replaces the old pair of a flat ribbon on cube posts and a
            // borrowed Synthwave fence half a metre behind it.
            BuildGuardRail(materials["Forest Guard Rail"]);
            if (RoadPath.Route != null) BuildRouteProps();
            PlaceTestAssets();
            if (RoadPath.Route != null && RoadPath.Route.HasCover)
            {
                BuildRouteOpenGround();
                BuildRouteLakes();
            }

            if (NoCanopy) return;

            // Gate A measured Greenwood as alpha-test overdraw, not geometry or draw
            // calls: 850 renderers a chunk of which 824 are cutout, 126 FPS with the
            // canopy and 457 without, while Hollywood draws more triangles and runs four
            // times faster. The only lever that moves it is less foliage covering the
            // screen - instancing and LODs were measured and did nothing.
            //
            // Five near bands used to run here, all spanning 26-66 m. They overlapped
            // almost entirely, so most of what they added was a second and third layer
            // of leaf cards over the same ground - which is precisely the cost, since
            // alpha test defeats early-Z and every overlapping card shades again. Three
            // bands cover the same span with the layering that was being paid for twice.
            // Taller, and one band closer. The reference is a road cut through timber that
            // stands well above the car, not a treeline you look over - so the near band
            // starts at the verge rather than 26 m out, and every band gained height.
            if (RoadPath.Route != null)
            {
                // A forest, not two rows and a field. ScatterBand places one item per
                // step along the road at a random point across the band's width, so a
                // wide band spreads the same count over far more ground: the old 34-80
                // and 70-160 m bands came out at one tree per ~300 m2 - an open field
                // with the odd tree - while the overlapping near bands made the rows.
                // Narrow strips of even width, stacked to 160 m, give the same density
                // all the way back: ~1 tree per 50 m2 near the road, ~1 per 90 m2
                // further out where they are small on screen. Open ground, water and
                // cliffs still keep them out (SpawnForestPiece).
                for (var near = 12f; near < 76f; near += 8f)
                {
                    var from = near;
                    ScatterBand(8f, from, from + 8f, (d, l, s) => ForestTree(d, l, 18f, 32f));
                }
                // Behind 76 m the near bands already close the view, so the back of the
                // stand is thinned to half: RR_BUDGET counted ~5,900 visible trees at
                // 7.9 M triangles, the largest single cost in the frame.
                for (var near = 76f; near < 160f; near += 12f)
                {
                    var from = near;
                    ScatterBand(22f, from, from + 12f, (d, l, s) => ForestTree(d, l, 20f, 32f));
                }
                // Understory: young firs between the trunks, to about 70 m. Tall firs
                // lose their lower branches, so under their crowns the eye ran straight
                // through to bare ground - it read as open land behind a row of trunks.
                // Young trees are what close a real Black Forest stand at eye level.
                for (var near = 14f; near < 70f; near += 14f)
                {
                    var from = near;
                    ScatterBand(9f, from, from + 14f, (d, l, s) => Understory(d, l));
                }
                // The bank itself. Every tall tree near the road is pushed back until its
                // crown clears the carriageway - on a cutting that put all of them past the
                // top of the bank, the slope facing the road stayed bare and only their
                // crowns showed over the crest, like trees sunk to the top in the hill.
                // Young spruce have crowns small enough to stand on the slope itself, from
                // just past the rail, as they do along a real cutting.
                if (OverRealTerrain)
                    ScatterBand(4.5f, 0f, 9f, (d, l, s) => Understory(d, l));
                // Forestry: windthrow and cut stumps on the floor. A managed Black Forest
                // stand is never a clean lawn under the trees.
                ScatterBand(26f, 12f, 70f, (d, l, s) =>
                    SpawnForestPiece("BlackForest|SM_log_fallen", d, l, -0.1f, 0.45f, 0.75f, "Forest Log"));
                ScatterBand(14f, 10f, 60f, (d, l, s) =>
                    SpawnForestPiece("BlackForest|SM_stump", d, l, -0.05f, 0.3f, 0.6f, "Forest Stump"));
            }
            else
            {
                ScatterBand(7f, 9f, 18f, (d, l, s) => ForestTree(d, l, 14f, 22f));
                ScatterBand(7f, 9f, 18f, (d, l, s) => ForestTree(d, l, 16f, 24f));
                ScatterBand(7f, 18f, 30f, (d, l, s) => ForestTree(d, l, 18f, 28f));
                ScatterBand(8f, 26f, 42f, (d, l, s) => ForestTree(d, l, 20f, 32f));
                ScatterBand(8f, 34f, 56f, (d, l, s) => ForestTree(d, l, 16f, 26f));
                // Far canopy. Cheap in coverage terms - it sits at the horizon rather than
                // over the camera - so it keeps the forest reading as deep.
                ScatterBand(11f, 70f, 160f, (d, l, s) => ForestTree(d, l, 18f, 30f));
            }
            ScatterBand(2.2f, 18f, 38f, (d, l, s) =>
                SpawnForestPiece(ForestBush(), d, l, 0.05f, 1.4f, 3.0f, "Forest Bush"));
            ScatterBand(2.8f, 34f, 60f, (d, l, s) =>
                SpawnForestPiece(ForestBush(), d, l, 0.05f, 1.2f, 2.6f, "Forest Bush Deep"));
            ScatterBand(3.5f, 24f, 70f, (d, l, s) =>
                ForestPlant(d, l, 0.6f, 1.4f, "Forest Ground Cover"));
            ScatterBand(2.2f, 18f, 30f, (d, l, s) =>
                ForestPlant(d, l, 0.7f, 1.5f, "Forest Fern Dense"));
        }

        private const float GuardRailSection = 4f;

        /// Gap between the outer edge of the shoulder and the back of the beam. The beam
        /// face sits ~0.09 m nearer the road than that, the posts ~0.35 m further out.
        private const float GuardRailOffset = 0.35f;

        private static bool guardRailReported;

        /// Lays the guard rail section by section, end to end. Deliberately not a
        /// ScatterBand: its jitter, per-chunk spacing and occasional dropouts are what
        /// make vegetation look varied, and on a rail they read as broken barrier.
        ///
        /// Placed off the measured road width, not a constant. A fixed 16 m put it
        /// nine metres into Greenwood's single-lane verge, behind undergrowth taller
        /// than the rail, where nobody could see it.
        ///
        /// Road "distance" is world Z, not length along the road, so stepping it by the
        /// section length opens gaps on every bend - widest on the outside of the curve.
        /// Instead each side's rail line is measured along its true length and cut into
        /// equal pieces, each stretched a few percent to fit exactly. The chunk's end
        /// points are shared with its neighbours, so the rail also meets across chunks.
        private void BuildGuardRail(Material material)
        {
            const float sampleStep = 0.5f;
            var placed = 0;
            var count = Mathf.CeilToInt((segEnd - segStart) / sampleStep) + 1;
            var distances = new float[count];
            var arc = new float[count];
            for (var side = -1; side <= 1; side += 2)
            {
                var previous = Vector3.zero;
                for (var i = 0; i < count; i++)
                {
                    distances[i] = Mathf.Min(segEnd, segStart + i * sampleStep);
                    var point = GuardRailPoint(distances[i], side);
                    arc[i] = i == 0 ? 0f : arc[i - 1] + Vector3.Distance(previous, point);
                    previous = point;
                }

                var sections = Mathf.Max(1, Mathf.RoundToInt(arc[count - 1] / GuardRailSection));
                var length = arc[count - 1] / sections;
                var j = 0;
                var fromDistance = segStart;
                var from = GuardRailPoint(fromDistance, side);
                for (var k = 1; k <= sections; k++)
                {
                    var target = k * length;
                    while (j < count - 2 && arc[j + 1] < target) j++;
                    var toDistance = k == sections
                        ? segEnd
                        : Mathf.Lerp(distances[j], distances[j + 1], Mathf.InverseLerp(arc[j], arc[j + 1], target));
                    var to = GuardRailPoint(toDistance, side);
                    if (PlaceGuardRailSection(from, to, side, (fromDistance + toDistance) * 0.5f, material)) placed++;
                    from = to;
                    fromDistance = toDistance;
                }
            }
            if (!guardRailReported)
            {
                guardRailReported = true;
                var lateral = RoadPath.HalfWidthAt(segStart) + RoadPath.ShoulderWidth + GuardRailOffset;
                Debug.Log($"RR_EVENT guardrail sections={placed} lateral={lateral:0.0}m");
            }
        }

        private static Vector3 GuardRailPoint(float distance, int side) =>
            RoadPath.Point(distance, side * (RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + GuardRailOffset));

        private bool PlaceGuardRailSection(Vector3 from, Vector3 to, int side, float midDistance, Material material) =>
            PlaceAlongSpan("Guardrail", "SM_guardrail_section_4m", material, from, to, side, midDistance,
                "Forest Guard Rail") != null;

        /// Places a long roadside piece (guard rail section, cliff) so it spans from -> to,
        /// stretched to fit exactly, with its front towards the road.
        ///
        /// The FBX axis conversion decides which local axis a mesh runs along and which
        /// way it faces, so this measures instead of assuming: the long axis goes onto
        /// the span, and the side the mesh's bulk sits on (a rail's posts, a cliff's
        /// hillside) goes away from the road.
        private GameObject PlaceAlongSpan(string pack, string mesh, Material material, Vector3 from, Vector3 to,
            int side, float midDistance, string name)
        {
            var span = to - from;
            if (span.sqrMagnitude < 0.25f) return null;
            var piece = BiomeModel(pack, mesh, material);
            if (piece == null) return null;
            piece.name = name;
            var along = span.normalized;
            var middle = (from + to) * 0.5f;
            if (!TryGetMeshBounds(piece, out var bounds))
            {
                piece.transform.SetPositionAndRotation(middle, Quaternion.LookRotation(along, Vector3.up));
                return piece;
            }

            var alongX = bounds.size.x > bounds.size.z;
            var rotation = Quaternion.LookRotation(along, Vector3.up) *
                           (alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            if (Vector3.Dot(rotation * bounds.center, RoadPath.Right(midDistance)) * side < 0f)
                rotation *= Quaternion.Euler(0f, 180f, 0f);

            // Stretch to the exact span so neighbours meet, then centre it on the span.
            var stretch = span.magnitude / Mathf.Max(0.01f, alongX ? bounds.size.x : bounds.size.z);
            var scale = alongX ? new Vector3(stretch, 1f, 1f) : new Vector3(1f, 1f, stretch);
            var centreOffset = rotation * Vector3.Scale(scale, bounds.center);
            piece.transform.localScale = scale;
            piece.transform.SetPositionAndRotation(middle - along * Vector3.Dot(centreOffset, along), rotation);
            return piece;
        }

        // ---------------------------------------------------------------- cliffs

        /// Distance from the outer edge of the shoulder to the line a cliff's front is
        /// kept behind: ~1.4 m past the guard rail's posts.
        private const float CliffOffset = GuardRailOffset + 1.8f;
        /// Wall sections are 24 m and laid end to end: every section ends in the same
        /// profile, so neighbours join without a seam. A run is closed off at both ends
        /// by a 16 m cap that slopes down to the ground.
        private const float CliffSectionLength = 24f;
        private const float CliffCapLength = 16f;
        /// How far behind the cliff line trees and undergrowth are kept clear: the
        /// whole depth of the rock (face, crest, top and back slope, ~45 m) plus a
        /// little. Anything planted inside it stuck out through the rock.
        private const float CliffClearDepth = 50f;

        private static readonly string[] CliffMeshes = { "SM_cliff_01", "SM_cliff_02", "SM_cliff_03" };
        private const string CliffCap = "SM_cliff_end";
        private readonly List<Vector3> cliffZones = new();   // (side, start, end) per chunk

        /// Rock walls 22-36 m tall right behind the guard rail, in runs of three or four
        /// sections, on roughly half the chunks - and on both sides at once in about a
        /// quarter of those, which makes a gorge. Built before the forest so trees and
        /// undergrowth are not planted in front of the faces (see InCliffZone): the small
        /// roadside rocks tried before were hidden by exactly that.
        private void BuildCliffs(Material material)
        {
            cliffZones.Clear();
            if (material == null || Random.value > 0.5f) return;
            // As many sections as fit in the chunk with both caps and 5 m to spare each end.
            var fits = Mathf.FloorToInt((segEnd - segStart - 10f - 2f * CliffCapLength) / CliffSectionLength);
            if (fits < 1) return;
            var sections = Mathf.Min(Random.Range(3, 7), fits);
            var runLength = sections * CliffSectionLength;
            var first = segStart + 5f + CliffCapLength;
            var start = Random.Range(first, Mathf.Max(first + 0.1f, segEnd - 5f - CliffCapLength - runLength));
            var firstSide = Random.value < 0.5f ? -1 : 1;
            var gorge = Random.value < 0.25f;
            for (var side = -1; side <= 1; side += 2)
            {
                if (side != firstSide && !gorge) continue;
                if (!RouteForestAlong(start - CliffCapLength, start + runLength + CliffCapLength, side)) continue;
                var distance = start;
                PlaceCliffPiece(CliffCap, material, AdvanceAlongLine(start, -CliffCapLength, side), start, side, tallEnd: 1);
                for (var k = 0; k < sections; k++)
                {
                    var next = AdvanceAlongLine(distance, CliffSectionLength, side);
                    PlaceCliffPiece(CliffMeshes[Random.Range(0, CliffMeshes.Length)], material, distance, next, side);
                    distance = next;
                }
                var capEnd = AdvanceAlongLine(distance, CliffCapLength, side);
                PlaceCliffPiece(CliffCap, material, distance, capEnd, side, tallEnd: -1);
                cliffZones.Add(new Vector3(side, start - CliffCapLength - 6f, capEnd + 6f));
            }
        }

        private static bool cliffMeshReported;

        /// Lays one cliff piece along the cliff line from fromDistance to toDistance by
        /// bending its mesh to the road, rather than placing it as a straight chord.
        ///
        /// Straight 24 m pieces met at the front on a bend, but their 40 m deep backs
        /// fanned apart on the outside of the curve and ran through each other on the
        /// inside: a V-shaped notch in the crest every 24 m, through which the open
        /// end of a piece showed as a paper-thin sheet of rock. Bent, a run is one
        /// continuous surface on any bend, and it follows the road's rise and fall.
        ///
        /// Each vertex keeps its height and its depth behind the face; its position
        /// along the piece becomes a position along the cliff line (by arc length, so
        /// the texture is not stretched), and depth is measured out along the road's
        /// normal there. tallEnd: 0 for a wall section, -1 / +1 for a cap whose tall
        /// end (the mesh origin) meets the wall at fromDistance / toDistance.
        private GameObject PlaceCliffPiece(string mesh, Material material, float fromDistance, float toDistance,
            int side, int tallEnd = 0)
        {
            var piece = BiomeModel("Cliffs", mesh, material);
            if (piece == null) return null;
            piece.name = "Forest Cliff";
            piece.transform.localScale = Vector3.one;
            piece.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var filter = piece.GetComponentInChildren<MeshFilter>();
            var source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable)
            {
                if (!cliffMeshReported)
                {
                    cliffMeshReported = true;
                    Debug.LogWarning($"Cliff mesh {mesh} is missing or not Read/Write enabled - cliffs skipped.");
                }
                Destroy(piece);
                return null;
            }

            // Into the piece's frame (the piece sits at the origin, unrotated).
            var toPiece = filter.transform.localToWorldMatrix;
            var vertices = source.vertices;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = toPiece.MultiplyPoint3x4(vertices[i]);

            // Measure it: which way it runs and which side is the face. The face is the
            // side the upper part of the rock leans towards - the back slope runs out
            // low and far behind.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, maxY = float.MinValue;
            foreach (var v in vertices)
            {
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
                maxY = Mathf.Max(maxY, v.y);
            }
            float upperZ = 0f;
            var upper = 0;
            foreach (var v in vertices)
            {
                if (v.y < maxY * 0.6f) continue;
                upperZ += v.z;
                upper++;
            }
            var faceSign = upper > 0 && upperZ / upper > (minZ + maxZ) * 0.5f ? 1f : -1f;
            float xFrom = minX, xTo = maxX, joinX = minX;
            if (tallEnd != 0)
            {
                var tallX = toPiece.GetColumn(3).x;
                var farX = Mathf.Abs(minX - tallX) > Mathf.Abs(maxX - tallX) ? minX : maxX;
                xFrom = tallEnd < 0 ? tallX : farX;
                xTo = tallEnd < 0 ? farX : tallX;
                joinX = tallX;
            }
            // Depth is measured from the face at the join, which is the same profile on
            // every piece. Measured from each piece's own front-most point - which the
            // random relief puts up to a metre further out on one piece than the next -
            // neighbours stood at different distances from the road, and each join
            // showed the open edge of the piece standing proud: a paper-thin sheet.
            var faceZ = faceSign > 0f ? float.MinValue : float.MaxValue;
            foreach (var v in vertices)
            {
                if (Mathf.Abs(v.x - joinX) > 0.3f) continue;
                faceZ = faceSign > 0f ? Mathf.Max(faceZ, v.z) : Mathf.Min(faceZ, v.z);
            }
            if (Mathf.Abs(faceZ) == float.MaxValue) faceZ = faceSign > 0f ? maxZ : minZ;
            var span = xTo - xFrom;
            if (Mathf.Abs(span) < 0.01f)
            {
                Destroy(piece);
                return null;
            }

            // The cliff line from -> to, tabulated by arc length.
            const int samples = 48;
            var linePoints = new Vector3[samples + 1];
            var outwards = new Vector3[samples + 1];
            var arcs = new float[samples + 1];
            for (var k = 0; k <= samples; k++)
            {
                var d = Mathf.Lerp(fromDistance, toDistance, k / (float)samples);
                linePoints[k] = CliffLinePoint(d, side);
                outwards[k] = RoadPath.Right(d) * side;
                arcs[k] = k == 0 ? 0f : arcs[k - 1] + Vector3.Distance(linePoints[k - 1], linePoints[k]);
            }
            var origin = linePoints[samples / 2];

            var warped = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                var target = Mathf.Clamp01((v.x - xFrom) / span) * arcs[samples];
                var lo = 0;
                var hi = samples;
                while (hi - lo > 1)
                {
                    var mid = (lo + hi) >> 1;
                    if (arcs[mid] < target) lo = mid; else hi = mid;
                }
                var t = Mathf.InverseLerp(arcs[lo], arcs[hi], target);
                var outward = Vector3.Lerp(outwards[lo], outwards[hi], t).normalized;
                var depth = (faceZ - v.z) * faceSign;
                warped[i] = Vector3.Lerp(linePoints[lo], linePoints[hi], t) + outward * depth + Vector3.up * v.y - origin;
            }

            // Where the mapping mirrors the mesh (+x running against the road, the face
            // on the other side) it would turn inside out: flip the winding back. Unity's
            // Cross is the same formula in either handedness, so the unmirrored basis
            // (x, y, z) gives Dot(Cross(x, y), z) = +1.
            var alongWorld = (linePoints[samples] - linePoints[0]) * Mathf.Sign(span);
            var depthWorld = outwards[samples / 2] * -faceSign;
            var mirrored = Vector3.Dot(Vector3.Cross(alongWorld, Vector3.up), depthWorld) < 0f;

            var bent = new Mesh { name = source.name + " (bent)" };
            if (warped.Length > 65535) bent.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            bent.vertices = warped;
            bent.uv = source.uv;
            var triangles = source.triangles;
            if (mirrored)
                for (var i = 0; i < triangles.Length; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            bent.triangles = triangles;
            bent.RecalculateNormals();
            bent.RecalculateTangents();
            bent.RecalculateBounds();

            if (filter.transform != piece.transform)
            {
                filter.transform.localPosition = Vector3.zero;
                filter.transform.localRotation = Quaternion.identity;
                filter.transform.localScale = Vector3.one;
            }
            filter.sharedMesh = bent;
            piece.AddComponent<OwnedMesh>().Mesh = bent;
            piece.transform.position = origin;
            // A collider only for planting the forest on top (CliffTop), on the Ignore
            // Raycast layer so no game raycast sees it.
            piece.layer = CliffLayer;
            piece.AddComponent<MeshCollider>().sharedMesh = bent;
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return piece;
        }

        /// A cutting is through forest: on the B500, no rock wall where the real
        /// roadside is heath, meadow, a village or the lake.
        private static bool RouteForestAlong(float from, float to, int side)
        {
            if (RoadPath.Route == null || !RoadPath.Route.HasCover) return true;
            for (var d = from; d <= to; d += 10f)
                if (RoadPath.Route.CoverAt(d, side * 20f) != RoadRoute.CoverForest) return false;
            return true;
        }

        private static Vector3 CliffLinePoint(float distance, int side) =>
            RoadPath.Point(distance, side * (RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + CliffOffset));

        /// Road distance is world Z, not length along the road: find the distance that
        /// is `metres` further along the cliff line itself, so sections keep their size
        /// on bends.
        private static float AdvanceAlongLine(float distance, float metres, int side)
        {
            var origin = CliffLinePoint(distance, side);
            var next = distance + metres;
            for (var i = 0; i < 3; i++)
            {
                var got = Vector3.Distance(origin, CliffLinePoint(next, side));
                next = distance + (next - distance) * Mathf.Abs(metres) / Mathf.Max(0.1f, got);
            }
            return next;
        }

        /// True where a cliff face stands between this point and the road, so nothing is
        /// planted in front of it.
        private const int CliffLayer = 2;   // Ignore Raycast

        /// The height of the rock top or back slope at this point, if a cliff is there
        /// and it is not too steep to stand a tree on.
        private static bool CliffTop(float distance, float lateral, out float y)
        {
            y = 0f;
            var p = RoadPath.Point(distance, lateral);
            if (!Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, 1 << CliffLayer)) return false;
            if (hit.normal.y < 0.6f) return false;
            y = hit.point.y;
            return true;
        }

        private bool InCliffZone(float distance, float lateral)
        {
            for (var i = 0; i < cliffZones.Count; i++)
            {
                var z = cliffZones[i];
                if (Mathf.Sign(lateral) != z.x || distance < z.y || distance > z.z) continue;
                var line = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + CliffOffset;
                if (Mathf.Abs(lateral) < line + CliffClearDepth) return true;
            }
            return false;
        }

        /// Mesh bounds in the object's own space. Renderer bounds are world-axis
        /// aligned, so on a curved road they would be inflated and could not tell which
        /// axis is the long one.
        private static bool TryGetMeshBounds(GameObject item, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            var toLocal = item.transform.worldToLocalMatrix;
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh.bounds;
                var toItem = toLocal * filter.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = toItem.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f)));
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            return found;
        }

        private static void KeepTrunkOffRoad(GameObject item, float distance, float lateral, float trunkRadius = 0.9f)
        {
            var side = Mathf.Sign(lateral);
            var minimum = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + trunkRadius;
            if (Mathf.Abs(lateral) >= minimum) return;
            item.transform.position = RoadPath.Point(distance, side * minimum,
                item.transform.position.y - RoadPath.Center(distance).y);
        }

        /// Trunk position is not the thing that reaches into frame - the crown is. A tree
        /// scaled to 24 m can spread 12 m of canopy from a trunk that sits legally outside
        /// the shoulder, which is why nudging lateral offsets never converged.
        ///
        /// This measures the rendered bounds, pushes the tree out until the crown's inner
        /// edge clears the driving corridor, and REJECTS it if that push would be large.
        /// Rejection matters as much as the push: Gate A measured Greenwood as alpha-test
        /// overdraw (126 FPS canopy-on vs 457 off), and the only lever that helps is less
        /// foliage covering screen pixels.
        private const float CanopyMargin = 3.5f;
        private const float MaxCanopyPush = 20f;

        private static bool KeepCanopyOffRoad(GameObject tree, float distance, float side)
        {
            if (!TryGetCombinedBounds(tree, out var bounds)) return true;

            var centre = RoadPath.Center(distance);
            var right = RoadPath.Right(distance);
            var corridor = RoadPath.HalfWidthAt(distance) + RoadPath.ShoulderWidth + CanopyMargin;

            var minProjection = float.PositiveInfinity;
            var maxProjection = float.NegativeInfinity;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                var projection = Vector3.Dot(corner - centre, right);
                minProjection = Mathf.Min(minProjection, projection);
                maxProjection = Mathf.Max(maxProjection, projection);
            }

            var push = side > 0f
                ? Mathf.Max(0f, corridor - minProjection)
                : -Mathf.Max(0f, maxProjection + corridor);

            if (Mathf.Abs(push) > MaxCanopyPush) return false;
            tree.transform.position += right * push;
            return true;
        }

        private void CreateLowTree(float distance, float lateral, float scale)
        {
            var root = Adopt(new GameObject("Background Forest Tree"));
            root.transform.position = RoadPath.Point(distance, lateral, 0f);
            root.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var trunk = Primitive(PrimitiveType.Cylinder, "Background Trunk", Vector3.zero,
                new Vector3(0.55f, 3.8f, 0.55f) * scale, materials["Low Bark"], root.transform);
            trunk.transform.localPosition = new Vector3(0f, 3.8f * scale, 0f);
            var crown = Primitive(PrimitiveType.Sphere, "Background Crown", Vector3.zero,
                new Vector3(5.4f, 4.2f, 5.4f) * scale, materials["Low Leaf"], root.transform);
            crown.transform.localPosition = new Vector3(0f, 8.6f * scale, 0f);
            EnsureOutsideRoad(root, distance, Mathf.Sign(lateral));
        }

        private static readonly System.Collections.Generic.HashSet<UnityEngine.EntityId> smoothedMeshes = new();

        /// Weld vertex normals by position so the low-poly presets shade as smooth,
        /// flat surfaces instead of faceted 3D props. Meshes are shared cached assets,
        /// so each unique mesh is smoothed once per session; non-readable meshes are
        /// left with their imported hard normals.
        private static void SmoothVehicleMeshes(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !smoothedMeshes.Add(mesh.GetEntityId())) continue;
                // Ask before touching, do not catch afterwards. Mesh.vertices on a
                // non-readable mesh logs "Not allowed to access vertices" to the Console
                // and then throws, so the try/catch below swallowed the exception while
                // every wiper blade and window on every vehicle still printed three
                // errors. That is where a run's 287 errors came from - not the Fab
                // plugin, which only ever contributes two.
                if (!mesh.isReadable) continue;
                try
                {
                    var verts = mesh.vertices;
                    var tris = mesh.triangles;
                    var sums = new System.Collections.Generic.Dictionary<Vector3Int, Vector3>(verts.Length);
                    for (var t = 0; t < tris.Length; t += 3)
                    {
                        var a = verts[tris[t]];
                        var b = verts[tris[t + 1]];
                        var c = verts[tris[t + 2]];
                        var face = Vector3.Cross(b - a, c - a);
                        foreach (var idx in new[] { tris[t], tris[t + 1], tris[t + 2] })
                        {
                            var key = PackPosition(verts[idx]);
                            sums.TryGetValue(key, out var n);
                            sums[key] = n + face;
                        }
                    }

                    var normals = new Vector3[verts.Length];
                    for (var i = 0; i < verts.Length; i++)
                    {
                        sums.TryGetValue(PackPosition(verts[i]), out var n);
                        normals[i] = n.sqrMagnitude > 1e-10f ? n.normalized : Vector3.up;
                    }
                    mesh.normals = normals;
                }
                catch (System.Exception)
                {
                    // Non-readable import: keep the factory hard normals.
                }
            }
        }

        private static Vector3Int PackPosition(Vector3 v) => new(
            Mathf.RoundToInt(v.x * 512f), Mathf.RoundToInt(v.y * 512f), Mathf.RoundToInt(v.z * 512f));

        private Material tireMaterial;
        private Material rimMaterial;
        private static readonly System.Collections.Generic.HashSet<UnityEngine.EntityId> roundedWheels = new();
        private static int wheelDebugLogs;

        /// The Synty wheel meshes are 8-10 sided polygons - visibly octagonal tires.
        /// Wheels are separate meshes, so their geometry is swapped for a generated
        /// 40-segment tire ring + rim, oriented along the original's smallest bounds
        /// axis. Materials are dedicated rubber/rim pairs, not the livery atlas.
        private void ReplaceWheelMeshes(GameObject root)
        {
            if (tireMaterial == null || rimMaterial == null) return;
            // Reset per-vehicle: Instantiate copies the prefab hierarchy with MeshFilters
            // that still reference the SAME imported mesh asset, so the EntityId would
            // already be in the set from a previous vehicle's pass, causing every
            // subsequent car's wheels to be skipped (keeping octagonal Synty geometry).
            roundedWheels.Clear();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                var lower = filter.name.ToLowerInvariant();
                if (mesh == null || !lower.Contains("wheel") || lower.Contains("steering")) continue;
                if (!roundedWheels.Add(mesh.GetEntityId())) continue;

                var size = mesh.bounds.size;
                int axle = 0;
                if (size.y <= size.x && size.y <= size.z) axle = 1;
                else if (size.z <= size.x && size.z <= size.y) axle = 2;
                var radius = 0.5f * (axle == 0 ? Mathf.Max(size.y, size.z)
                                   : axle == 1 ? Mathf.Max(size.x, size.z)
                                   : Mathf.Max(size.x, size.y));
                if (radius < 0.05f) continue;
                var width = Mathf.Max(0.12f, size[axle]);
                filter.sharedMesh = BuildRoundWheelMesh(axle, radius, width);
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterials = new[] { tireMaterial, rimMaterial };
                if (wheelDebugLogs < 8)
                {
                    wheelDebugLogs++;
                    Debug.Log($"RR_WHEEL '{filter.name}' bounds={size} axle={axle} " +
                              $"radius={radius:0.000} width={width:0.000} scale={filter.transform.lossyScale.x:0.000}");
                }
            }
        }

        /// 40-segment tire annulus (submesh 0) around a shorter rim cylinder
        /// (submesh 1), revolved around the given axis in mesh-local space.
        private static Mesh BuildRoundWheelMesh(int axle, float radius, float width)
        {
            const int segments = 40;
            var profile = new[]
            {
                new Vector2(radius * 0.60f, -width * 0.5f),
                new Vector2(radius, -width * 0.5f),
                new Vector2(radius, width * 0.5f),
                new Vector2(radius * 0.60f, width * 0.5f),
            };
            var verts = new System.Collections.Generic.List<Vector3>();
            var tire = new System.Collections.Generic.List<int>();
            var rim = new System.Collections.Generic.List<int>();

            Vector3 Point(float alongAxis, float radialOut, int segment)
            {
                var angle = segment / (float)segments * Mathf.PI * 2f;
                var cos = Mathf.Cos(angle) * radialOut;
                var sin = Mathf.Sin(angle) * radialOut;
                return axle switch
                {
                    0 => new Vector3(alongAxis, cos, sin),
                    1 => new Vector3(cos, alongAxis, sin),
                    _ => new Vector3(cos, sin, alongAxis),
                };
            }

            // Tire: revolve the 4-corner profile, one quad strip per profile edge.
            // profile[p] stores (radius, axialOffset), but Point() takes
            // (alongAxis, radialOut) - the arguments must be swapped here, or the
            // tread revolves at the tiny axial-offset "radius" and gets pushed out
            // to the (much larger) profile radius along the axle, producing a
            // small blob beside the rim instead of a tire ring around it.
            for (var p = 0; p < 4; p++)
            {
                var a0 = verts.Count;
                for (var s = 0; s < segments; s++)
                {
                    verts.Add(Point(profile[p].y, profile[p].x, s));
                }
                var a1 = verts.Count;
                var b0 = verts.Count;
                for (var s = 0; s < segments; s++)
                {
                    verts.Add(Point(profile[(p + 1) % 4].y, profile[(p + 1) % 4].x, s));
                }
                var b1 = verts.Count;
                for (var s = 0; s < segments; s++)
                {
                    var s2 = (s + 1) % segments;
                    tire.Add(a0 + s); tire.Add(b0 + s); tire.Add(b0 + s2);
                    tire.Add(a0 + s); tire.Add(b0 + s2); tire.Add(a0 + s2);
                }
            }

            // Rim: short cylinder with caps, sitting between the tire beads.
            var rimRadius = radius * 0.58f;
            var rimWidth = width * 0.35f;
            var rim0 = verts.Count;
            for (var s = 0; s < segments; s++) verts.Add(Point(-rimWidth, rimRadius, s));
            var rim1 = verts.Count;
            for (var s = 0; s < segments; s++) verts.Add(Point(rimWidth, rimRadius, s));
            for (var s = 0; s < segments; s++)
            {
                var s2 = (s + 1) % segments;
                rim.Add(rim0 + s); rim.Add(rim1 + s); rim.Add(rim1 + s2);
                rim.Add(rim0 + s); rim.Add(rim1 + s2); rim.Add(rim0 + s2);
            }
            var capA = verts.Count; verts.Add(Point(-rimWidth * 1.01f, 0f, 0));
            var capB = verts.Count; verts.Add(Point(rimWidth * 1.01f, 0f, 0));
            for (var s = 0; s < segments; s++)
            {
                var s2 = (s + 1) % segments;
                rim.Add(capA); rim.Add(rim0 + s2); rim.Add(rim0 + s);
                rim.Add(capB); rim.Add(rim1 + s); rim.Add(rim1 + s2);
            }

            var mesh = new Mesh { name = "RoundWheel" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tire, 0);
            mesh.SetTriangles(rim, 1);
            var uvs = new Vector2[verts.Count];
            for (var i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(0.5f, 0.5f);
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            return mesh;
        }

        private void BuildCar()
        {
            var spawn = startDistance + 5f;
            // Single-lane biomes (country road, desert two-lane) start centred; multi-lane cities keep the classic right-lane launch.
            var startLaneCount = LaneCountFor(BiomeIndexAt(startDistance));
            var startLateral = startLaneCount == 1 ? -1.2f : -2.25f;
            car = new GameObject($"Player {GameState.CurrentCar.Name}").transform;
            car.position = RoadPath.Point(spawn, startLateral, 0.85f);
            car.rotation = RoadPath.Rotation(spawn);
            var body = Primitive(PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(2.15f, 0.62f, 4.4f), materials["Car Orange"], car);
            body.transform.localPosition = Vector3.zero;
            var hood = Primitive(PrimitiveType.Cube, "Hood", Vector3.zero, new Vector3(1.95f, 0.34f, 1.45f), materials["Car Orange"], car);
            hood.transform.localPosition = new Vector3(0f, 0.35f, 1.12f);
            var cabin = Primitive(PrimitiveType.Cube, "Cabin", Vector3.zero, new Vector3(1.72f, 0.68f, 1.75f), materials["Glass"], car);
            cabin.transform.localPosition = new Vector3(0f, 0.64f, -0.35f);
            Primitive(PrimitiveType.Cube, "Stripe", Vector3.zero, new Vector3(0.34f, 0.03f, 4.25f), materials["White Paint"], car).transform.localPosition = new Vector3(0f, 0.33f, 0f);
			Primitive(PrimitiveType.Cube, "Front Bumper", Vector3.zero, new Vector3(2.22f, 0.18f, 0.18f), materials["Car Dark"], car).transform.localPosition = new Vector3(0f, -0.05f, 2.18f);
			Primitive(PrimitiveType.Cube, "Rear Bumper", Vector3.zero, new Vector3(2.22f, 0.18f, 0.18f), materials["Car Dark"], car).transform.localPosition = new Vector3(0f, -0.05f, -2.18f);
			var lightMaterial = MakeMaterial("Headlight", new Color(1f, 0.92f, 0.68f), 0.1f, 0.9f);
			lightMaterial.SetColor("_EmissionColor", new Color(3.2f, 2.7f, 1.6f));
			lightMaterial.EnableKeyword("_EMISSION");
			foreach (var x in new[] { -0.72f, 0.72f })
				Primitive(PrimitiveType.Cube, "Headlight", Vector3.zero, new Vector3(0.44f, 0.20f, 0.08f), lightMaterial, car).transform.localPosition = new Vector3(x, 0.08f, 2.24f);
            foreach (var x in new[] { -1.08f, 1.08f })
            foreach (var z in new[] { -1.38f, 1.38f })
            {
                var wheel = Primitive(PrimitiveType.Cylinder, "Wheel", Vector3.zero, new Vector3(0.46f, 0.24f, 0.46f), materials["Tire"], car);
                wheel.transform.localPosition = new Vector3(x, -0.24f, z);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
			// Replace the fallback blockout with whichever Street Racer preset is selected
			// in the garage. Each car carries its own livery from the shipped catalogue.
			var selected = GameState.CurrentCar;
			// Measured off the spawned mesh below, then applied once the controller
			// exists. It used to be written straight onto car.GetComponent<Arcade...>()
			// from inside this block - but the controller is not added until the end of
			// the method, so that GetComponent returned null every time and the player
			// silently kept the 2.5 m / 1.3 m placeholder hull no matter which car was
			// selected. The garage's long chassis were colliding as small hatchbacks.
			var playerHull = Vector2.zero;
			var racerPrefab = Resources.Load<GameObject>($"Vehicles/{selected.Mesh}");
			if (racerPrefab != null)
			{
				// Every Synty preset exposes three slots - StreetRacerSHD (chassis),
				// StreetRacerSHD_Livery (painted panels) and GlassSHD. Assigning one
				// material to all three rendered the windows as opaque painted metal and
				// gave paint, trim and glass an identical flat response.
				// Livery decals are BACK (flat colour lost all detail); the details the
				// player wanted gone stay gone: no emissive mask, smooth-shaded meshes.
				var atlas = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_01_A");
				var liveryTexture = Resources.Load<Texture2D>($"Vehicles/{selected.Livery}") ?? atlas;

				// Clearcoat paint: glossy and slightly metallic so it catches the
				// reflection probes and the biome key light.
				var livery = MakeMaterial($"Livery {selected.Name}", Color.white, 0.42f, 0.82f);
				if (liveryTexture != null)
				{
					livery.mainTexture = liveryTexture;
					if (livery.HasProperty("_BaseMap")) livery.SetTexture("_BaseMap", liveryTexture);
				}

				// Chassis/trim: the car's OWN livery sheet, same as the showroom - the
				// base atlas here is what made the world car and the garage disagree.
				var chassis = MakeMaterial($"Chassis {selected.Name}", Color.white, 0.30f, 0.55f);
				if (liveryTexture != null)
				{
					chassis.mainTexture = liveryTexture;
					if (chassis.HasProperty("_BaseMap")) chassis.SetTexture("_BaseMap", liveryTexture);
				}

				// The Synty emissive mask is GONE on purpose: on flat materials without
				// the livery atlas the mask's UV islands painted mid-grey glow over the
				// whole body and turned the wheels into bright yellow rings.

				var glass = MakeMaterial($"Glass {selected.Name}", new Color(0.06f, 0.09f, 0.12f), 0.0f, 0.96f);
				glass.SetFloat("_Smoothness", 0.96f);
				materials["Street Racer Atlas"] = livery;
				materials["Street Racer Chassis"] = chassis;
				materials["Street Racer Glass"] = glass;

				foreach (var renderer in car.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
				var racerVisual = Instantiate(racerPrefab, car);
				racerVisual.name = $"Synty {selected.Name} Visual";
				SmoothVehicleMeshes(racerVisual);
				racerVisual.transform.localPosition = new Vector3(0f, -0.48f, 0f);
				// The source FBX uses Z-up; Unity otherwise imports the complete preset on its side.
				racerVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
				racerVisual.transform.localScale = Vector3.one;
				// Collision radius must match the mesh: fixed 4.3 m / 2.05 m radii were
				// sized for a small car, so a 7.2 m truck overlapped a 5.0 m car by ~1.8 m
				// before the hit registered - that overlap is the clipping.
				if (TryGetLocalFootprint(racerVisual, out var playerHalfLength, out var playerHalfWidth))
					playerHull = new Vector2(playerHalfLength, playerHalfWidth);
                foreach (var renderer in racerVisual.GetComponentsInChildren<Renderer>(true))
                {
					renderer.enabled = true;
					var source = renderer.sharedMaterials;
					var assigned = new Material[source.Length];
					for (var i = 0; i < assigned.Length; i++)
					{
						var slot = source[i] != null ? source[i].name.ToLowerInvariant() : string.Empty;
						assigned[i] = slot.Contains("glass") ? materials["Street Racer Glass"]
							: slot.Contains("livery") ? materials["Street Racer Atlas"]
							: materials["Street Racer Chassis"];
					}
                    renderer.sharedMaterials = assigned;
                }
                // Wheels last: the slot pass above would overwrite their rubber/rim pair.
                ReplaceWheelMeshes(racerVisual);
                foreach (var l in racerVisual.GetComponentsInChildren<Light>(true)) DestroyImmediate(l.gameObject == racerVisual ? l : l.gameObject);
                foreach (var b in racerVisual.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null) continue;
                    var typeName = b.GetType().Name;
                    if (typeName.Contains("Halo") || typeName.Contains("Flare") || typeName.Contains("LensFlare") || typeName.Contains("Light"))
                        DestroyImmediate(b);
                }
            }
            BuildDriver(car);
            foreach (var collider in car.GetComponentsInChildren<Collider>()) Destroy(collider);
            var carCollider = car.gameObject.AddComponent<BoxCollider>();
            carCollider.size = new Vector3(2.15f, 1.2f, 4.4f);
            carCollider.center = new Vector3(0f, 0.25f, 0f);
            var arcadeController = car.gameObject.AddComponent<ArcadeCarController>();
            arcadeController.RoadDistance = spawn;
            // LateralOffset defaults to -2.25 (the right-lane launch); without syncing it
            // here, the countdown-phase Update() re-snaps the car to that default every
            // frame, undoing the centred spawn just chosen above for single-lane biomes.
            arcadeController.LateralOffset = startLateral;
            if (playerHull.sqrMagnitude > 0f)
            {
                // Traffic is normalised to a known length before it is measured; the
                // player's visual is left at its native prefab scale so the hand-tuned
                // ride height keeps the tyres on the asphalt. That means the measurement
                // here inherits whatever scale the FBX imported at, so it is clamped to
                // the range a road vehicle can actually occupy - a motorbike at the low
                // end, a semi cab at the high end - rather than trusted outright.
                arcadeController.HalfLength = Mathf.Clamp(playerHull.x, 0.9f, 8f);
                arcadeController.HalfWidth = Mathf.Clamp(playerHull.y, 0.4f, 1.8f);
                // The trigger box follows the same measurement, so the collider the
                // directors raycast against agrees with the hull the overlap test uses.
                carCollider.size = new Vector3(arcadeController.HalfWidth * 2f, 1.2f, arcadeController.HalfLength * 2f);
            }
            car.gameObject.AddComponent<RoadRageAudioAndVFX>();
        }

        private void BuildDriver(Transform vehicle)
        {
            var driver = new GameObject("Visible Driver").transform;
            driver.SetParent(vehicle, false);
            driver.localPosition = new Vector3(-0.43f, -0.28f, -0.26f);
            var torso = Primitive(PrimitiveType.Capsule, "Driver Torso", Vector3.zero,
                new Vector3(0.26f, 0.32f, 0.26f), materials["Driver Jacket"], driver);
            torso.transform.localPosition = new Vector3(0f, 0.35f, -0.02f);
            var head = Primitive(PrimitiveType.Sphere, "Driver Head", Vector3.zero,
                new Vector3(0.20f, 0.22f, 0.20f), materials["Driver Skin"], driver);
            head.transform.localPosition = new Vector3(0f, 0.64f, 0.04f);
            var hair = Primitive(PrimitiveType.Sphere, "Driver Hair", Vector3.zero,
                new Vector3(0.205f, 0.10f, 0.21f), materials["Driver Hair"], driver);
            hair.transform.localPosition = new Vector3(0f, 0.72f, 0.03f);
            foreach (var armX in new[] { -0.16f, 0.16f })
            {
                var arm = Primitive(PrimitiveType.Cylinder, "Driver Arm", Vector3.zero,
                    new Vector3(0.065f, 0.26f, 0.065f), materials["Driver Jacket"], driver);
                arm.transform.localPosition = new Vector3(armX, 0.44f, 0.22f);
                arm.transform.localRotation = Quaternion.Euler(65f, 0f, armX < 0f ? -16f : 16f);
            }
        }

        /// The assembled building variants from the NYC set, mirrored into Resources.
        /// The set ships these alongside the eight bare models - bottom/middle/roof
        /// compositions with more storeys and more silhouettes than the meshes alone.
        /// Shared by the Neon City frontage and the Manhattan blocks so both draw from
        /// the same catalogue rather than each keeping a partly-wrong list of its own.
        /// Kerbside parking. Ordinary cars only - no exotics, no trucks, nothing that
        /// reads as a set piece; a parked row is background, and anything eye-catching in
        /// it looks placed.
        private static readonly string[] ParkedCars =
        {
            "Vehicles/SK_Veh_Preset_Sedan_01", "Vehicles/SK_Veh_Preset_Hatch_01",
            "Vehicles/SK_Veh_Preset_Ute_01", "Vehicles/SK_Veh_Preset_Ute_02",
            "Vehicles/SK_Veh_Preset_Muscle_01",
        };

        /// Pavement clutter the NYCBlock6 pack shipped and nothing ever placed.
        private static readonly string[] PavementClutter =
        {
            "Buildings/NYCBlock6/Rubbish00", "Buildings/NYCBlock6/Rubbish01",
            "Buildings/NYCBlock6/Basket01", "Buildings/NYCBlock6/Basket03",
            "Buildings/NYCBlock6/Basket04", "Buildings/NYCBlock6/Post",
            "Buildings/NYCBlock6/Sewer",
        };

        private static readonly string[] NycVariants =
        {
            "Buildings/NYCVariants/building_1_1", "Buildings/NYCVariants/building_1_2", "Buildings/NYCVariants/building_1_3",
            "Buildings/NYCVariants/building_1_4", "Buildings/NYCVariants/building_1_5", "Buildings/NYCVariants/building_2_1",
            "Buildings/NYCVariants/building_2_2", "Buildings/NYCVariants/building_2_3", "Buildings/NYCVariants/building_2_4",
            "Buildings/NYCVariants/building_2_5", "Buildings/NYCVariants/building_3_1", "Buildings/NYCVariants/building_3_2",
            "Buildings/NYCVariants/building_3_3", "Buildings/NYCVariants/building_3_4", "Buildings/NYCVariants/building_3_5",
            "Buildings/NYCVariants/building_4_1", "Buildings/NYCVariants/building_4_2", "Buildings/NYCVariants/building_4_3",
            "Buildings/NYCVariants/building_4_4", "Buildings/NYCVariants/building_4_5", "Buildings/NYCVariants/building_5_1",
            "Buildings/NYCVariants/building_5_2", "Buildings/NYCVariants/building_5_3", "Buildings/NYCVariants/building_5_4",
            "Buildings/NYCVariants/building_5_5", "Buildings/NYCVariants/building_6_1", "Buildings/NYCVariants/building_6_2",
            "Buildings/NYCVariants/building_6_3", "Buildings/NYCVariants/building_6_4", "Buildings/NYCVariants/building_6_5",
            "Buildings/NYCVariants/building_6_6", "Buildings/NYCVariants/building_6_7", "Buildings/NYCVariants/building_6_8",
            "Buildings/NYCVariants/building_6_9", "Buildings/NYCVariants/building_6_10", "Buildings/NYCVariants/building_8_1",
            "Buildings/NYCVariants/building_8_2", "Buildings/NYCVariants/building_8_3", "Buildings/NYCVariants/building_8_4",
            "Buildings/NYCVariants/building_8_5", "Buildings/NYCVariants/building_8_6", "Buildings/NYCVariants/building_8_7",
            "Buildings/NYCVariants/building_8_8", "Buildings/NYCVariants/building_8_9", "Buildings/NYCVariants/building_8_10",
            "Buildings/NYCVariants/building_9_1", "Buildings/NYCVariants/building_9_2", "Buildings/NYCVariants/building_9_3",
            "Buildings/NYCVariants/building_9_4", "Buildings/NYCVariants/building_9_5", "Buildings/NYCVariants/building_9_6",
            "Buildings/NYCVariants/building_9_7", "Buildings/NYCVariants/building_9_8", "Buildings/NYCVariants/building_9_9",
            "Buildings/NYCVariants/building_9_10"
        };

        private static readonly TrafficCarController.Offence[] OffenceCycle =
        {
            TrafficCarController.Offence.Weaving,
            TrafficCarController.Offence.Speeding,
            TrafficCarController.Offence.WrongWay,
            TrafficCarController.Offence.Tailgating,
            TrafficCarController.Offence.Weaving,
        };

        private Transform livingTraffic;
        private float trafficTopUpTimer;

        /// Share of the full traffic count a road carries. A single lane each way has
        /// nowhere to pass, so it takes a third of a six-lane highway's traffic.
        /// Two lanes each way is Greenwood's mountain road: 0.65 of the highway's traffic
        /// still packed it into queues and pile-ups, so it runs lighter.
        private static float TrafficScaleFor(int laneCount) =>
            laneCount >= 3 ? 1f : laneCount == 2 ? 0.45f : 0.35f;

        /// Weaving and wrong-way driving put a car across the only lane of a single-lane
        /// road, so there they become speeding: still an offender worth chasing.
        private static TrafficCarController.Offence OffenceForRoad(TrafficCarController.Offence offence, int laneCount) =>
            laneCount < 2 && (offence == TrafficCarController.Offence.WrongWay ||
                              offence == TrafficCarController.Offence.Weaving)
                ? TrafficCarController.Offence.Speeding
                : offence;

        /// Cars on the road at full intensity versus at the start. Twelve is a busy
        /// highway to begin with; by the time a run is going well it should be work.
        private const int BaseTrafficCount = 12;
        /// Twenty-two cars is a desktop figure. On mobile the escalation still happens,
        /// it just tops out where the frame budget does.
        private static int PeakTrafficCount => RichDetailBudget ? 12 : 8;

        /// Adds traffic as a run escalates.
        ///
        /// Traffic was spawned once at bootstrap and only recycled after that, so the
        /// road at 10 km was exactly as busy as the road at 200 m. Density is the main
        /// lever an endless driving game has for pacing, and it was not being pulled.
        private void EscalateTraffic()
        {
            if (livingTraffic == null || GameState.RunOver) return;
            trafficTopUpTimer -= Time.deltaTime;
            if (trafficTopUpTimer > 0f) return;
            trafficTopUpTimer = 2.5f;

            // Both ends scale with the carriageway. Only the ceiling used to, so at the
            // start of a run the target was twelve cars on any road - the top-up added a
            // car every 2.5 s until Greenwood's single lane held twelve, and they bunched
            // into knots with nowhere to pass.
            var laneCount = LaneCountFor(BiomeIndexAt(TrafficCarController.PlayerDistance));
            var roadScale = TrafficScaleFor(laneCount);
            var target = Mathf.RoundToInt(Mathf.Lerp(BaseTrafficCount * roadScale, PeakTrafficCount * roadScale,
                GameState.RunIntensity));
            if (TrafficCarController.All.Count >= target) return;

            // Spawned well ahead so a car never appears in view: 560-740 m, the same window
            // traffic is recycled into. 320-520 m was in plain view in Greenwood's fog.
            var models = new[]
            {
                "SK_Veh_Preset_Sedan_01", "SK_Veh_Preset_Hatch_01", "SK_Veh_Preset_Sports_01",
                "SK_Veh_Preset_Muscle_01", "SK_Veh_Preset_Exotic_01", "SK_Veh_Preset_Ute_01",
                "SK_Veh_Preset_Ute_02", "SK_Veh_Preset_Ute_03", "SK_Veh_Preset_Ute_04"
            };
            var index = TrafficCarController.All.Count;
            var lane = new[] { -0.85f, 0.2f, -0.5f, 0.5f, -0.2f, 0.85f }[index % 6];
            var direction = lane < 0f ? 1f : -1f;
            // Violators get more common as the run escalates, so the road grows more
            // hostile rather than merely more crowded.
            var violatorOdds = Mathf.Lerp(0.28f, 0.55f, GameState.RunIntensity);
            var offence = Random.value < violatorOdds
                ? OffenceForRoad(OffenceCycle[index % OffenceCycle.Length], laneCount)
                : TrafficCarController.Offence.None;
            var speed = (direction > 0f ? 68f + index % 5 * 14f : 95f + index % 4 * 15f)
                        * Mathf.Lerp(1f, 1.18f, GameState.RunIntensity);

            CreateTrafficVehicle(livingTraffic, $"Traffic Car {index + 1}", NextRodinModel() ?? models[index % models.Length],
                Color.white, TrafficCarController.PlayerDistance + Random.Range(560f, 740f),
                lane, speed, direction, false, 0f, offence);
        }

        // ------------------------------------------------------------ Rodin traffic

        /// The Rodin cars in Assets/Resources/TestAssets (git-ignored, on the owner's PC
        /// only) drive in traffic in place of the Synty standard cars: every test model
        /// whose name starts with "car". Without any, traffic is the Synty set as before.
        ///
        /// Each is used at the first detail level inside RodinTrafficTriangleBudget - LOD4,
        /// about 31k triangles, on the cars exported so far - with its wheels part of the
        /// body. Road Rage > Set Up Rodin Models splits each car's paint from the rest of
        /// its texture into <name>_paint.mat: the paint becomes grey shading and URP Lit's
        /// detail colour, times two, over a mask, gives it its colour back. Swapping that
        /// one 4x4 colour texture repaints the body and leaves glass, tyres, lights and
        /// chrome as they were. A car without a paint material keeps its own colour.
        private const long RodinTrafficTriangleBudget = 40000;
        private const string RodinModelPrefix = "Rodin|";
        private const float RodinDefaultLength = 4.6f;

        /// Paint colours as the texture stores them. A null entry keeps the car's own.
        private static readonly Color?[] RodinPaints =
        {
            null, null,
            new Color(0.62f, 0.06f, 0.05f), new Color(0.42f, 0.05f, 0.08f),
            new Color(0.06f, 0.16f, 0.46f), new Color(0.16f, 0.36f, 0.66f),
            new Color(0.90f, 0.90f, 0.88f), new Color(0.66f, 0.68f, 0.70f),
            new Color(0.30f, 0.31f, 0.33f), new Color(0.07f, 0.07f, 0.08f),
            new Color(0.10f, 0.32f, 0.18f), new Color(0.86f, 0.62f, 0.08f),
            new Color(0.84f, 0.34f, 0.06f), new Color(0.46f, 0.40f, 0.30f),
        };

        private sealed class RodinCar
        {
            public string Name;
            public GameObject Template;
            public Material Paint;
            public Material[] Painted;
        }

        private List<RodinCar> rodinCars;
        private Transform rodinTemplates;
        private readonly List<int> rodinBag = new();

        private List<RodinCar> RodinCars
        {
            get
            {
                if (rodinCars != null) return rodinCars;
                rodinCars = new List<RodinCar>();
                var paints = new Dictionary<string, Material>();
                foreach (var material in Resources.LoadAll<Material>("TestAssets"))
                    if (material.name.EndsWith("_paint")) paints[material.name] = material;
                foreach (var asset in TestAssets)
                {
                    if (!asset.name.StartsWith("car", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var template = BuildRodinTemplate(asset);
                    if (template == null) continue;
                    paints.TryGetValue(asset.name + "_paint", out var paint);
                    rodinCars.Add(new RodinCar
                    {
                        Name = asset.name, Template = template, Paint = paint,
                        Painted = new Material[RodinPaints.Length],
                    });
                }
                if (rodinCars.Count > 0)
                    Debug.Log($"RR_RODIN {rodinCars.Count} Rodin car(s) in traffic: " +
                              string.Join(", ", rodinCars.ConvertAll(c => c.Name + (c.Paint != null ? "" : " (own colour)"))));
                return rodinCars;
            }
        }

        /// The next Rodin car for a traffic slot, from a shuffled bag so the same car does
        /// not come round twice in a row; null when there are none.
        private string NextRodinModel()
        {
            var cars = RodinCars;
            if (cars.Count == 0) return null;
            if (rodinBag.Count == 0)
            {
                for (var i = 0; i < cars.Count; i++) rodinBag.Add(i);
                for (var i = rodinBag.Count - 1; i > 0; i--)
                {
                    var j = Random.Range(0, i + 1);
                    (rodinBag[i], rodinBag[j]) = (rodinBag[j], rodinBag[i]);
                }
            }
            var pick = rodinBag[rodinBag.Count - 1];
            rodinBag.RemoveAt(rodinBag.Count - 1);
            return RodinModelPrefix + pick;
        }

        /// One stripped copy per car, kept inactive under the bootstrap: the heavy detail
        /// levels, colliders and scripts are removed once, not per traffic car.
        private GameObject BuildRodinTemplate(GameObject asset)
        {
            if (rodinTemplates == null)
            {
                rodinTemplates = new GameObject("Rodin Car Templates").transform;
                rodinTemplates.SetParent(transform, false);
                rodinTemplates.gameObject.SetActive(false);
            }
            var template = Instantiate(asset, rodinTemplates);
            template.name = asset.name;
            var group = template.GetComponentInChildren<LODGroup>(true);
            var lods = group != null ? group.GetLODs() : null;
            if (lods != null && lods.Length > 0)
            {
                var keep = lods.Length - 1;
                for (var i = lods.Length - 1; i >= 0; i--)
                {
                    var count = 0L;
                    foreach (var r in lods[i].renderers) count += Triangles(r);
                    if (count <= RodinTrafficTriangleBudget) keep = i;
                }
                var kept = new HashSet<Renderer>(lods[keep].renderers);
                for (var i = 0; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                    {
                        if (r == null || kept.Contains(r)) continue;
                        if (r.gameObject != template && r.transform.childCount == 0) DestroyImmediate(r.gameObject);
                        else r.enabled = false;
                    }
                // One level, dropped only once the car is a sliver on screen.
                var renderers = new List<Renderer>();
                foreach (var r in kept) if (r != null) renderers.Add(r);
                group.SetLODs(new[] { new LOD(0.006f, renderers.ToArray()) });
                group.RecalculateBounds();
                var total = 0L;
                foreach (var r in renderers) total += Triangles(r);
                Debug.Log($"RR_RODIN '{asset.name}' traffic uses LOD{keep}: {total:N0} triangles");
            }
            foreach (var collider in template.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var light in template.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
            foreach (var r in template.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = ShadowCastingMode.On;
            return template;
        }

        private Material RodinPaint(RodinCar rodinCar, int paint)
        {
            if (rodinCar.Paint == null || RodinPaints[paint] == null) return rodinCar.Paint;
            if (rodinCar.Painted[paint] != null) return rodinCar.Painted[paint];
            var colour = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = $"{rodinCar.Name} paint {paint}" };
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = RodinPaints[paint].Value;
            colour.SetPixels(pixels);
            colour.Apply(false, true);
            var material = new Material(rodinCar.Paint) { name = $"{rodinCar.Paint.name} {paint}" };
            material.SetTexture("_DetailAlbedoMap", colour);
            rodinCar.Painted[paint] = material;
            return material;
        }

        /// A Rodin car as a traffic car's visual: front along +Z (the "_r" turn in its
        /// name, as at the test spot), at the height in its name or a car's length, in a
        /// random colour, wheels on the road.
        private bool BuildRodinVisual(Transform root, string name, string modelName)
        {
            if (!int.TryParse(modelName.Substring(RodinModelPrefix.Length), out var index)) return false;
            var cars = RodinCars;
            if (index < 0 || index >= cars.Count) return false;
            var rodinCar = cars[index];
            var visual = Instantiate(rodinCar.Template, root);
            visual.name = $"{name} Visual ({rodinCar.Name})";
            visual.SetActive(true);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, TestAssetRotation(rodinCar.Name), 0f);
            visual.transform.localScale = rodinCar.Template.transform.localScale;
            var paint = RodinPaint(rodinCar, Random.Range(0, RodinPaints.Length));
            if (paint != null)
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    var shared = renderer.sharedMaterials;
                    for (var m = 0; m < shared.Length; m++) shared[m] = paint;
                    renderer.sharedMaterials = shared;
                }
            if (!TryGetCombinedBounds(visual, out var bounds) || bounds.size.y < 0.001f) return true;
            var named = System.Text.RegularExpressions.Regex.IsMatch(rodinCar.Name, @"_h\d",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var length = Mathf.Max(bounds.size.x, bounds.size.z);
            var scale = named ? TestAssetHeight(rodinCar.Name) / bounds.size.y : RodinDefaultLength / length;
            // A height typed for the test spot that makes a bus or a toy of it is not trusted.
            if (length * scale < 3.2f || length * scale > 6.5f) scale = RodinDefaultLength / length;
            visual.transform.localScale *= scale;
            NormalizeVehicleVisual(visual, Mathf.Max(bounds.size.x, bounds.size.z) * scale);
            return true;
        }

        private void BuildTraffic()
        {
            var trafficRoot = new GameObject("Living Highway Traffic").transform;
            livingTraffic = trafficRoot;
            // Six presets out of the fifteen the pack ships, on a twelve-car spawn, meant
            // the same model appeared twice in a row often enough to read as a repeat.
            // The four utes and the four truck bodies are all here now, so a full block
            // of traffic no longer duplicates.
            var models = new[]
            {
                "SK_Veh_Preset_Sedan_01", "SK_Veh_Preset_Hatch_01", "SK_Veh_Preset_Sports_01",
                "SK_Veh_Preset_Muscle_01", "SK_Veh_Preset_Exotic_01", "SK_Veh_Preset_Ute_01",
                "SK_Veh_Preset_Ute_02", "SK_Veh_Preset_Ute_03", "SK_Veh_Preset_Ute_04"
            };
            // Distinct bodies for the two special roles, so a tanker and a hauler in the
            // same block are not the same lorry twice.
            var tankers = new[] { "SK_Veh_Preset_Truck_01", "SK_Veh_Preset_Truck_02" };
            var haulers = new[] { "SK_Veh_Preset_Truck_03", "SK_Veh_Preset_Truck_04" };
            var palette = new[]
            {
                new Color(0.82f, 0.10f, 0.08f), new Color(0.10f, 0.34f, 0.88f),
                new Color(0.94f, 0.72f, 0.10f), new Color(0.12f, 0.68f, 0.43f),
                new Color(0.72f, 0.18f, 0.78f), new Color(0.80f, 0.82f, 0.86f)
            };
            // Clear 100-meter safety corridor in front of player (player spawns at startDistance + 5f).
            // Wide spacing: the first kilometre used to be a junk pile of angled cars
            // queuing around each other right at the start line.
            var forwardSpread = new[]
            {
                110f, 155f, 205f, 260f, 320f, 385f, 455f, 530f, 610f, 695f, 780f, 865f,
                // Six more, extending the line rather than packing it. The first attempt
                // interleaved them - 135, 180, 235 and so on - which cut the spacing in
                // the first 420 m to 20-45 m. Cars at different speeds inside gaps that
                // small spend their whole life in each other's following distance, so the
                // road bunched into knots before anything had even crashed. Traffic
                // recycles ahead of the player anyway, so a car placed at 1.4 km still
                // arrives; it just arrives spread out.
                955f, 1050f, 1150f, 1255f, 1365f, 1480f,
            };
            var distances = new float[forwardSpread.Length];
            for (var i = 0; i < forwardSpread.Length; i++) distances[i] = startDistance + forwardSpread[i];
            // Fractions of half-width, so cars sit in lanes on any road profile.
            var lanes = new[]
            {
                -0.85f, 0.2f, -0.5f, 0.5f, -0.2f, 0.85f, -0.85f, 0.2f, -0.5f, 0.5f, -0.2f, 0.85f,
                0.5f, -0.85f, 0.2f, -0.5f, 0.85f, -0.2f,
            };
            // Twelve cars on a six-lane highway is traffic; the same twelve on a two-lane
            // country road is a wall you cannot get through. Scale with the carriageway.
            var laneCount = LaneCountFor(BiomeIndexAt(startDistance));
            var trafficCount = Mathf.RoundToInt(lanes.Length * TrafficScaleFor(laneCount));
            var brutes = 0;
            var enforcers = 0;
            var cabs = 0;
            var rodinSpawned = 0;
            for (var i = 0; i < Mathf.Min(distances.Length, trafficCount); i++)
            {
                var direction = lanes[i] < 0f ? 1f : -1f;
                var speed = direction > 0f ? 68f + i % 5 * 14f : 95f + i % 4 * 15f;
                var violatorEvery = ArcadeCarController.CinematicPilot ? 2 : 3;
                var offence = i % violatorEvery == 1
                    ? OffenceForRoad(OffenceCycle[(i / violatorEvery) % OffenceCycle.Length], laneCount)
                    : TrafficCarController.Offence.None;

                var role = TrafficCarController.VehicleRole.Standard;
                var model = models[i % models.Length];
                if (i == 4 || i == 9)
                {
                    role = TrafficCarController.VehicleRole.FuelTanker;
                    model = tankers[i % tankers.Length];
                }
                else if (i == 6 || i == 11)
                {
                    role = TrafficCarController.VehicleRole.CarHauler;
                    model = haulers[i % haulers.Length];
                }
                else
                {
                    // The garage roster patrols the highway too - the buyable vehicles
                    // are not garage-only decoration. Sedans/muscle cars weave, brute
                    // pickups and trail utes speed, exotic/sports cars run the wrong
                    // way, and the Enforcer hogs a lane. Motorbikes are OUT of the
                    // game entirely by design decision.
                    if (offence == TrafficCarController.Offence.Weaving)
                        model = i % 4 == 1 ? "SK_Veh_Preset_Sedan_01" : "SK_Veh_Preset_Muscle_01";
                    else if (offence == TrafficCarController.Offence.Speeding)
                        model = i % 4 == 1 ? "SK_Veh_Preset_Ute_04" : "SK_Veh_Preset_Ute_03";
                    else if (offence == TrafficCarController.Offence.WrongWay)
                        model = i % 4 == 1 ? "SK_Veh_Preset_Sports_01" : "SK_Veh_Preset_Exotic_01";
                    else if (i == 8 && offence == TrafficCarController.Offence.None)
                        model = "SK_Veh_Preset_Truck_02";
                }

                // Manhattan runs cabs. An avenue without them reads as a generic
                // six-lane whatever the buildings look like, and a yellow sedan every
                // third car is the single cheapest thing that says New York. Only the
                // law-abiding standard cars become taxis: a cab that weaves, speeds or
                // drives the wrong way is a different game.
                var tint = palette[i % palette.Length];
                var isCab = biomeName == "MANHATTAN"
                            && role == TrafficCarController.VehicleRole.Standard
                            && offence == TrafficCarController.Offence.None
                            && i % 3 == 0;
                if (isCab)
                {
                    model = "SK_Veh_Preset_Sedan_01";
                    tint = TaxiYellow;
                    cabs++;
                }

                if (!isCab && role == TrafficCarController.VehicleRole.Standard && !model.Contains("Truck"))
                {
                    var rodinModel = NextRodinModel();
                    if (rodinModel != null)
                    {
                        model = rodinModel;
                        rodinSpawned++;
                    }
                }
                if (model.Contains("Ute_04")) brutes++;
                else if (model.Contains("Truck_02")) enforcers++;

                var spawned = CreateTrafficVehicle(trafficRoot, $"{(isCab ? "Taxi" : "Traffic Car")} {i + 1}",
                    model, tint, distances[i], lanes[i], speed, direction, false, 0f, offence, role);
                if (isCab) AddTaxiRoofSign(spawned);
            }

            Debug.Log($"RR_TRAFFIC spawned={trafficRoot.childCount} models={models.Length} " +
                      $"brutes={brutes} enforcers={enforcers} cabs={cabs} rodin={rodinSpawned}");
            // Accident scenes are a road-blocking pile of wrecks. On a single-lane road
            // (Greenwood) there is no room to pass one, so it walls the player in at ~420 m
            // and ends the run almost immediately. Only place them where there is room to
            // steer around the wreck.
            if (laneCount >= 2)
            {
                BuildAccidentScene(trafficRoot, startDistance + 420f, -1f, models[1], models[3]);
                BuildAccidentScene(trafficRoot, startDistance + 820f, 1f, models[0], models[4]);
            }
        }

        /// Cab yellow. Warmer and less green than the palette's amber, which is a car
        /// colour rather than a livery.
        private static readonly Color TaxiYellow = new Color(0.98f, 0.74f, 0.06f);

        /// The lit box on a cab's roof, which is most of what identifies one at a glance
        /// from behind - the shape reads before the colour does at night.
        ///
        /// Parented to the car so it turns and leans with it. Primitive destroys the
        /// collider it creates, which matters here more than anywhere else: an extra
        /// collider on a traffic car would reach the shared contact registry and start
        /// registering hits against a roof sign.
        private void AddTaxiRoofSign(TrafficCarController car)
        {
            if (car == null || !TryGetCombinedBounds(car.gameObject, out var bounds)) return;
            // Guarded rather than indexed. A missing palette entry throws, and a cab
            // losing its sign is not worth taking the run down for - the same domain
            // reload that empties this dictionary is already handled everywhere else by
            // rebuilding rather than by trusting it.
            if (!materials.TryGetValue("Taxi Sign", out var signMaterial)) return;
            var top = new Vector3(bounds.center.x, bounds.max.y + 0.12f, bounds.center.z);
            Primitive(PrimitiveType.Cube, "Taxi Roof Sign", top,
                new Vector3(0.62f, 0.24f, 0.26f), signMaterial, car.transform);
        }

        private TrafficCarController CreateTrafficVehicle(Transform parent, string name, string modelName,
            Color tint, float distance, float lane, float speed, float direction, bool wreck, float wreckYaw,
            TrafficCarController.Offence offence = TrafficCarController.Offence.None,
            TrafficCarController.VehicleRole role = TrafficCarController.VehicleRole.Standard)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);

            // Traffic used the Synthwave pack's decorative car props - flat, untextured
            // and visibly from another era than the player's vehicle. These are the same
            // Synty presets the hero car uses, with the same three material slots, so
            // traffic and player finally belong to one art set.
            var rodin = modelName.StartsWith(RodinModelPrefix) && BuildRodinVisual(root, name, modelName);
            var prefab = rodin ? null : Resources.Load<GameObject>($"Vehicles/{modelName}");
            if (prefab == null && !rodin) Debug.LogWarning($"RR_TRAFFIC missing prefab Vehicles/{modelName}");
            if (prefab != null)
            {
                // Liveries are BACK: full flat colour lost every detail and read as
                // toy blobs. Cars keep their livery decals again, while the smooth
                // normal weld and the removed emissive mask keep the flat-clean read
                // the player actually asked for (no facets, no yellow wheels).
                var liveries = new[]
                {
                    "Vehicles/PolygonStreetRacer_Texture_01_A",
                    "Vehicles/PolygonStreetRacer_Veh_Tex_07_Race_Yellow",
                    "Vehicles/PolygonStreetRacer_Veh_Tex_13_Race_Blue",
                    "Vehicles/PolygonStreetRacer_Veh_Tex_RR_Orange",
                    "Vehicles/PolygonStreetRacer_Veh_Tex_03_Carbon_Fibre",
                    "Vehicles/PolygonStreetRacer_Veh_Tex_24_Rust"
                };
                var chosenLiveryTex = Resources.Load<Texture2D>(liveries[Mathf.Abs(name.GetHashCode()) % liveries.Length])
                                      ?? materials["Street Racer Atlas"].mainTexture;

                var paint = new Material(materials["Street Racer Atlas"]) { name = $"{name} Paint" };
                if (chosenLiveryTex != null)
                {
                    paint.mainTexture = chosenLiveryTex;
                    if (paint.HasProperty("_BaseMap")) paint.SetTexture("_BaseMap", chosenLiveryTex);
                }
                paint.color = Color.white;
                if (paint.HasProperty("_BaseColor")) paint.SetColor("_BaseColor", Color.white);

                var visual = Instantiate(prefab, root);
                SmoothVehicleMeshes(visual);
                visual.name = $"{name} Visual";
                visual.transform.localPosition = Vector3.zero;
                // Source FBX is Z-up, same as the player preset.
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.transform.localScale = Vector3.one;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    var source = renderer.sharedMaterials;
                    var assigned = new Material[source.Length];
                    for (var i = 0; i < assigned.Length; i++)
                    {
                        var slot = source[i] != null ? source[i].name.ToLowerInvariant() : string.Empty;
                        if (slot.Contains("glass") || slot.Contains("window"))
                        {
                            assigned[i] = materials["Street Racer Glass"];
                        }
                        else if (slot.Contains("livery") || slot.Contains("paint") || slot.Contains("tex_16") || slot.Contains("texture_01") || i == 0)
                        {
                            assigned[i] = paint;
                        }
                        else
                        {
                            assigned[i] = materials["Street Racer Chassis"];
                        }
                    }
                    renderer.sharedMaterials = assigned;
                }
                // Wheels last: the slot pass above would overwrite their rubber/rim pair.
                ReplaceWheelMeshes(visual);
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) Destroy(collider);
                foreach (var l in visual.GetComponentsInChildren<Light>(true)) DestroyImmediate(l.gameObject == visual ? l : l.gameObject);
                foreach (var b in visual.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null) continue;
                    var typeName = b.GetType().Name;
                    if (typeName.Contains("Halo") || typeName.Contains("Flare") || typeName.Contains("LensFlare") || typeName.Contains("Light"))
                        DestroyImmediate(b);
                }
                NormalizeVehicleVisual(visual, VehicleLengthFor(modelName));
            }
            var controller = root.gameObject.AddComponent<TrafficCarController>();
            controller.Role = role;
            // Hull comes from the mesh, always. The hand-set role footprints that used
            // to override this described vehicles two to three times longer than what
            // was actually being drawn, which is the other half of the clipping: the
            // hull and the model were not the same object.
            if (TryGetLocalFootprint(root.gameObject, out var halfLength, out var halfWidth))
                controller.SetFootprint(halfLength, halfWidth);
            controller.Initialize(distance, lane, speed, direction, wreck, wreckYaw, offence);
            return controller;
        }

        /// Hull footprint measured in the vehicle root's own frame.
        ///
        /// The previous measurement read a world-space AABB (Renderer.bounds) taken
        /// before the car had been rotated onto the road, so "length" was whichever way
        /// the model happened to point inside its chunk - and the Synty presets import
        /// Z-up and are then rolled 90 degrees, which leaves the long axis across world
        /// Z on most of them. A 4.75 m car was registering as roughly 2 m long and
        /// 4.75 m wide, so the longitudinal reach the overlap test used was less than
        /// half of what it should have been: two cars had to bury a quarter of their
        /// length in one another before contact registered. That is the clipping.
        ///
        /// Measuring in local space fixes the axes, and since a road vehicle is always
        /// longer than it is wide the larger horizontal extent is the length regardless
        /// of which way a given FBX was authored.
        private static bool TryGetLocalFootprint(GameObject item, out float halfLength, out float halfWidth)
        {
            halfLength = 0f;
            halfWidth = 0f;
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            var toLocal = item.transform.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    var local = toLocal.MultiplyPoint3x4(world);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            var size = max - min;
            halfLength = Mathf.Max(size.x, size.z) * 0.5f;
            halfWidth = Mathf.Min(size.x, size.z) * 0.5f;
            return halfLength > 0.01f && halfWidth > 0.01f;
        }

        /// Kerb-weight lengths in metres, so a lorry is a lorry. Every traffic vehicle
        /// used to be normalised to a flat 4.75 m, which shrank the trucks to hatchback
        /// size on screen while their collision hulls stayed hand-set at 9.6 m and 9.0 m
        /// - a lorry reserved twice its own visible length of road and the traffic
        /// behind it braked for empty asphalt.
        private static float VehicleLengthFor(string modelName)
        {
            if (modelName.Contains("Motorbike")) return 2.15f;
            if (modelName.Contains("Hatch")) return 4.15f;
            if (modelName.Contains("Sports")) return 4.45f;
            if (modelName.Contains("Exotic")) return 4.55f;
            if (modelName.Contains("Sedan")) return 4.85f;
            if (modelName.Contains("Muscle")) return 5.05f;
            if (modelName.Contains("Ute")) return 5.45f;
            if (modelName.Contains("Truck")) return 9.20f;
            return 4.60f;
        }

        private static void NormalizeVehicleVisual(GameObject visual, float targetLength)
        {
            if (!TryGetCombinedBounds(visual, out var bounds)) return;
            var horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontalLength > 0.01f) visual.transform.localScale *= targetLength / horizontalLength;
            if (!TryGetCombinedBounds(visual, out bounds)) return;
            visual.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        private void BuildAccidentScene(Transform parent, float distance, float side, string firstModel, string secondModel)
        {
            var outerLane = side < 0f ? -6.75f : 6.75f;
            var shoulder = side < 0f ? -9.55f : 9.55f;
            CreateTrafficVehicle(parent, $"Accident Wreck {distance:0} A", firstModel, new Color(0.76f, 0.12f, 0.08f),
                distance, outerLane, 0f, side < 0f ? 1f : -1f, true, side * 24f);
            CreateTrafficVehicle(parent, $"Accident Wreck {distance:0} B", secondModel, new Color(0.18f, 0.24f, 0.30f),
                distance + 5.2f, shoulder, 0f, side < 0f ? 1f : -1f, true, -side * 32f);

            for (var i = 0; i < 4; i++)
            {
                var coneDistance = distance - 19f + i * 4.5f;
                var coneLane = Mathf.Lerp(side * 10.2f, outerLane, i / 3f);
                var cone = PrimitiveOnRoad(PrimitiveType.Cylinder, "Accident Warning Cone", coneDistance, coneLane, 0.34f,
                    new Vector3(0.20f, 0.34f, 0.20f), materials["Car Orange"], Vector3.zero, false);
                if (cone != null && parent != null) cone.transform.SetParent(parent, true);
            }
            CreateLocalLight(RoadPath.Point(distance + 1.5f, shoulder, 1.2f), new Color(1f, 0.24f, 0.06f), 8f, 12f);
        }

        private void BuildCamera()
        {
            foreach (var oldCamera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                DestroyImmediate(oldCamera.gameObject);
            }
            foreach (var oldListener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            {
                DestroyImmediate(oldListener);
            }

            var cameraObject = new GameObject("Cinematic Chase Camera");
            // Camera.main is null for untagged cameras; the HUD quarry markers and the
            // aftertouch director both resolve the chase camera through it.
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            var flareLayer = cameraObject.GetComponent("FlareLayer");
            if (flareLayer != null) DestroyImmediate(flareLayer);
            var urpCamData = cameraObject.GetComponent<UniversalAdditionalCameraData>()
                          ?? cameraObject.AddComponent<UniversalAdditionalCameraData>();
            urpCamData.renderPostProcessing = true;
            var mood = Mood();
            camera.fieldOfView = 64f;
            camera.nearClipPlane = 0.12f;
            camera.farClipPlane = 1400f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.Lerp(mood.Sky, mood.Equator, 0.35f);
            ApplySkyToCamera(camera);
            ApplyCityShotCameraPreset(camera);
            cameraObject.AddComponent<AudioListener>();
            var chase = cameraObject.AddComponent<ChaseCamera>();
            chase.target = car;
            chase.player = car.GetComponent<ArcadeCarController>();

            var takedownDirector = cameraObject.AddComponent<RoadRageTakedownDirector>();
            takedownDirector.BindCameraAndPlayer(camera, car);

            var aftertouchDirector = cameraObject.AddComponent<RoadRageAftertouchDirector>();
            aftertouchDirector.BindCameraAndPlayer(camera, car);

            var policeDirector = cameraObject.AddComponent<RoadRagePolicePursuitDirector>();
            policeDirector.BindPlayer(car, camera);

            var boostDirector = cameraObject.AddComponent<RoadRageBoostDirector>();
            boostDirector.BindPlayer(car, camera);

            var skidDirector = cameraObject.AddComponent<RoadRageSkidmarkDirector>();
            skidDirector.BindPlayer(car);

            cameraObject.AddComponent<RoadRageImpactShakeDirector>();


            var audioBridge = cameraObject.AddComponent<RoadRageAudioBridge>();

            var landingDirector = cameraObject.AddComponent<RoadRageLandingDirector>();
            var leaderboardDirector = cameraObject.AddComponent<RoadRageLeaderboardDirector>();

            if (reflectionProbe != null)
            {
                // Cubemap capture is axis-aligned regardless of transform rotation, so
                // parenting to the chase camera just keeps the probe near the player.
                reflectionProbe.transform.SetParent(cameraObject.transform, false);
                reflectionProbe.transform.localPosition = new Vector3(0f, 6f, 14f);
                cameraObject.AddComponent<ReflectionProbeDriver>().probe = reflectionProbe;
            }
        }

		private void ApplyCityShotCameraPreset(Camera camera)
		{
			var preset = CommandLineValue("-preset=");
			if (string.IsNullOrWhiteSpace(preset)) return;
			preset = preset.Trim().ToLowerInvariant();

			if (biomeName == Biomes[7] && preset == "brooklyn-shot")
			{
				camera.fieldOfView = 56f;
				camera.nearClipPlane = 0.10f;
				camera.farClipPlane = 650f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.06f, 0.08f, 0.1f);
			}
			else if (biomeName == Biomes[8] && preset == "manhattan-shot")
			{
				camera.fieldOfView = 60f;
				camera.nearClipPlane = 0.10f;
				camera.farClipPlane = 900f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.52f, 0.54f, 0.58f);
			}
		}
    }

    /// Drives the -shot= verification flag: let the world settle, capture, then exit.
    /// Headless verification of the run economy. The loop cannot be exercised by
    /// screenshots - this drives the state machine directly and logs measurements, per
    /// PRODUCTION-GATES.md section 3 step 2.
    public sealed class LoopSelfTest : MonoBehaviour
    {
        private System.Collections.IEnumerator Start()
        {
            yield return null;

            // The test banks cash, spins the wheel, spends gems and rolls the pass, so
            // it brackets itself: whatever it does, the player's save goes back exactly
            // as it was found. Without this, running the test costs a real profile.
            var savedProgress = GameState.Snapshot();

            var cashBefore = GameState.Cash;
            GameState.BeginRun();
            Debug.Log($"RR_TEST begin integrity={GameState.Integrity} runOver={GameState.RunOver} cash={cashBefore}");

            var violators = 0;
            var innocents = 0;
            foreach (var t in FindObjectsByType<TrafficCarController>(FindObjectsInactive.Exclude))
                if (t.IsViolator) violators++; else if (!t.IsWreck) innocents++;
            Debug.Log($"RR_TEST traffic violators={violators} innocents={innocents}");
            // How long does a run actually last, and what ends it?
            if (ArcadeCarController.CinematicPilot)
            {
                var pcar = FindAnyObjectByType<ArcadeCarController>();
                var t0 = Time.time;
                var startIntegrity = GameState.Integrity;
                var hits0 = GameState.InnocentsHit; var td0 = GameState.Takedowns;
                while (Time.time - t0 < 30f && GameState.Integrity > 0f)
                    yield return new WaitForSeconds(0.5f);
                Debug.Log($"RR_SURVIVE {(GameState.Integrity <= 0f ? "DIED" : "alive")} " +
                          $"after={Time.time - t0:0.0}s integrity={GameState.Integrity:0}/{startIntegrity:0} " +
                          $"takedowns={GameState.Takedowns - td0} innocents={GameState.InnocentsHit - hits0} " +
                          $"km={(pcar != null ? pcar.DistanceKm : 0f):0.00}");
            }
            // Measure the actual vehicle footprints against the 4.3 m collision radius.
            var pc = FindAnyObjectByType<ArcadeCarController>();
            if (pc != null && RoadRageBootstrap.TryGetCombinedBoundsPublic(pc.gameObject, out var pb))
                Debug.Log($"RR_SIZE player length={pb.size.z:0.0} width={pb.size.x:0.0}");
            foreach (var t in FindObjectsByType<TrafficCarController>(FindObjectsInactive.Exclude))
            {
                if (RoadRageBootstrap.TryGetCombinedBoundsPublic(t.gameObject, out var tb))
                {
                    Debug.Log($"RR_SIZE traffic length={tb.size.z:0.0} width={tb.size.x:0.0}");
                    break;
                }
            }
            // Anything sitting high above the road - the "objects in the sky".
            var carNow = FindAnyObjectByType<ArcadeCarController>();
            if (carNow != null)
            {
                var roadY = RoadPath.Center(carNow.RoadDistance).y;
                var seen = new System.Collections.Generic.Dictionary<string,int>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    var b = r.bounds;
                    if (b.min.y < roadY + 25f) continue;
                    if (Mathf.Abs(b.center.z - carNow.RoadDistance) > 400f) continue;
                    var key = $"{r.transform.root.name}/{r.name}";
                    seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
                }
                foreach (var kv in seen)
                    Debug.Log($"RR_SKY {kv.Key} x{kv.Value}");
                if (seen.Count == 0) Debug.Log("RR_SKY none above road+25m within 400m");

                // Widen: the ten highest renderers anywhere within 2.5 km.
                var all = new System.Collections.Generic.List<Renderer>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                    if (Mathf.Abs(r.bounds.center.z - carNow.RoadDistance) < 2500f) all.Add(r);
                all.Sort((a, b) => b.bounds.center.y.CompareTo(a.bounds.center.y));
                for (var i = 0; i < Mathf.Min(10, all.Count); i++)
                {
                    var r = all[i];
                    Debug.Log($"RR_HIGH '{r.transform.root.name}/{r.name}' " +
                              $"y={r.bounds.center.y - roadY:0} ahead={r.bounds.center.z - carNow.RoadDistance:0} " +
                              $"size={r.bounds.size:0}");
                }
            }
            // Does the autopilot actually hunt? Sample takedowns over 12 s.
            if (ArcadeCarController.CinematicPilot)
            {
                var t0 = GameState.Takedowns; var i0 = GameState.InnocentsHit;
                var d0 = FindAnyObjectByType<ArcadeCarController>().RoadDistance;
                yield return new WaitForSeconds(12f);
                var car2 = FindAnyObjectByType<ArcadeCarController>();
                Debug.Log($"RR_PILOT 12s: takedowns={GameState.Takedowns - t0} " +
                          $"innocents={GameState.InnocentsHit - i0} " +
                          $"metres={(car2 != null ? car2.RoadDistance - d0 : 0f):0}");
            }
            Debug.Log($"RR_TEST corridor pushed={RoadRageBootstrap.corridorPushed} " +
                      $"removed={RoadRageBootstrap.corridorRemoved}");
            var kept = RoadRageBootstrap.canopyKept;
            var culled = RoadRageBootstrap.canopyRejected;
            Debug.Log($"RR_TEST canopy kept={kept} rejected={culled} " +
                      $"({(kept + culled > 0 ? 100f * culled / (kept + culled) : 0f):0}% culled)");

            // Traffic motion: a screenshot cannot show whether cars drive or vibrate in
            // place. Sample each car's road distance over 2 s and report the spread.
            var cars = FindObjectsByType<TrafficCarController>(FindObjectsInactive.Exclude);
            var before = new float[cars.Length];
            for (var i = 0; i < cars.Length; i++) before[i] = cars[i].RoadDistance;
            yield return new WaitForSeconds(2f);
            var moved = 0;
            var stuck = 0;
            var totalDelta = 0f;
            for (var i = 0; i < cars.Length; i++)
            {
                if (cars[i] == null) continue;
                var d = Mathf.Abs(cars[i].RoadDistance - before[i]);
                totalDelta += d;
                if (d > 8f) moved++; else stuck++;
            }
            Debug.Log($"RR_STUCK t+2s  {TrafficCarController.StuckReport()}");
            yield return new WaitForSeconds(8f);
            Debug.Log($"RR_STUCK t+10s {TrafficCarController.StuckReport()}");
            TrafficCarController.DumpCars(FindAnyObjectByType<ArcadeCarController>().RoadDistance);
            Debug.Log($"RR_TEST motion over 2s: moving={moved} stuck={stuck} " +
                      $"avgMetres={(cars.Length > 0 ? totalDelta / cars.Length : 0f):0.0}");

            GameState.RunDistanceKm = 3.2f;
            GameState.Award(250, "TAKEDOWN");
            Debug.Log($"RR_TEST after takedown score={GameState.Score} combo={GameState.Combo}");

            GameState.ApplyDamage(26f);
            Debug.Log($"RR_TEST after innocent hit integrity={GameState.Integrity}");

            var ended = false;
            for (var i = 0; i < 20 && !ended; i++) ended = GameState.ApplyDamage(26f);
            Debug.Log($"RR_TEST runOver={GameState.RunOver} endedOnDamage={ended} " +
                      $"banked={GameState.LastRunCash} cashNow={GameState.Cash} delta={GameState.Cash - cashBefore}");

            // Lucky Wheel: a spin must consume exactly one spin and land on a real wedge.
            // Cash is not asserted - the upgrade and respin wedges pay nothing in cash.
            GameState.WheelSpins = 3;
            var spinIndex = GameState.SpinWheel();
            var wheelOk = spinIndex >= 0 && spinIndex < GameState.WheelPrizes.Length;
            Debug.Log($"RR_TEST wheel spin={spinIndex} prize={GameState.WheelPrizeName(spinIndex)} " +
                      $"spinsLeft={GameState.WheelSpins} charges={GameState.DoubleCharges}");

            // Set to zero rather than spinning down to it: the SPIN AGAIN wedge refunds
            // a spin, so a drain loop is not deterministic and would flake in CI.
            GameState.WheelSpins = 0;
            var refusedWhenEmpty = GameState.SpinWheel() < 0;
            Debug.Log($"RR_TEST wheel refusedWhenEmpty={refusedWhenEmpty} spinsLeft={GameState.WheelSpins}");

            // Adrenaline Revive: under deferred banking nothing is committed on a crash, so a
            // revive only charges the fee - cash, the daily counter and pass XP are untouched.
            // cashAtCrash/furyAtCrash equal Cash/FuryXp here because LastRunCash/Fury are 0
            // until CommitRun (which does not run on the revived crash).
            GameState.Cash += 50000;
            var cashAtCrash = GameState.Cash - GameState.LastRunCash;
            var furyAtCrash = GameState.FuryXp - GameState.LastRunFury;
            var reviveFee = GameState.ReviveCost;
            var revived = GameState.Revive();
            var reviveOk = revived && !GameState.RunOver && GameState.LastRunCash == 0
                           && GameState.Cash == cashAtCrash - reviveFee
                           && GameState.FuryXp == furyAtCrash;
            Debug.Log($"RR_TEST revive applied={revived} runOver={GameState.RunOver} " +
                      $"cash={GameState.Cash} expected={cashAtCrash - reviveFee} " +
                      $"fury={GameState.FuryXp} expected={furyAtCrash} integrity={GameState.Integrity:0.0}");

            // End it again and commit, so the doubler below has a fresh banked payout and the
            // pass gets its XP for the whole run. Under deferred banking EndRun only computes
            // the pending payout; CommitRun banks it - exactly as the HUD does the frame the
            // results screen takes over from the revive prompt.
            GameState.EndRun();
            GameState.CommitRun();
            var furyOk = GameState.LastRunFury > 0 && GameState.FuryXp >= GameState.LastRunFury
                         && GameState.FuryTier <= GameState.FuryTiers;
            Debug.Log($"RR_TEST fury run={GameState.LastRunFury} total={GameState.FuryXp} " +
                      $"tier={GameState.FuryTier}/{GameState.FuryTiers} " +
                      $"unclaimed={GameState.FuryUnclaimedCount()} pro={GameState.FuryPro} " +
                      $"daysLeft={GameState.FuryDaysLeft}");

            // Gems: earned only, and every sink refuses when short. Set explicitly rather
            // than read from PlayerPrefs so the check does not depend on the save file.
            GameState.Gems = GameState.GemSpinPrice;
            var spinsBeforeGem = GameState.WheelSpins;
            var gemSpin = GameState.BuySpinWithGems();
            var gemsOk = gemSpin && GameState.WheelSpins == spinsBeforeGem + 1 && GameState.Gems == 0
                         && !GameState.BuySpinWithGems() && GameState.WheelPaidToday == 0;
            Debug.Log($"RR_TEST gems spinBought={gemSpin} balance={GameState.Gems} " +
                      $"spins={GameState.WheelSpins} cashLadder={GameState.WheelPaidToday} " +
                      $"reviveDefault={GameState.ReviveDefaultPayment}");

            // Double earnings: pays the run's banked cash a second time, exactly once.
            GameState.DoubleCharges = 1;
            var runCash = GameState.LastRunCash;
            var beforeDouble = GameState.Cash;
            var doubled = GameState.DoubleEarnings();
            var doubledTwice = GameState.DoubleEarnings();
            var paid = GameState.Cash - beforeDouble;
            var doubleOk = doubled && !doubledTwice && runCash > 0 && paid == runCash
                           && GameState.LastRunCash == runCash * 2 && GameState.DoubleCharges == 0;
            Debug.Log($"RR_TEST double applied={doubled} secondAttempt={doubledTwice} " +
                      $"paid={paid} expected={runCash} banked={GameState.LastRunCash} charges={GameState.DoubleCharges}");

            Debug.Log(GameState.RunOver && GameState.Cash > cashBefore
                      && wheelOk && refusedWhenEmpty && reviveOk && furyOk && gemsOk && doubleOk
                ? "RR_TEST RESULT PASS"
                : "RR_TEST RESULT FAIL");

            GameState.RestoreSnapshot(savedProgress);
            Debug.Log($"RR_TEST save restored cash={GameState.Cash} gems={GameState.Gems} " +
                      $"spins={GameState.WheelSpins} furyXp={GameState.FuryXp} " +
                      $"tokens={GameState.ReviveTokens} charges={GameState.DoubleCharges}");

            Application.Quit();
        }
    }

    public sealed class BiomeScreenshot : MonoBehaviour
    {
        private string outputPath;
        private float elapsed;
        private bool captured;
        private int sampledFrames;
        private float sampledTime;
        private int warmupFrames;

        private static float CaptureAfterSeconds =>
            float.TryParse(RoadRageBootstrap.CommandLineValue("-runsec="), out var seconds) ? seconds : 3f;

        public void Initialize(string path)
        {
            outputPath = path;
            // Uncapped, otherwise every biome reports exactly the monitor's refresh rate
            // and the measurement says nothing about how much headroom is left.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            warmupFrames++;

            // Sample by frame count, not wall clock: the first frame builds the whole
            // world and compiles shaders (~2.7s), which would otherwise swallow a
            // time-based window entirely. The HUD's own FPS label is a single frame's
            // 1/deltaTime and the capture frame stalls, so it can't be trusted either.
            if (warmupFrames > 90 && sampledFrames < 150)
            {
                sampledFrames++;
                sampledTime += Time.unscaledDeltaTime;
            }

            if (!captured && warmupFrames > 240 && elapsed > CaptureAfterSeconds)
            {
                if (sampledFrames > 0)
                    Debug.Log($"RR_PERF frames={sampledFrames} avg={sampledTime / sampledFrames * 1000f:F2}ms " +
                              $"fps={sampledFrames / sampledTime:F1} score={GameState.Score} takedowns={GameState.Takedowns} " +
                              $"combo={GameState.Combo} dailyDist={GameState.Daily["distance"]:F2}");
                LogQuarryState();
                ScreenCapture.CaptureScreenshot(outputPath);
                captured = true;
            }
            else if (captured && elapsed > CaptureAfterSeconds + 2f) Application.Quit();
        }

        /// Measurement hook for the chase loop: how many rule-breakers are alive at
        /// capture time, how far the nearest one is, and whether it is fleeing.
        private static void LogQuarryState()
        {
            var violators = 0;
            var fleeing = 0;
            var runners = 0;
            var nearestGap = float.MaxValue;
            foreach (var traffic in TrafficCarController.All)
            {
                if (traffic == null || !traffic.IsViolator) continue;
                violators++;
                if (traffic.IsFleeing) fleeing++;
                if (traffic.IsHitAndRunner) runners++;
                var gap = traffic.GapToPlayer;
                if (gap >= 0f && gap < nearestGap) nearestGap = gap;
            }
            Debug.Log($"RR_QUARRY violators={violators} fleeing={fleeing} runners={runners} " +
                      $"nearestGap={(nearestGap == float.MaxValue ? -1f : nearestGap):0}m");
        }
    }

    public sealed class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public ArcadeCarController player;
        public static bool LogCamera;

        private const float Trail = 8.2f;
        private const float Rise = 4.7f;

        /// The camera used to fly wherever the smoothed follow put it, which meant it
        /// passed through building walls, tunnel sides and hillsides - three of nine
        /// playtest screenshots were the inside of geometry or a black screen.
        ///
        /// Scenery colliders are stripped when props spawn, so there is nothing to
        /// spherecast against. Instead the camera is constrained in ROAD space: it may
        /// never sit further from the centreline than the carriageway edge, and never
        /// below the road surface. Buildings are always outside RoadsideClearance, so
        /// staying inside it cannot intersect them.
        private Vector3 ConstrainToCorridor(Vector3 position)
        {
            if (player == null) return position;
            var distance = RoadPath.Wrap(player.RoadDistance - Trail);
            var centre = RoadPath.Center(distance);
            var right = RoadPath.Right(distance);

            // Correct the free position in place - do NOT rebuild it from the centreline.
            // Rebuilding discarded the follow's along-road offset and dropped the camera
            // to road level ahead of the car, which lost the car from frame entirely.
            var lateral = Vector3.Dot(position - centre, right);
            var limit = Mathf.Max(2f, RoadPath.HalfWidthAt(distance) - 1.5f);
            var clamped = Mathf.Clamp(lateral, -limit, limit);
            position += right * (clamped - lateral);
            // Height floor must follow the CAR, not the road behind it. On a climb the
            // road behind is lower, so a road-relative floor let the camera drop below
            // the crest and the hillside filled the screen ("no vision").
            var floor = Mathf.Max(centre.y + 2.4f, target.position.y + 1.9f);
            position.y = Mathf.Max(position.y, floor);
            return position;
        }

        private void LateUpdate()
        {
            if (RoadRageLandingDirector.Instance != null &&
                RoadRageLandingDirector.Instance.TryGetShowcaseCameraPose(target, out var showcasePos, out var showcaseRot))
            {
                transform.position = showcasePos;
                transform.rotation = showcaseRot;
                return;
            }

            if (RoadRageTakedownDirector.Instance != null &&
                RoadRageTakedownDirector.Instance.TryGetTakedownCameraPose(out var takedownPos, out var takedownRot))
            {
                transform.position = Vector3.Lerp(transform.position, takedownPos, Time.unscaledDeltaTime * 12f);
                transform.rotation = Quaternion.Slerp(transform.rotation, takedownRot, Time.unscaledDeltaTime * 12f);
                return;
            }

            if (target == null) return;
			var desired = target.position + target.up * Rise - target.forward * Trail;
            var next = (transform.position - desired).sqrMagnitude > 2500f
                ? desired
                : Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-7f * Time.deltaTime));
            
            var shakeOffset = Vector3.zero;
            var shakeRot = Quaternion.identity;
            if (RoadRageImpactShakeDirector.Instance != null)
            {
                shakeOffset = RoadRageImpactShakeDirector.Instance.CurrentShakeOffset;
                shakeRot = RoadRageImpactShakeDirector.Instance.CurrentShakeRotation;
            }

            transform.position = ConstrainToCorridor(next) + shakeOffset;
            if (LogCamera && player != null && Time.frameCount == 240)
            {
                // Name whatever is sitting in the camera's corridor.
                var dd0 = player.RoadDistance - Trail;
                var c0 = RoadPath.Center(dd0);
                var r0 = RoadPath.Right(dd0);
                foreach (var rend in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    var b = rend.bounds;
                    if ((b.center - transform.position).sqrMagnitude > 400f) continue;
                    if (b.size.magnitude < 4f) continue;
                    Debug.Log($"RR_NEAR '{rend.transform.root.name}/{rend.name}' " +
                              $"lat={Vector3.Dot(b.center - c0, r0):0.0} " +
                              $"size={b.size:0.0} dist={(b.center - transform.position).magnitude:0.0}");
                }
            }
            if (LogCamera && player != null && Time.frameCount % 30 == 0)
            {
                var dd = player.RoadDistance - Trail;
                var c = RoadPath.Center(dd);
                var r = RoadPath.Right(dd);
                Debug.Log($"RR_CAM lat={Vector3.Dot(transform.position - c, r):0.0} " +
                          $"limit={RoadPath.HalfWidthAt(dd) - 1.5f:0.0} " +
                          $"height={transform.position.y - c.y:0.0} " +
                          $"playerKm={player.RoadDistance / 1000f:0.00}");
            }
            var targetLook = Quaternion.LookRotation(target.position + target.up * 1.2f + target.forward * 9f - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetLook * shakeRot,
                1f - Mathf.Exp(-9f * Time.deltaTime));
        }
    }

    /// Keeps the local reflection probe on the player and schedules its captures.
    ///
    /// A capture is six extra scene renders, so this was never EveryFrame: at 85 km/h a
    /// ~4 Hz refresh is indistinguishable from a continuous one.
    ///
    /// Until 2026-09-18 nothing created the probe this was handed. The field was configured
    /// in six places, BuildReflectionProbe() had an empty body and had no call site either,
    /// and this driver was attached to the chase camera with a null - so its LateUpdate
    /// returned on the first line and every ReflectionProbeUsage.Simple renderer in the world
    /// sampled nothing.
    ///
    /// The schedule is tier-dependent because the cost is not: the rich tier captures four
    /// times a second, the low tier waits until the player has travelled far enough for the
    /// old capture to be visibly wrong and then captures once. Distance rather than time,
    /// because a stationary player gains nothing from a fresh capture and still pays for six
    /// renders. Both are logged, so the cost can be measured instead of argued about.
    public sealed class ReflectionProbeDriver : MonoBehaviour
    {
        public ReflectionProbe probe;
        /// Seconds between captures on the rich tier.
        public float interval = 0.25f;
        /// Metres the player must travel before the low tier recaptures. Distance, not time:
        /// a stationary player gains nothing from a new capture but still pays for six renders.
        public float lowTierTravelStep = 40f;

        private float nextRefresh;
        private Vector3 lastCapturePosition;
        private bool capturedOnce;
        private int captures;

        private void LateUpdate()
        {
            if (probe == null || !RoadRageBootstrap.ReflectionsEnabled) return;

            var position = transform.position;
            var rich = RoadRageBootstrap.RichDetailBudget;
            if (rich)
            {
                if (Time.time < nextRefresh) return;
                nextRefresh = Time.time + interval;
            }
            else
            {
                if (capturedOnce
                    && (position - lastCapturePosition).sqrMagnitude < lowTierTravelStep * lowTierTravelStep)
                    return;
            }

            lastCapturePosition = position;
            capturedOnce = true;
            probe.RenderProbe();
            captures++;
            // First capture plus a periodic heartbeat: enough to prove the schedule is
            // running from a player log without spamming it at four captures a second.
            if (captures == 1 || captures % 60 == 0)
                Debug.Log($"RR_REFLECT capture {captures} at {position.x:0},{position.z:0} " +
                          $"tier={(rich ? "rich" : "low")} res={probe.resolution}");
        }
    }

    /// <summary>
    /// Smoothly keeps the distant horizon mountain ring and sky dome centered on the camera
    /// so distant mountains and clouds are 100% static with zero popping and zero chunk rebuilds.
    /// </summary>
    /// Keeps the panorama sky's horizon the same colour as the fog. Zone transitions and
    /// weather move the fog colour at runtime; fogged mountains and ground then meet the
    /// sky without an edge.
    /// Destroys a mesh built at run time with the object that shows it. Unity does not
    /// free a runtime mesh when its GameObject goes, so each bent cliff piece would
    /// otherwise leak its mesh every time a chunk streamed out.
    public sealed class OwnedMesh : MonoBehaviour
    {
        public Mesh Mesh;

        private void OnDestroy()
        {
            if (Mesh != null) Destroy(Mesh);
        }
    }

    /// Keeps the showroom's lights in step with its camera (see EnsureShowroom).
    public sealed class ShowroomLightGate : MonoBehaviour
    {
        public Camera Camera;
        public Light[] Lights;

        private void LateUpdate()
        {
            var on = Camera != null && Camera.enabled;
            foreach (var light in Lights)
                if (light != null && light.enabled != on) light.enabled = on;
        }
    }

    public sealed class SkyHorizonSync : MonoBehaviour
    {
        private static readonly int HorizonColor = Shader.PropertyToID("_HorizonColor");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        /// Time-of-day colour over the panorama (RoadRageBootstrap.RollRunConditions).
        public static Color Tint = Color.white;

        private void LateUpdate()
        {
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(HorizonColor))
                sky.SetColor(HorizonColor, RenderSettings.fogColor);
            if (sky != null && sky.HasProperty(TintId))
                sky.SetColor(TintId, Tint);
        }
    }

    public sealed class GlobalHorizonFollower : MonoBehaviour
    {
        private Transform targetCamera;

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                if (Camera.main != null) targetCamera = Camera.main.transform;
            }
            if (targetCamera != null)
            {
                var p = targetCamera.position;
                transform.position = new Vector3(p.x, 0f, p.z);
            }
        }
    }
}
