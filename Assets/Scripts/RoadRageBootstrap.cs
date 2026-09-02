using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    public sealed class RoadRageBootstrap : MonoBehaviour
    {
        private const float RoadWidth = RoadPath.Width;
        private const float WorldLength = RoadPath.Length;

        private sealed class MaterialDict
        {
            private readonly Dictionary<string, Material> inner = new();
            private readonly RoadRageBootstrap owner;

            public MaterialDict(RoadRageBootstrap owner) => this.owner = owner;

            public Material this[string key]
            {
                get
                {
                    if (inner.TryGetValue(key, out var mat) && mat != null) return mat;
                    Debug.LogWarning($"[RoadRage] Material '{key}' was not found in dictionary, creating automatic fallback.");
                    var fallback = owner.MakeMaterial(key, new Color(0.6f, 0.5f, 0.4f));
                    inner[key] = fallback;
                    return fallback;
                }
                set => inner[key] = value;
            }

            public bool ContainsKey(string key) => inner.ContainsKey(key);
            public bool TryGetValue(string key, out Material mat) => inner.TryGetValue(key, out mat);
            public void Clear() => inner.Clear();
        }

        private readonly MaterialDict materials;
        public RoadRageBootstrap() => materials = new MaterialDict(this);
        private ReflectionProbe reflectionProbe;
        private Transform car;
		public static string requestedBiome;
        /// Indices the picker and journey currently expose.
        private static readonly int[] ActiveBiomes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        private static readonly string[] Biomes =
        {
            "GREENWOOD", "SNOW STATION", "SEWER TUNNEL", "TIRE DISTRICT",
            "ALIEN BIOMASS", "NEON CITY", "RED CANYON", "HONG KONG", "MANHATTAN",
            "HOLLYWOOD HILLS", "MIDNIGHT DOCKS", "VOLCANO PASS", "SALT FLATS", "STORM COAST"
        };
        private static readonly string[] ComingSoon = { "MIDNIGHT DOCKS", "VOLCANO PASS", "SALT FLATS", "STORM COAST" };
        private bool pickerSeen;
		private string biomeName;
		private WeatherKind activeWeather;
		private WeatherSystem weatherSystem;
		private float startDistance;
		public WeatherKind Weather => activeWeather;

		private static WeatherKind? ParseWeather(string value)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return value.ToLowerInvariant() switch
			{
				"rain" => WeatherKind.Rain,
				"storm" => WeatherKind.Storm,
				"snow" => WeatherKind.Snow,
				"clear" => WeatherKind.Clear,
				_ => null,
			};
	}
		public string BiomeName => biomeName;
		public Transform PlayerCar => car;
		public static IReadOnlyList<string> PlayableBiomes =>
			System.Array.ConvertAll(ActiveBiomes, i => Biomes[i]);
		public static IReadOnlyList<string> LockedBiomes => ComingSoon;
		public bool PickerOpen { get; private set; }

        public static RoadRageBootstrap Instance { get; private set; }

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

            foreach (var oldHud in FindObjectsByType<RoadRageHUD>(FindObjectsInactive.Include))
            {
                DestroyImmediate(oldHud);
            }

			biomeName = ResolveBiome();
			Time.timeScale = 1f;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            GameState.Load();
            GameState.RollDailyMissions();
            GameState.ResetRun();
            GameState.BeginRun();
            // -car=N forces a vehicle for verification captures without owning it.
            if (int.TryParse(CommandLineValue("-car="), out var forcedCar))
            {
                GameState.SelectedCar = Mathf.Clamp(forcedCar, 0, GameState.Cars.Length - 1);
                if (!GameState.OwnedCars.Contains(GameState.SelectedCar))
                    GameState.OwnedCars.Add(GameState.SelectedCar);
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
            ProfileChunks = HasCommandLineFlag("-profile");
            if (HasCommandLineFlag("-selftest")) gameObject.AddComponent<LoopSelfTest>();
            NoCanopy = HasCommandLineFlag("-nocanopy");
            LogSky = HasCommandLineFlag("-skylog");
            if (LogSky) StartCoroutine(SkyAudit());
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
            UpdateStreaming(startDistance);
            BuildCar();
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
			gameObject.AddComponent<RoadRageHUD>().Initialize(car.GetComponent<ArcadeCarController>(), this);
			if (HasCommandLineFlag("-picker"))
				OpenPicker();
			var screenshotPath = CommandLineValue("-shot=");
			if (!string.IsNullOrEmpty(screenshotPath))
				gameObject.AddComponent<BiomeScreenshot>().Initialize(screenshotPath);
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
				activeWeather = WeatherKind.Storm;
				if (!hadStartOverride) startDistance = 0f;
			}
		}

        private string ResolveBiome()
        {
           
            if (!string.IsNullOrEmpty(requestedBiome))
                return requestedBiome;

            var saved = PlayerPrefs.GetString("ROAD_RAGE_BIOME", "");
            if (!string.IsNullOrEmpty(saved))
                return saved;

            var requested = CommandLineValue("-biome=");
            if (string.IsNullOrEmpty(requested))
                return Biomes[0];

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
            if (value.Contains("midnight") || value.Contains("dock")) return Biomes[10];
            if (value.Contains("volcano") || value.Contains("pass")) return Biomes[11];
            if (value.Contains("salt") || value.Contains("flat")) return Biomes[12];
            if (value.Contains("storm") || value.Contains("coast")) return Biomes[13];

            return Biomes[0];  // DEFAULT
        }

        public void NextBiome()
		{
			var current = System.Array.IndexOf(Biomes, biomeName);
			ReloadBiome(Biomes[(current + 1) % Biomes.Length]);
		}

		public void OpenPicker()
		{
			PickerOpen = true;
			Time.timeScale = 0f;
			lastToggleTime = Time.unscaledTime;
		}

		public void ClosePicker()
		{
			pickerSeen = true;
			PickerOpen = false;
			Time.timeScale = 1f;
			lastToggleTime = Time.unscaledTime;
		}

		public void SelectBiome(string nextBiome)
		{
			ClosePicker();
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
            if (RoadRageRampDirector.Instance != null)
            {
                RoadRageRampDirector.Instance.ClearRamps();
            }

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
                if (n.Contains("Garage") || n.Contains("Biomass") || n.Contains("Accident") || n.Contains("Chunk") || n.Contains("StuntRamp"))
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

            // 7. Rebuild lighting for new biome
            BuildLighting();
            if (globalHorizonSky != null) Destroy(globalHorizonSky);
            EnsureGlobalHorizonSky(biomeIndex);

            // 8. Reset player car & pursuit
            if (RoadRagePolicePursuitDirector.Instance != null)
                RoadRagePolicePursuitDirector.Instance.ResetPursuit();
            GameState.Integrity = GameState.MaxIntegrity;
            if (car != null)
            {
                var controller = car.GetComponent<ArcadeCarController>();
                if (controller != null)
                {
                    controller.RoadDistance = startDistance + 5f;
                    controller.SpeedKph = 0f;
                    controller.TouchThrottle = 0f;
                    controller.TouchSteer = 0f;
                    controller.CountdownTimer = 3.2f;
                }
                car.position = RoadPath.Point(startDistance + 5f, 0f, 0.4f);
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

            // 11. Stream initial chunks for the new biome
            UpdateStreaming(startDistance);

            // 12. Close picker and restore timescale
            ClosePicker();
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
					BlendZoneLighting(controller.RoadDistance);
				}
			}

			// Escape / Start button / B key toggles picker with 350ms unscaled debounce
			if ((GameInput.GetEscapePressed() || GameInput.GetBKeyPressed()) && Time.unscaledTime - lastToggleTime > 0.35f)
			{
				lastToggleTime = Time.unscaledTime;
				if (PickerOpen) ClosePicker();
				else OpenPicker();
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

        private Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// How far a surface tint is pulled towards neutral. Desaturating the lighting
        /// alone was not enough: the world geometry carries its own colour, and a biome
        /// like Alien Biomass is mostly violet organics and violet rock, so it still read
        /// as a single hue under a neutral key. BiomeMaterial funnels through here, so
        /// this is the one place every generated surface tint passes.
        private const float SurfaceDesaturation = 0.5f;

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

        private Texture2D BiomeTexture(string pack, string name) =>
            Resources.Load<Texture2D>($"Biomes/{pack}/Textures/{name}");

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
			var ground = BiomeSurface(BiomeMaterial("Forest Floor PBR", "RunicForest",
				"T_ground_02_D", "T_ground_02_N", new Color(0.62f, 0.60f, 0.48f), 0f, 0.06f),
				"RunicForest", "T_ground_02_MSO", 0.35f);
            // Tighter tiling: at the old scale the ground read as one flat wash across the
            // whole 240m plane instead of ground the player is moving over.
            ground.mainTextureScale = new Vector2(90f, 420f);

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
                new Color(0.30f, 0.32f, 0.35f), 0.03f, 0.22f), "Shared", "T_asphalt_MSO", 0.55f);
            road.mainTextureScale = new Vector2(4.5f, 3.2f);
            var shoulder = BiomeSurface(BiomeMaterial("Shoulder", "Shared", "T_asphalt_D", "T_asphalt_N",
                new Color(0.22f, 0.24f, 0.24f), 0f, 0.1f), "Shared", "T_asphalt_MSO", 0.45f);
            shoulder.mainTextureScale = new Vector2(1.4f, 3.2f);
            MakeMaterial("White Paint", new Color(0.92f, 0.94f, 0.9f), 0f, 0.3f);
            MakeMaterial("Yellow Paint", new Color(1f, 0.66f, 0.06f), 0f, 0.25f);
            MakeMaterial("Car Orange", new Color(0.95f, 0.22f, 0.035f), 0.55f, 0.78f);
            MakeMaterial("Car Dark", new Color(0.012f, 0.018f, 0.022f), 0.25f, 0.55f);
            MakeMaterial("Glass", new Color(0.025f, 0.12f, 0.16f), 0.7f, 0.92f);
            MakeMaterial("Tire", new Color(0.012f, 0.012f, 0.014f), 0f, 0.08f);
            MakeMaterial("Driver Skin", new Color(0.72f, 0.43f, 0.28f), 0f, 0.32f);
            MakeMaterial("Driver Jacket", new Color(0.08f, 0.11f, 0.16f), 0.12f, 0.38f);
            MakeMaterial("Driver Hair", new Color(0.035f, 0.022f, 0.018f), 0f, 0.16f);
            MakeMaterial("Low Bark", new Color(0.18f, 0.12f, 0.075f), 0f, 0.12f);
            MakeMaterial("Low Leaf", new Color(0.09f, 0.31f, 0.12f), 0f, 0.08f);
            MakeMaterial("Sidewalk", new Color(0.24f, 0.25f, 0.29f), 0.08f, 0.34f);
            MakeMaterial("City Neon", new Color(0.17f, 0.45f, 0.72f), 0.48f, 0.72f);

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
            MakeMaterial("Industrial Ground", new Color(0.105f, 0.095f, 0.082f), 0.08f, 0.32f);
            BiomeMaterial("Demo Facades", "DemoCity", "building_facades", "building_facades_nm", Color.white, 0.15f, 0.42f);
            BiomeMaterial("Demo Highrise", "DemoCity", "highrise_facades", "highrise_facades_nm", Color.white, 0.18f, 0.55f, "highrise_facades_em");
            BiomeMaterial("Demo Bases", "DemoCity", "building_bases", "building_bases_nm", Color.white, 0.12f, 0.38f);
            BiomeMaterial("Demo Windows", "DemoCity", "building_windows_wet", "building_windows_wet_nm", new Color(0.85f, 0.92f, 1f), 0.35f, 0.85f);
            BiomeMaterial("Demo Interior", "DemoCity", "building_interior", "building_interior_nm", Color.white, 0.15f, 0.40f);
            BiomeMaterial("Demo Props", "DemoCity", "props_main", "props_main_nm", Color.white, 0.45f, 0.50f);
            BiomeMaterial("Demo Fence", "DemoCity", "road_sideway_fences", "road_sideway_fences_nm", Color.white, 0.65f, 0.45f);
            BiomeSurface(BiomeMaterial("City Concrete", "Synthwave", "T_concrete_D", "T_concrete_N", new Color(0.42f, 0.46f, 0.54f), 0.18f, 0.42f), "Synthwave", "T_concrete_MSO");
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
            BiomeSurface(BiomeMaterial("City Palm", "Synthwave", "T_palm_tree_D", "T_palm_tree_N", Color.white, 0.1f, 0.35f), "Synthwave", "T_palm_tree_MSO");
            materials["Palm Frond"] = materials["City Palm"];
            MakeMaterial("City Asphalt Trim", new Color(0.10f, 0.10f, 0.13f), 0.2f, 0.44f);

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
                "T_vetegation_atlas_normal", new Color(0.68f, 0.82f, 0.54f), 0.34f);
            BiomeCutoutMaterial("Forest Flowers", "RunicForest", "T_flowers_D", "T_flowers_N",
                new Color(0.86f, 0.86f, 0.70f), 0.36f);
            BiomeSurface(BiomeMaterial("Forest Pebble", "RunicForest", "T_small_rock_D", "T_small_rock_N",
                new Color(0.62f, 0.62f, 0.58f), 0f, 0.2f), "RunicForest", "T_small_rock_MSO", 0.6f);

            BiomeCutoutMaterial("Forest Bush", "ForestVillage", "T_bush_D", "T_bush_N",
                new Color(0.60f, 0.74f, 0.48f), 0.36f);
            BiomeCutoutMaterial("Forest Fern", "ForestVillage", "T_plant_D", "T_plant_N",
                new Color(0.62f, 0.80f, 0.50f), 0.34f);
            BiomeSurface(BiomeMaterial("Forest Roots", "ForestVillage", "T_roots_D", "T_roots_N",
                new Color(0.56f, 0.48f, 0.40f), 0f, 0.18f), "ForestVillage", "T_roots_MSO", 0.5f);
            BiomeSurface(BiomeMaterial("Forest Boulder", "ForestVillage", "T_rock_01_D", "T_rock_01_N",
                new Color(0.60f, 0.60f, 0.56f), 0f, 0.2f), "ForestVillage", "T_rock_01_MSO", 0.6f);
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
        
        private static void ApplyPlatformQuality()
        {
            if (!Application.isMobilePlatform)
            {
                QualitySettings.shadowDistance = 160f;
                QualitySettings.shadowCascades = 4;
                QualitySettings.shadows = UnityEngine.ShadowQuality.All;
                return;
            }

            QualitySettings.shadowDistance = 70f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Low;
            QualitySettings.globalTextureMipmapLimit = 0;
            Application.targetFrameRate = 60;
            Debug.Log("RR_QUALITY mobile tier: no reflection probe, 70m 1-cascade shadows, 1024 textures");
        }

        /// Wet asphalt is mostly a smoothness trick: raise road/shoulder gloss so the
        /// probe's reflection reads, and darken the albedo the way real water does.
        private void ApplyRoadWetness(float wetness)
        {
            if (wetness <= 0.001f) return;
            foreach (var name in new[] { "Road", "Shoulder" })
            {
                if (!materials.TryGetValue(name, out var material)) continue;
                var dry = name == "Road" ? 0.25f : 0.15f;
                material.SetFloat("_Smoothness", Mathf.Lerp(dry, 0.40f, wetness));
                material.SetColor("_BaseColor", material.GetColor("_BaseColor") * Mathf.Lerp(1f, 0.82f, wetness));
            }
        }

        private void BuildReflectionProbe()
        {
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
            RenderSettings.ambientSkyColor = Color.Lerp(mood.Sky, weather.FogTint, weather.FogTintAmount * 0.6f);
            RenderSettings.ambientEquatorColor = Color.Lerp(mood.Equator, weather.FogTint, weather.FogTintAmount * 0.4f);
            RenderSettings.ambientGroundColor = mood.Ground;
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;

            ApplyRoadWetness(Mathf.Clamp01(mood.RoadWetness + weather.WetnessAdd));
            ApplyPlatformQuality();

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
			var bloom = volume.profile.Add<Bloom>();
			bloom.intensity.Override(0f);
			bloom.active = false;
			// Without tonemapping every HDR highlight clips flat, which is a large part
			// of the "plastic toy" read. ACES gives filmic rolloff on the bright end.
			// ACES rolls the highlights off filmically but it also shifts hue and lifts
			// saturation, which is a large part of the over-cooked look. Neutral does the
			// range remap only and leaves the grading to ColorAdjustments below.
			var tonemap = volume.profile.Add<Tonemapping>();
			tonemap.mode.Override(TonemappingMode.Neutral);

			// Slight motion blur sells speed and hides the low-poly silhouettes.
			var motionBlur = volume.profile.Add<MotionBlur>();
			motionBlur.intensity.Override(0.18f);
			motionBlur.clamp.Override(0.04f);

			var color = volume.profile.Add<ColorAdjustments>();
			color.postExposure.Override(mood.PostExposure + weather.ExposureAdd);
			// Contrast and saturation were stacked on top of an ACES curve that already
			// pushes both, so every biome graded out as a poster. Neutral tonemapping
			// leaves hue and saturation alone, and the grade now only takes colour away.
			color.contrast.Override(3.5f);
			color.saturation.Override(GradeSaturation);
			var vignette = volume.profile.Add<Vignette>();
			vignette.intensity.Override(0.20f);
			vignette.smoothness.Override(0.68f);
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
        }

        /// How far every biome palette is pulled towards its own luminance. The moods
        /// were authored as near-pure hues (a violet Neon City sky at 0.32/0.16/0.52, a
        /// bottle-green sewer, a magenta Alien Biomass) and those colours multiply into
        /// ambient, fog and the sun, so every surface in the zone inherited the cast.
        /// Pulling them most of the way to neutral keeps each biome's identity readable
        /// while taking the poster-paint saturation out of the frame.
        private const float MoodDesaturation = 0.62f;
        /// Ground bounce carries the strongest cast because it lights the underside of
        /// every car, so it loses slightly more than the sky does.
        private const float GroundDesaturation = 0.72f;
        /// Final global trim in the colour grade, on top of the neutral tonemapper.
        private const float GradeSaturation = -16f;

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
            mood.Fog = Desaturate(mood.Fog, MoodDesaturation);
            mood.Sky = Desaturate(mood.Sky, MoodDesaturation);
            mood.Equator = Desaturate(mood.Equator, MoodDesaturation);
            mood.Ground = Desaturate(mood.Ground, GroundDesaturation);
            // Key light keeps more of its warmth than the ambient does - a fully neutral
            // sun flattens the shading, and a tinted key reads as time of day, not as
            // a colour filter over the whole frame.
            mood.SunColor = Desaturate(mood.SunColor, MoodDesaturation * 0.55f);
            return mood;
        }

        private BiomeMood Mood() => Mood(System.Array.IndexOf(Biomes, biomeName));

        private BiomeMood Mood(int biomeIndex) => Neutralize(RawMood(biomeIndex));

        /// Authored palettes. Read these through Mood() so the neutral pass is never
        /// bypassed; this is only separate so the per-biome values stay editable.
        private static BiomeMood RawMood(int biomeIndex) => biomeIndex switch
        {
            1 => new BiomeMood // SNOW STATION
            {
                FogDensity = 0.0045f, Fog = new Color(0.62f, 0.75f, 0.86f),
                Sky = new Color(0.68f, 0.82f, 0.96f), Equator = new Color(0.48f, 0.60f, 0.72f),
                Ground = new Color(0.28f, 0.36f, 0.44f), SunColor = new Color(0.92f, 0.96f, 1f),
                SunIntensity = 1.18f, PostExposure = -0.18f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f
            },
            2 => new BiomeMood // SEWER TUNNEL
            {
                FogDensity = 0.014f, Fog = new Color(0.05f, 0.10f, 0.08f),
                Sky = new Color(0.13f, 0.26f, 0.19f), Equator = new Color(0.10f, 0.20f, 0.14f),
                Ground = new Color(0.04f, 0.08f, 0.06f), SunColor = new Color(0.85f, 0.92f, 0.88f),
                SunIntensity = 0.62f, PostExposure = 0.34f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.55f
            },
            3 => new BiomeMood // TIRE DISTRICT
            {
                FogDensity = 0.0065f, Fog = new Color(0.38f, 0.38f, 0.40f),
                Sky = new Color(0.52f, 0.56f, 0.62f), Equator = new Color(0.32f, 0.35f, 0.38f),
                Ground = new Color(0.14f, 0.14f, 0.14f), SunColor = new Color(0.95f, 0.95f, 0.95f),
                SunIntensity = 1.35f, PostExposure = 0.35f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.3f
            },
            4 => new BiomeMood // ALIEN BIOMASS
            {
                FogDensity = 0.015f, Fog = new Color(0.13f, 0.05f, 0.17f),
                Sky = new Color(0.20f, 0.07f, 0.28f), Equator = new Color(0.09f, 0.20f, 0.14f),
                Ground = new Color(0.04f, 0.07f, 0.05f), SunColor = new Color(0.72f, 0.55f, 1f),
                SunIntensity = 0.95f, PostExposure = 0.30f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.22f
            },
            5 => new BiomeMood // NEON CITY
            {
                FogDensity = 0.0035f, Fog = new Color(0.12f, 0.08f, 0.24f),
                Sky = new Color(0.32f, 0.16f, 0.52f), Equator = new Color(0.38f, 0.18f, 0.46f),
                Ground = new Color(0.14f, 0.08f, 0.22f), SunColor = new Color(0.85f, 0.70f, 1f),
                SunIntensity = 1.25f, PostExposure = 0.35f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.65f
            },
            6 => new BiomeMood // RED CANYON
            {
                FogDensity = 0.0038f, Fog = new Color(0.68f, 0.65f, 0.62f),
                Sky = new Color(0.72f, 0.78f, 0.88f), Equator = new Color(0.55f, 0.48f, 0.42f),
                Ground = new Color(0.24f, 0.18f, 0.14f), SunColor = new Color(1f, 0.98f, 0.92f),
                SunIntensity = 1.50f, PostExposure = 0.16f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f
            },
            7 => new BiomeMood // BROOKLYN
            {
                FogDensity = 0.0075f, Fog = new Color(0.30f, 0.43f, 0.55f),
                Sky = new Color(0.41f, 0.60f, 0.78f), Equator = new Color(0.23f, 0.34f, 0.43f),
                Ground = new Color(0.16f, 0.18f, 0.16f), SunColor = new Color(0.98f, 0.98f, 0.95f),
                SunIntensity = 1.40f, PostExposure = 0.20f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.08f
            },
            9 => new BiomeMood // HOLLYWOOD HILLS - Crisp California daylight with blue skies
            {
                FogDensity = 0.0015f, Fog = new Color(0.75f, 0.85f, 0.95f),
                Sky = new Color(0.60f, 0.78f, 0.98f), Equator = new Color(0.65f, 0.72f, 0.78f),
                Ground = new Color(0.35f, 0.35f, 0.35f), SunColor = new Color(1f, 1f, 1f),
                SunIntensity = 1.45f, PostExposure = 0.15f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.0f
            },
            8 => new BiomeMood // MANHATTAN
            {
                FogDensity = 0.009f, Fog = new Color(0.055f, 0.086f, 0.13f),
                Sky = new Color(0.09f, 0.12f, 0.23f), Equator = new Color(0.09f, 0.16f, 0.29f),
                Ground = new Color(0.04f, 0.05f, 0.08f), SunColor = new Color(0.65f, 0.75f, 1f),
                SunIntensity = 0.9f, PostExposure = 0.12f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.62f
            },
            _ => new BiomeMood // GREENWOOD
            {
                FogDensity = 0.0065f, Fog = new Color(0.33f, 0.47f, 0.43f),
                Sky = new Color(0.40f, 0.55f, 0.62f), Equator = new Color(0.20f, 0.34f, 0.28f),
                Ground = new Color(0.075f, 0.11f, 0.075f), SunColor = new Color(0.98f, 0.98f, 0.95f),
                SunIntensity = 1.40f, PostExposure = 0.12f, BloomIntensity = 0f, BloomThreshold = 5f, RoadWetness = 0.1f
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
            var groundName = GroundNameFor(biomeIndex);
            var isCity = biomeIndex == 5 || biomeIndex == 7 || biomeIndex == 8 || biomeIndex == 3 || biomeIndex == 9;
            if (isCity)
            {
                // Flanking solid foundations on left and right sides (leaves central highway 100% clean with zero z-fighting)
                BuildRibbon($"Left {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    -150f, -1.0f, -0.02f, materials[groundName], sampleStep: 6f, displace: 0f, relative: true);
                BuildRibbon($"Right {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    1.0f, 150f, -0.02f, materials[groundName], sampleStep: 6f, displace: 0f, relative: true);
            }
            else
            {
                BuildRibbon($"Left {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    -150f, -1.0f, -0.05f, materials[groundName], sampleStep: 5f, displace: 4.5f, lateralSegments: 20, relative: true);
                BuildRibbon($"Right {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} Ground",
                    1.0f, 150f, -0.05f, materials[groundName], sampleStep: 5f, displace: 4.5f, lateralSegments: 20, relative: true);
            }
            // Main Asphalt Highway
            EnableProbeReflections(BuildRibbon("Curved Asphalt Highway", -1f, 1f, 0.02f, materials["Road"], relative: true));

            // Road Edge & Terrain Integration per Biome Type:
            var hasCityCurbs = biomeIndex == 5 || biomeIndex == 7 || biomeIndex == 8 || biomeIndex == 3;
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
                BuildRibbon("Left Forest Verge", -1.55f, -1.0f, 0.02f, materials["Forest Floor PBR"], relative: true);
                BuildRibbon("Right Forest Verge", 1.0f, 1.55f, 0.02f, materials["Forest Floor PBR"], relative: true);
                BuildRibbon("Left Forest Grass Stripe", -1.35f, -1.0f, 0.045f, materials["Forest Grass"], relative: true);
                BuildRibbon("Right Forest Grass Stripe", 1.0f, 1.35f, 0.045f, materials["Forest Grass"], relative: true);
            }
            else
            {
                EnableProbeReflections(BuildRibbon("Left Curved Shoulder", -1.20f, -1f, 0.035f, materials["Shoulder"], relative: true));
                EnableProbeReflections(BuildRibbon("Right Curved Shoulder", 1f, 1.20f, 0.035f, materials["Shoulder"], relative: true));
            }

            // Road Paint & Lane Striping
            BuildRibbon("Center Yellow L", -0.22f, -0.10f, 0.038f, materials["Yellow Paint"]);
            BuildRibbon("Center Yellow R", 0.10f, 0.22f, 0.038f, materials["Yellow Paint"]);
            BuildRibbon("Left Edge Line", -0.96f, -0.90f, 0.038f, materials["White Paint"], relative: true);
            BuildRibbon("Right Edge Line", 0.90f, 0.96f, 0.038f, materials["White Paint"], relative: true);

            var lanes = LaneCountFor(biomeIndex);
            if (lanes == 2)
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
        private GameObject BuildRibbon(string name, float leftLateral, float rightLateral, float height,
            Material material, float start = float.NaN, float end = float.NaN, float sampleStep = 6f,
            bool relative = false, float displace = 0f, int lateralSegments = 1)
        {
            // NaN means "this segment": ribbons are rebuilt per streamed chunk.
            if (float.IsNaN(start)) start = segStart - 2f;
            if (float.IsNaN(end)) end = segEnd + 2f;
            if (displace > 0f) lateralSegments = Mathf.Max(lateralSegments, 10);
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
                for (var j = 0; j < across; j++)
                {
                    var f = across == 1 ? 0f : j / (float)(across - 1);
                    var lateral = Mathf.Lerp(leftLateral, rightLateral, f) * scale;
                    var lift = 0f;
                    if (displace > 0f)
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
                            Mathf.InverseLerp(RoadPath.ClearanceAt(distance) + 6f,
                                RoadPath.ClearanceAt(distance) + 32f, Mathf.Abs(lateral)));
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
            return CreateMeshObject(name, vertices, triangles, uv, material, colors);
        }

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
                        var skylinePass = material != null && material.name == "Cyber Skyline";
                        if (sourceName.Contains("hologram") || sourceName.Contains("sign") || sourceName.Contains("light") || sourceName.Contains("lamp") || sourceName.Contains("farola")) assigned[i] = materials["City Neon"];
                        else if (sourceName.Contains("billboard") || sourceName.Contains("panel")) assigned[i] = materials["City Billboard"];
                        else if (sourceName.Contains("streetlamp") || sourceName.Contains("pole") || sourceName.Contains("post")) assigned[i] = materials["City Asphalt Trim"];
                        else if (sourceName.Contains("fireplug") || sourceName.Contains("hydrant")) assigned[i] = materials["Car Orange"];
                        else if (sourceName.Contains("window_car") || sourceName.Contains("glass")) assigned[i] = materials["Glass"];
                        else if (sourceName.Contains("window") || sourceName.Contains("interior_light")) assigned[i] = materials["City Windows"];
                        else if (sourceName.Contains("trim") || sourceName.Contains("metal") || sourceName.Contains("roof") || sourceName.Contains("tejad")) assigned[i] = materials["City Asphalt Trim"];
                        else if (sourceName.Contains("concrete") || sourceName.Contains("concrate") || sourceName.Contains("brick") || sourceName.Contains("plaster") || sourceName.Contains("highrise") || sourceName.Contains("build")) assigned[i] = skylinePass ? material : materials["City Concrete"];
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
            model.transform.position = RoadPath.Point(distance, lateral, height);
            model.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(localEuler);
            model.transform.localScale = scale;
            if (enforceClearance && Mathf.Abs(lateral) > 0.01f)
                EnsureOutsideRoad(model, distance, Mathf.Sign(lateral));
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
        private static void NormalizeModelHeight(GameObject model, float targetHeight, float groundHeight = 0.05f)
        {
            if (!TryGetCombinedBounds(model, out var bounds) || bounds.size.y < 0.01f) return;
            var groundY = model.transform.position.y;
            model.transform.localScale *= targetHeight / bounds.size.y;
            if (!TryGetCombinedBounds(model, out bounds)) return;
            model.transform.position += Vector3.up * (groundY - bounds.min.y + groundHeight);
            var dist = bounds.center.z;
            var roadRight = RoadPath.Right(dist);
            var roadCenter = RoadPath.Point(dist, 0f, 0f);
            var proj = Vector3.Dot(bounds.center - roadCenter, roadRight);
            if (Mathf.Abs(proj) > 0.01f)
                EnsureOutsideRoad(model, dist, Mathf.Sign(proj));
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
            }

            if (showroomCar == carIndex) return;
            showroomCar = carIndex;
            for (var i = showroomStage.childCount - 1; i >= 0; i--)
                Destroy(showroomStage.GetChild(i).gameObject);

            var spec = GameState.Cars[Mathf.Clamp(carIndex, 0, GameState.Cars.Length - 1)];
            var prefab = Resources.Load<GameObject>($"Vehicles/{spec.Mesh}");
            if (prefab == null) return;

            var paint = new Material(materials["Street Racer Atlas"]) { name = "Showroom Paint" };
            var livery = Resources.Load<Texture2D>($"Vehicles/{spec.Livery}");
            if (livery != null)
            {
                paint.mainTexture = livery;
                if (paint.HasProperty("_BaseMap")) paint.SetTexture("_BaseMap", livery);
            }

            var visual = Instantiate(prefab, showroomStage);
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
                        : materials["Street Racer Chassis"];
                }
                r.sharedMaterials = assigned;
            }
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
            // Frame every vehicle the same: bikes and semis differ by 4x in length.
            if (TryGetCombinedBounds(visual, out var b) && b.size.magnitude > 0.01f)
            {
                var longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                visual.transform.localScale *= 6.0f / Mathf.Max(0.01f, longest);
                if (TryGetCombinedBounds(visual, out b))
                    visual.transform.localPosition -= new Vector3(0f, b.min.y - showroomStage.position.y, 0f);
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
        private static int LaneCountFor(int biomeIndex) => biomeIndex switch
        {
            0 => 1,  // GREENWOOD  - country road
            6 => 1,  // RED CANYON - desert two-lane
            9 => 2,  // HOLLYWOOD  - hillside road; one lane each way left no room to pass
            2 => 2,  // SEWER      - tunnel, narrower than a highway
            1 => 2,  // SNOW       - remote highway
            _ => 3,  // cities
        };

        private static float HalfWidthFor(int biomeIndex) =>
            LaneCountFor(biomeIndex) * RoadPath.LaneWidth;

        /// Smoothly interpolated so the carriageway tapers across a zone seam. The taper
        /// straddles the boundary, which is also where the gateway stands.
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

        public int BiomeIndexAt(float distance)
        {
            var order = (journeyStart + ZoneIndexAt(distance)) % JourneyOrder.Length;
            return JourneyOrder[order];
        }

        public string BiomeNameAt(float distance) => Biomes[BiomeIndexAt(distance)];

        /// -profile logs per-chunk render cost; -nocanopy skips the forest canopy bands
        /// so overdraw can be separated from raw geometry cost. Diagnostics only.
        private static bool ProfileChunks;
        private static bool NoCanopy;
        private static bool LogSky;

        private void BuildChunk(int index)
        {
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
                        if (m != null && m.IsKeywordEnabled("_ALPHATEST_ON")) { cutouts++; break; }
                }
                Debug.Log($"RR_COST {Biomes[Mathf.Clamp(biomeIndex, 0, Biomes.Length - 1)]} " +
                          $"seg={segStart:0}: renderers={renderers.Length} tris={tris} cutoutRenderers={cutouts}");
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
            RenderSettings.ambientSkyColor = Color.Lerp(here.Sky, weather.FogTint, weather.FogTintAmount * 0.6f);
            RenderSettings.ambientEquatorColor = Color.Lerp(here.Equator, weather.FogTint, weather.FogTintAmount * 0.4f);
            RenderSettings.ambientGroundColor = here.Ground;
            if (sunLight != null)
            {
                sunLight.color = here.SunColor;
                sunLight.intensity = here.SunIntensity * weather.SunScale;
            }
            ApplyRoadWetness(Mathf.Clamp01(here.RoadWetness + weather.WetnessAdd));
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
                default: BuildForest(); break;
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
            BuildWallRibbon("Sewer Left Curved Wall", -13.4f, -0.05f, 10.2f, materials["Sewer Concrete"]);
            BuildWallRibbon("Sewer Right Curved Wall", 13.4f, -0.05f, 10.2f, materials["Sewer Concrete"]);
            BuildRibbon("Sewer Curved Ceiling", -13.6f, 13.6f, 10.2f, materials["Sewer Concrete"], -45f, WorldLength + 45f, 7f);

            // Wall furniture mounted safely flush against tunnel walls (zero lane intrusion)
            ScatterBand(10f, 12.8f, 13.3f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_pipe_03", materials["Sewer Pipe"],
                    d, l, Random.Range(2.5f, 6.5f), new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one * Random.Range(0.6f, 0.95f), "Wall Pipe", true));
            ScatterBand(18f, 12.9f, 13.4f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_pillar", materials["Sewer Concrete"],
                    d, l, 0f, new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one * Random.Range(0.85f, 1.15f), "Tunnel Pillar", true));
            ScatterBand(34f, 13.0f, 13.5f, (d, l, s) =>
                PlaceBiomeModelOnRoad("Sewers", "SM_arch", materials["Sewer Concrete"],
                    d, l, 0f, new Vector3(0f, s > 0 ? -90f : 90f, 0f),
                    Vector3.one, "Side Arch", true));

            for (var z = SegBegin(-12f, 18f); z < segEnd; z += 18f)
            {
                if (((int)(z / 18f)) % 2 == 0)
                {
                    PlaceBiomeModelOnRoad("Sewers", "SM_pipe_03", materials["Sewer Pipe"],
                        z + 5f, -13.0f, 3.2f, new Vector3(0f, 90f, 0f), Vector3.one * 0.78f, "Wall-Mounted Sewer Pipe", true);
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
                    var stop = PlaceBiomeModelOnRoad("Buildings", "DemoCity/bus_stop",
                        materials["City Props"], stopDistance, side * 19.8f, 0.14f,
                        new Vector3(0f, side > 0f ? -90f : 90f, 0f), Vector3.one, "City Bus Stop");
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
				RenderSettings.ambientIntensity = 1.25f;
				RenderSettings.ambientSkyColor = brooklyn ? new Color(0.38f, 0.44f, 0.52f) : new Color(0.45f, 0.50f, 0.60f);
				RenderSettings.ambientEquatorColor = new Color(0.35f, 0.38f, 0.44f);
				RenderSettings.ambientGroundColor = new Color(0.20f, 0.22f, 0.25f);
				RenderSettings.fog = true;

				if (brooklyn)
				{
					RenderSettings.fogColor = new Color(0.36f, 0.42f, 0.50f);
					RenderSettings.fogDensity = 0.0035f;
				}
				else
				{
					RenderSettings.fogColor = new Color(0.52f, 0.58f, 0.68f);
					RenderSettings.fogDensity = 0.0022f;
				}

				var sceneLights = Object.FindObjectsByType<Light>();
				for (var i = 0; i < sceneLights.Length; i++)
				{
					var sceneLight = sceneLights[i];
					if (sceneLight.type != LightType.Directional || !sceneLight.isActiveAndEnabled) continue;
					sceneLight.color = new Color(1.0f, 0.96f, 0.90f);
					sceneLight.intensity = brooklyn ? 1.45f : 1.75f;
					sceneLight.shadowStrength = 0.75f;
				}

				if (reflectionProbe != null)
				{
					reflectionProbe.size = new Vector3(60f, 20f, 60f);
					reflectionProbe.blendDistance = 3f;
					reflectionProbe.resolution = 256;
					reflectionProbe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
				}

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
				ApplyCityPhotorealMood(brooklyn: false);
				ApplyCitySurfaceMaterialOverrides(brooklyn: false);
				ApplyCityPhotorealSignature(brooklyn: false);
				ApplyCityDepthPass(brooklyn: false);
				ApplyCityRoadToneProfile(brooklyn: false);
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
						neon.SetColor("_EmissionColor", brooklyn
							? new Color(0.32f, 0.58f, 1f, 1f)
							: new Color(0.58f, 0.34f, 1.08f, 1f));
				}

				if (materials.TryGetValue("Hideout Vehicle PBR", out var vehicle))
				{
					if (vehicle.HasProperty("_EmissionColor"))
						vehicle.SetColor("_EmissionColor", brooklyn
							? new Color(0.04f, 0.05f, 0.06f, 1f)
							: new Color(0.14f, 0.16f, 0.22f, 1f));
				}

				if (reflectionProbe != null)
				{
					reflectionProbe.intensity = brooklyn ? 1.06f : 1.22f;
					reflectionProbe.size = brooklyn
						? new Vector3(58f, 18f, 58f)
						: new Vector3(64f, 24f, 64f);
					reflectionProbe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
				}
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

        private void BuildCyberSprawl()
        {
            Random.InitState(41903 ^ chunkSeed);

            // Ground-level NYC sidewalk clutter & newspaper boxes
            ScatterBand(10f, 13.0f, 15.0f, (d, l, s) =>
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

            // building_9 through building_13 used to be listed here and nothing by
            // those names has ever shipped - Resources/Buildings/NYC stops at 8 - so
            // five of the fourteen draws silently produced nothing and the skyline came
            // up with holes in it. The shared variant roster, plus the standalone USA
            // block that only the skyline uses.
            var nycSkyscrapers = new string[NycVariants.Length + 1];
            System.Array.Copy(NycVariants, nycSkyscrapers, NycVariants.Length);
            nycSkyscrapers[NycVariants.Length] = "Buildings/USA/building";

            var nycFrontageBlocks = new[]
            {
                "Buildings/NYCBlock6/builds", "Buildings/NYCBlock6/shops",
                "Buildings/NYCVariants/building_1_1", "Buildings/NYCVariants/building_2_2",
                "Buildings/NYCVariants/building_3_1", "Buildings/NYCVariants/building_4_3",
                "Buildings/NYCVariants/building_5_2", "Buildings/USA/building"
            };

            var nycRooftops = new[]
            {
                "Buildings/NYCBlock6/roof00", "Buildings/NYCBlock6/roof01",
                "Buildings/NYCBlock6/roof02", "Buildings/NYCBlock6/roof03",
                "Buildings/NYCBlock6/roof04", "Buildings/NYCBlock6/roof05",
                "Buildings/NYCBlock6/roof06", "Buildings/NYCBlock6/roof07",
                "Buildings/NYCBlock6/roof08"
            };

            for (var z = SegBegin(0f, 24f); z < segEnd; z += 24f)
            {
                var block = Mathf.FloorToInt(z / 24f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var facing = side > 0f ? -90f : 90f;
                    var frontDistance = z + (side > 0 ? 3f : -4f);

                    // 1. Authentic NYC Street Frontages (Brownstones, Bodegas, Shops)
                    var frontMesh = nycFrontageBlocks[BlockHash(block, side * 7) % nycFrontageBlocks.Length];
                    var front = PlaceBiomeModelOnRoad("Buildings", frontMesh,
                        materials["City Concrete"], frontDistance, side * Random.Range(16.5f, 21.5f), 0f,
                        new Vector3(0f, facing, 0f), Vector3.one, "NYC Street Frontage");
                    if (front != null)
                    {
                        NormalizeModelHeight(front, Random.Range(34f, 62f));
                        EnsureOutsideRoad(front, frontDistance, side);
                    }

                    // 2. Iconic NYC Rooftop Water Tanks & HVAC units
                    if (block % 2 == 0)
                    {
                        var roofMesh = nycRooftops[BlockHash(block, side * 11) % nycRooftops.Length];
                        var roofProp = PlaceBiomeModelOnRoad("Buildings", roofMesh,
                            materials["City Asphalt Trim"], frontDistance, side * Random.Range(18.0f, 24.0f), 35f,
                            new Vector3(0f, facing, 0f), Vector3.one, "NYC Rooftop Water Tank");
                        if (roofProp != null) NormalizeModelHeight(roofProp, Random.Range(4.5f, 8.5f), 35f);
                    }

                    // 3. Towering Background Manhattan Midtown Skyscrapers (65m to 160m)
                    var towerDistance = z + Random.Range(-10f, 10f);
                    var towerMesh = nycSkyscrapers[BlockHash(block, side * 3) % nycSkyscrapers.Length];
                    var tower = PlaceBiomeModelOnRoad("Buildings", towerMesh,
                        materials["City Skyline"], towerDistance, side * Random.Range(36f, 75f), 0f,
                        new Vector3(0f, facing, 0f), Vector3.one, "Manhattan Midtown Skyscraper");
                    if (tower != null) NormalizeModelHeight(tower, Random.Range(70f, 160f));

                    // 4. NYC Street Lamposts with warm amber glow
                    var lampDistance = z + (side > 0 ? 10f : -7f);
                    var lamp = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/lampost2", materials["City Asphalt Trim"],
                        lampDistance, side * 13.0f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Street Lamp");
                    if (lamp != null)
                    {
                        NormalizeModelHeight(lamp, 7.5f, 0.14f);
                        CreateLocalLight(RoadPath.Point(lampDistance, side * 13.0f, 6.8f),
                            new Color(1f, 0.88f, 0.65f), 10f, 16f);
                    }

                    // 5. NYC Traffic Lights at intersections
                    if (block % 5 == 1)
                    {
                        var trafficLight = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/Trafficlight", materials["City Props"],
                            z + 14f, side * 13.2f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Traffic Light");
                        if (trafficLight != null) NormalizeModelHeight(trafficLight, 6.5f, 0.14f);
                    }

                    // 6. NYC Bus Shelters & Advertising Billboards
                    if (block % 4 == 2)
                    {
                        var shelter = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/Busstop", materials["City Props"],
                            z + 18f, side * 13.6f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Bus Shelter");
                        if (shelter != null) NormalizeModelHeight(shelter, 3.4f, 0.14f);
                    }
                    else if (block % 3 == 0)
                    {
                        var panel = PlaceBiomeModelOnRoad("Buildings", "NYCBlock6/Panel00", materials["City Billboard"],
                            z + 18f, side * 14.2f, 0.14f, new Vector3(0f, facing, 0f), Vector3.one, "NYC Street Billboard");
                        if (panel != null) NormalizeModelHeight(panel, 4.2f, 0.14f);
                    }
                }
            }
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

        private void CreateLocalLight(Vector3 position, Color color, float intensity, float range)
        {
            // Point light discs completely removed per design across all biomes.
            // Atmospheric lighting is provided cleanly by Directional Sun, Sky Ambient, Emissive Maps, and Reflection Probes.
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
        private float ScatterDensity => QualitySettings.GetQualityLevel() <= 1 ? 0.55f : 1f;

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
            if (v.NextDouble() < 0.12) return;   // occasional gap: a clearing, a vacant lot

            var near = Mathf.Max(1f, nearLateral + shift);
            var far = Mathf.Max(near + 1f, nearLateral + shift + (farLateral - nearLateral) * widen);

            var spacing = Mathf.Max(1.6f, step / Mathf.Max(0.2f, ScatterDensity * densityMul));
            for (var z = SegBegin(startZ, spacing); z < segEnd; z += spacing)
            for (var side = -1; side <= 1; side += 2)
            {
                // Independent per-side dropout breaks the mirrored look as well.
                if (v.NextDouble() < 0.10) continue;
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
            model.transform.position = RoadPath.Point(distance, lateral, height);
            model.transform.rotation = RoadPath.Rotation(distance) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            model.transform.localScale = Vector3.one * Random.Range(minScale, maxScale);
            EnsureOutsideRoad(model, distance, Mathf.Sign(lateral));
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
            "RunicForest|Flowers/SM_grass_01",
            "RunicForest|Flowers/SM_dead_grass",
            "ForestVillage|Vegetation/SM_plant",
            "ForestVillage|Vegetation/SM_plant1",
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
            var split = entry.Split('|');
            var model = BiomeModel(split[0], split[1], materials["Forest Undergrowth"]);
            if (model == null) return null;
            model.name = label;
            model.transform.position = RoadPath.Point(distance, lateral, height);
            model.transform.rotation = RoadPath.Rotation(distance) *
                                       Quaternion.Euler(-90f, Random.Range(0f, 360f), 0f);
            model.transform.localScale = Vector3.one;
            NormalizeModelHeight(model, Random.Range(minHeight, maxHeight), height);
            return model;
        }

        internal static int canopyKept;
        internal static int canopyRejected;

        private GameObject ForestTree(float distance, float lateral, float minHeight, float maxHeight)
        {
            var table = Random.value < 0.62f ? BroadleafTrees : PineTrees;
            var tree = SpawnForestPiece(table[Random.Range(0, table.Length)], distance, lateral, 0f,
                minHeight, maxHeight, "Forest Tree");
            if (tree == null) return null;
            KeepTrunkOffRoad(tree, distance, Mathf.Sign(lateral));
            if (!KeepCanopyOffRoad(tree, distance, Mathf.Sign(lateral)))
            {
                Destroy(tree);
                canopyRejected++;
                return null;
            }
            canopyKept++;
            return tree;
        }

        private GameObject ForestPlant(float distance, float lateral, float minHeight, float maxHeight, string label) =>
            SpawnForestPiece(ForestPlants[Random.Range(0, ForestPlants.Length)], distance, lateral, 0.06f,
                minHeight, maxHeight, label);

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
            if (reflectionProbe != null)
            {
                reflectionProbe.intensity = 1.25f;
                reflectionProbe.size = new Vector3(68f, 24f, 68f);
                reflectionProbe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
            }
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
            if (globalHorizonSky != null) return;

            globalHorizonSky = new GameObject("Global Horizon Sky & Mountains");
            globalHorizonSky.AddComponent<GlobalHorizonFollower>();

            if (biomeIndex == 9) // Hollywood Hills
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

            // The kit's ground texture is bare dirt, so the forest floor has to be made
            // of meshes: pack undergrowth densely enough that the ground barely shows.
            ScatterBand(1.8f, 7.4f, 13f, (d, l, s) =>
                ForestPlant(d, l, 0.9f, 1.7f, "Verge Undergrowth"));
            ScatterBand(2.4f, 13f, 26f, (d, l, s) =>
                ForestPlant(d, l, 1.0f, 2.0f, "Undergrowth"));
            ScatterBand(3.2f, 26f, 55f, (d, l, s) =>
                ForestPlant(d, l, 1.2f, 2.4f, "Deep Undergrowth"));
            ScatterBand(2.6f, 7.2f, 20f, (d, l, s) =>
            {
                return ForestPlant(d, l, 0.6f, 1.3f, "Forest Grass");
            });

            // Guardrail and a cut bank, both hugging the shoulder
            BuildRibbon("Left Shoulder Bank", -8.4f, -7.6f, 0.5f, materials["Forest Floor PBR"], sampleStep: 5f);
            BuildRibbon("Right Shoulder Bank", 7.6f, 8.4f, 0.5f, materials["Forest Floor PBR"], sampleStep: 5f);
            BuildRibbon("Left Leaf Litter", -8f, -7f, 0.055f, materials["Forest Grass"], sampleStep: 5f);
            BuildRibbon("Right Leaf Litter", 7f, 8f, 0.055f, materials["Forest Grass"], sampleStep: 5f);
            for (var side = -1; side <= 1; side += 2)
            {
                BuildRibbon($"{(side < 0 ? "Left" : "Right")} Guardrail",
                    side * 7.3f, side * 7.5f, 0.95f, materials["Sidewalk"], sampleStep: 4f);
                ScatterBand(9f, 7.4f, 7.4f, (d, l, s) =>
                    PrimitiveOnRoad(PrimitiveType.Cube, "Guardrail Post", d, side * 7.4f, 0.45f,
                        new Vector3(0.16f, 0.9f, 0.16f), materials["Car Dark"], Vector3.zero, false), 0.05f);
            }

            // Trunks packed right against the shoulder. Canopies overhang the road.
            if (NoCanopy) return;
            ScatterBand(10f, 26f, 38f, (d, l, s) => ForestTree(d, l, 12f, 17f));
            ScatterBand(11f, 34f, 52f, (d, l, s) => ForestTree(d, l, 16f, 24f));
            ScatterBand(10f, 27f, 42f, (d, l, s) => ForestTree(d, l, 13f, 21f));
            ScatterBand(11f, 30f, 54f, (d, l, s) => ForestTree(d, l, 12f, 20f));
            ScatterBand(11f, 36f, 66f, (d, l, s) => ForestTree(d, l, 12f, 19f));
            // Far canopy
            ScatterBand(17f, 60f, 140f, (d, l, s) => ForestTree(d, l, 14f, 24f));
            // Bushes and deadfall break up the ground between trunks.
            ScatterBand(5.5f, 8f, 34f, (d, l, s) =>
            {
                return SpawnForestPiece("ForestVillage|Vegetation/SM_bush", d, l, 0.05f, 1.6f, 3.2f, "Forest Bush");
            });

            // Wider grass coverage
            ScatterBand(3.4f, 20f, 55f, (d, l, s) =>
            {
                return ForestPlant(d, l, 0.7f, 1.5f, "Forest Ground Cover");
            });

            // The per-chunk ridge band used to sit here. It placed 90-170 m mountains at
            // only 150-260 m lateral, so they filled the upper frame and read as objects
            // floating in the sky - one measured 320 m wide, 69 m up and 2 m from the
            // player. The Horizon Backdrop rig now carries the silhouette at 1.1 km+,
            // where a mountain belongs.
        }

        /// Nudges an object sideways only until its trunk (a narrow footprint at the
        /// base) is clear of the carriageway, leaving foliage free to reach over.
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

        private void BuildCar()
        {
            var spawn = startDistance + 5f;
            car = new GameObject($"Player {GameState.CurrentCar.Name}").transform;
            car.position = RoadPath.Point(spawn, -2.25f, 0.85f);
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

				// Chassis/trim: same atlas, but duller than the painted panels.
				var chassis = MakeMaterial($"Chassis {selected.Name}", Color.white, 0.30f, 0.55f);
				if (atlas != null)
				{
					chassis.mainTexture = atlas;
					if (chassis.HasProperty("_BaseMap")) chassis.SetTexture("_BaseMap", atlas);
				}

				// The Synty pack ships emissive masks for headlights, tail lights and
				// indicators that nothing was using - which is a large part of why every
				// vehicle read as a dark blob at night. This is real authored data, not
				// a generated approximation.
				var lights = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_Emissive_01");
				if (lights != null)
					foreach (var target in new[] { livery, chassis })
					{
						target.SetTexture("_EmissionMap", lights);
						target.SetColor("_EmissionColor", new Color(2.6f, 2.5f, 2.4f));
						target.EnableKeyword("_EMISSION");
						target.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
					}

				var glass = MakeMaterial($"Glass {selected.Name}", new Color(0.06f, 0.09f, 0.12f), 0.0f, 0.96f);
				glass.SetFloat("_Smoothness", 0.96f);
				materials["Street Racer Atlas"] = livery;
				materials["Street Racer Chassis"] = chassis;
				materials["Street Racer Glass"] = glass;

				foreach (var renderer in car.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
				var racerVisual = Instantiate(racerPrefab, car);
				racerVisual.name = $"Synty {selected.Name} Visual";
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
            var arcade = car.gameObject.AddComponent<ArcadeCarController>();
            arcade.RoadDistance = startDistance + 5f;
            if (playerHull.sqrMagnitude > 0f)
            {
                // Traffic is normalised to a known length before it is measured; the
                // player's visual is left at its native prefab scale so the hand-tuned
                // ride height keeps the tyres on the asphalt. That means the measurement
                // here inherits whatever scale the FBX imported at, so it is clamped to
                // the range a road vehicle can actually occupy - a motorbike at the low
                // end, a semi cab at the high end - rather than trusted outright.
                arcade.HalfLength = Mathf.Clamp(playerHull.x, 0.9f, 8f);
                arcade.HalfWidth = Mathf.Clamp(playerHull.y, 0.4f, 1.8f);
                // The trigger box follows the same measurement, so the collider the
                // directors raycast against agrees with the hull the overlap test uses.
                carCollider.size = new Vector3(arcade.HalfWidth * 2f, 1.2f, arcade.HalfLength * 2f);
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
            TrafficCarController.Offence.Tailgating,
            TrafficCarController.Offence.Weaving,
        };

        private void BuildTraffic()
        {
            var trafficRoot = new GameObject("Living Highway Traffic").transform;
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
            // Clear 65-meter safety corridor in front of player (player spawns at startDistance + 5f)
            // No traffic spawned within [startDistance - 35m .. startDistance + 65m] to prevent launch collisions
            var forwardSpread = new[] { 72f, 105f, 145f, 185f, 225f, 265f, 305f, 350f, 395f, 440f, 485f, 530f };
            var distances = new float[forwardSpread.Length];
            for (var i = 0; i < forwardSpread.Length; i++) distances[i] = startDistance + forwardSpread[i];
            // Fractions of half-width, so cars sit in lanes on any road profile.
            var lanes = new[] { -0.85f, 0.2f, -0.5f, 0.5f, -0.2f, 0.85f, -0.85f, 0.2f, -0.5f, 0.5f, -0.2f, 0.85f };
            // Twelve cars on a six-lane highway is traffic; the same twelve on a two-lane
            // country road is a wall you cannot get through. Scale with the carriageway.
            var laneCount = LaneCountFor(BiomeIndexAt(startDistance));
            var trafficCount = laneCount >= 3 ? lanes.Length
                             : laneCount == 2 ? Mathf.RoundToInt(lanes.Length * 0.65f)
                             : Mathf.RoundToInt(lanes.Length * 0.45f);
            for (var i = 0; i < Mathf.Min(distances.Length, trafficCount); i++)
            {
                var direction = lanes[i] < 0f ? 1f : -1f;
                var speed = direction > 0f ? 68f + i % 5 * 14f : 95f + i % 4 * 15f;
                var violatorEvery = ArcadeCarController.CinematicPilot ? 2 : 3;
                var offence = i % violatorEvery == 1
                    ? OffenceCycle[(i / violatorEvery) % OffenceCycle.Length]
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

                CreateTrafficVehicle(trafficRoot, $"Traffic Car {i + 1}", model,
                    palette[i % palette.Length], distances[i], lanes[i], speed, direction, false, 0f, offence, role);
            }

            Debug.Log($"RR_TRAFFIC spawned={trafficRoot.childCount} models={models.Length}");
            BuildAccidentScene(trafficRoot, startDistance + 210f, -1f, models[1], models[3]);
            BuildAccidentScene(trafficRoot, startDistance + 470f, 1f, models[0], models[8]);
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
            var prefab = Resources.Load<GameObject>($"Vehicles/{modelName}");
            if (prefab == null) Debug.LogWarning($"RR_TRAFFIC missing prefab Vehicles/{modelName}");
            if (prefab != null)
            {
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

                // Traffic headlights & tail lights
                var trafficLights = Resources.Load<Texture2D>("Vehicles/PolygonStreetRacer_Texture_Emissive_01");
                if (trafficLights != null)
                {
                    paint.SetTexture("_EmissionMap", trafficLights);
                    paint.SetColor("_EmissionColor", new Color(2.4f, 2.3f, 2.1f));
                    paint.EnableKeyword("_EMISSION");
                    paint.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                var visual = Instantiate(prefab, root);
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
            var camera = cameraObject.AddComponent<Camera>();
            var flareLayer = cameraObject.GetComponent("FlareLayer");
            if (flareLayer != null) DestroyImmediate(flareLayer);
            var mood = Mood();
            camera.fieldOfView = 64f;
            camera.nearClipPlane = 0.12f;
            camera.farClipPlane = 1400f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.Lerp(mood.Sky, mood.Equator, 0.35f);
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

            var hapticsDirector = cameraObject.AddComponent<RoadRageHapticsDirector>();
            hapticsDirector.BindPlayer(car);

            var rampDirector = cameraObject.AddComponent<RoadRageRampDirector>();
            rampDirector.BindPlayer(car);

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
				camera.fieldOfView = 58f;
				camera.nearClipPlane = 0.10f;
				camera.farClipPlane = 720f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
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
            Debug.Log(GameState.RunOver && GameState.Cash > cashBefore
                ? "RR_TEST RESULT PASS"
                : "RR_TEST RESULT FAIL");
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
                ScreenCapture.CaptureScreenshot(outputPath);
                captured = true;
            }
            else if (captured && elapsed > CaptureAfterSeconds + 2f) Application.Quit();
        }
    }

    public sealed class ArcadeCarController : MonoBehaviour, IRoadVehicle
    {
        public float SpeedKph { get; internal set; } = 0f;
        public float CountdownTimer { get; set; } = 3.2f;

        /// Measured from the spawned vehicle so collision matches the mesh.
        public float HalfLength { get; internal set; } = 2.5f;
        public float HalfWidth { get; internal set; } = 1.3f;
        // --- IRoadVehicle -------------------------------------------------------
        public float ContactDistance => RoadDistance;
        public float ContactLateral => LateralOffset;
        public float ContactHalfLength => HalfLength;
        public float ContactHalfWidth => HalfWidth;
        public float ContactHeight => verticalOffset;
        /// Heaviest thing on the road by a wide margin. Being shoved off your line by
        /// scenery traffic reads as losing the car, so the player absorbs the smallest
        /// share of every correction.
        public float ContactMass => HalfLength * HalfWidth * 4f;
        /// False while the aftertouch director owns the transform during a crash tumble.
        public bool ContactActive => isActiveAndEnabled && !ClearsTraffic;

        public void ApplyContactPush(float alongRoad, float acrossRoad)
        {
            RoadDistance += alongRoad;
            LateralOffset += acrossRoad;
        }

        private void OnEnable() => VehicleContacts.Register(this);
        private void OnDisable() => VehicleContacts.Unregister(this);
        // ------------------------------------------------------------------------

        public float DistanceKm => totalDistance / 1000f;
        public float RoadDistance { get; internal set; } = 5f;
        public float LateralOffset { get; internal set; } = -2.25f;
        /// Trailer autopilot. Hunts violators, avoids innocents and holds the throttle
        /// open so recorded footage shows the actual mechanic rather than someone
        /// fumbling arrow keys. Enabled with -cinematic.
        public static bool CinematicPilot;
        private float pilotLane;
        private float pilotSwerveUntil;
        private float pilotSwerveDir;

        private void DriveCinematically()
        {
            TouchThrottle = 1f;

            // An innocent directly in the path always wins - swerving away from one is
            // the clearest way to show that the game asks you to judge, not just crash.
            var innocent = TrafficCarController.InnocentInPath(RoadDistance, LateralOffset, 48f);
            if (innocent != null && Time.time > pilotSwerveUntil)
            {
                pilotSwerveDir = innocent.LaneOffset > LateralOffset ? -1f : 1f;
                pilotSwerveUntil = Time.time + 0.9f;
            }

            if (Time.time < pilotSwerveUntil)
            {
                var half = RoadPath.HalfWidthAt(RoadDistance) - 2f;
                pilotLane = Mathf.Clamp(LateralOffset + pilotSwerveDir * 6f, -half, half);
            }
            else
            {
                var target = TrafficCarController.FindViolatorAhead(RoadDistance, 150f);
                var blocked = target != null &&
                              TrafficCarController.InnocentInPath(RoadDistance, target.LaneOffset, 60f) != null;
                if (target != null && !blocked)
                {
                    // Lead the target slightly: violators weave, so aim where it is going.
                    pilotLane = target.LaneOffset;
                }
                else
                {
                    // Nothing to hunt - drift gently across the carriageway so the shot
                    // still has motion instead of tracking a dead-straight line.
                    var half = RoadPath.HalfWidthAt(RoadDistance) - 3f;
                    pilotLane = Mathf.Sin(Time.time * 0.22f) * half * 0.55f;
                }
            }

            var error = pilotLane - LateralOffset;
            TouchSteer = Mathf.Clamp(error * 0.42f, -1f, 1f);
        }

        public float TouchSteer { get; set; }
        public float TouchThrottle { get; set; }
        public float Speed => SpeedKph / 3.6f;
        public bool IsAccelerating => (GameInput.GetThrottle() + TouchThrottle) > 0.1f;
        public bool IsBraking => (GameInput.GetThrottle() + TouchThrottle) < -0.1f;
        public float SteerInput => Mathf.Clamp(GameInput.GetSteer() + TouchSteer, -1f, 1f);
        public float LateralVelocity => lateralVelocity;
        public bool IsAirborne => verticalOffset > 0.05f;
        /// High enough to pass over traffic rather than through it. Contact and
        /// separation are both skipped above this, so a jump clears the cars below.
        internal bool ClearsTraffic => verticalOffset >= 1.6f;

        /// Re-applies the road-space position. Update places the car, then the shared
        /// separation pass runs in LateUpdate and may still move it; without this the
        /// correction would not reach the transform until the following frame.
        internal void SyncToRoad() =>
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f + verticalOffset);

        /// Resolve contacts against every other vehicle once all of them have moved,
        /// then re-place. Update has already positioned the car; this is what carries
        /// the correction onto the transform in the same frame.
        private void LateUpdate()
        {
            VehicleContacts.ResolveOncePerFrame();
            if (isActiveAndEnabled) SyncToRoad();
        }
        public float AirtimeDuration { get; private set; }

        private float verticalOffset;
        private float verticalVelocity;
        private float lateralVelocity;
        private float totalDistance;
        private float nextImpactTime;
        // Daily distance is written to PlayerPrefs, so it is batched rather than saved per frame.
        private float distanceSinceDailyBump;
        private static readonly bool autoSteer = RoadRageBootstrap.CommandLineValue("-autosteer") != null;

        public void LaunchAirtime(float launchPower)
        {
            verticalVelocity = launchPower;
            verticalOffset = 0.15f;
            AirtimeDuration = 0f;
            if (RoadRageHapticsDirector.Instance != null)
            {
                RoadRageHapticsDirector.Instance.TriggerLightHaptic(0.35f);
            }
        }

        private void Update()
        {
            if (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive)
            {
                CountdownTimer = 3.0f;
                SpeedKph = 0f;
                lateralVelocity = 0f;
                verticalOffset = 0f;
                verticalVelocity = 0f;
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f);
                transform.rotation = RoadPath.Rotation(RoadDistance);
                return;
            }

            if (CountdownTimer > 0f)
            {
                CountdownTimer -= Time.deltaTime;
                SpeedKph = 0f;
                lateralVelocity = 0f;
                verticalOffset = 0f;
                verticalVelocity = 0f;
                transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f);
                transform.rotation = RoadPath.Rotation(RoadDistance);
                return;
            }

            // -autosteer weaves across the lanes so the unattended verification run
            // actually collides with traffic; without input it just holds its own lane.
            if (autoSteer) TouchSteer = Mathf.Sin(Time.time * 0.8f) * 1.2f;
            if (CinematicPilot) DriveCinematically();

            var steer = SteerInput;
            var rawThrottle = GameInput.GetThrottle() + TouchThrottle;
            var throttle = Mathf.Clamp(rawThrottle, -1f, 1f);

            // Car choice, engine upgrades, and tuning parts scale performance.
            var car = GameState.CurrentCar;
            var enginePower = 1f + GameState.UpgradeEngine * 0.05f;
            var isTurbo = GameState.TuningInduction == 1;
            var isDrift = GameState.TuningTires == 1;

            var topSpeedBonus = isTurbo ? 22f : 0f;
            var accelBonus = !isTurbo ? 1.25f : 1.0f; // Supercharger launch punch
            var isBoosting = RoadRageBoostDirector.Instance != null && RoadRageBoostDirector.Instance.IsBoosting;
            var boostMult = isBoosting ? 1.52f : 1.0f;
            var boostAccel = isBoosting ? 2.8f : 1.0f;

            float targetSpeed;
            float accelRate;

            if (throttle > 0.05f || CinematicPilot)
            {
                var maxSpeed = (150f * car.Speed + topSpeedBonus) * enginePower * boostMult;
                targetSpeed = maxSpeed * Mathf.Max(0.2f, throttle);
                accelRate = 38f * car.Acceleration * enginePower * accelBonus * boostAccel;
            }
            else if (throttle < -0.05f)
            {
                targetSpeed = 0f;
                accelRate = 80f * car.Acceleration;
            }
            else
            {
                // No throttle given: smoothly coast down to standing stop (0 km/h)
                targetSpeed = 0f;
                accelRate = 18f;
            }

            SpeedKph = Mathf.MoveTowards(SpeedKph, targetSpeed, Time.deltaTime * accelRate);

            var steerSpeed = isDrift ? 13.5f : 10.5f;
            lateralVelocity = Mathf.Lerp(lateralVelocity, steer * steerSpeed, 1f - Mathf.Exp(-7f * Time.deltaTime));
            var forwardTravel = SpeedKph / 3.6f * Time.deltaTime;
            totalDistance += forwardTravel;

            // Airborne physics simulation
            var airPitch = 0f;
            if (verticalOffset > 0f || verticalVelocity > 0f)
            {
                AirtimeDuration += Time.deltaTime;
                verticalOffset += verticalVelocity * Time.deltaTime;
                verticalVelocity -= 26f * Time.deltaTime; // Gravity
                airPitch = Mathf.Clamp(verticalVelocity * 1.8f, -14f, 22f);

                if (verticalOffset <= 0f)
                {
                    verticalOffset = 0f;
                    verticalVelocity = 0f;
                    // Landing impact!
                    if (AirtimeDuration > 0.45f)
                    {
                        var bonus = Mathf.RoundToInt(AirtimeDuration * 1200f);
                        GameState.Award(bonus, $"🚀 AIRTIME STUNT ({AirtimeDuration:0.1}s)");
                        if (RoadRageBoostDirector.Instance != null)
                            RoadRageBoostDirector.Instance.AddBoost(40f, "AIRTIME STUNT");
                        if (RoadRageHapticsDirector.Instance != null)
                            RoadRageHapticsDirector.Instance.TriggerMediumHaptic(0.45f);
                    }
                    AirtimeDuration = 0f;
                }
            }

            // Distance points tick up as you drive; the oncoming side is worth double,
            // the risk/reward trade the shipped build uses to pull players across the line.
            GameState.Tick(Time.deltaTime);
            GameState.Score += Mathf.RoundToInt(SpeedKph * Time.deltaTime * 0.7f * (LateralOffset < -1.5f ? 2f : 1f));
            var travelledKm = forwardTravel / 1000f;
            GameState.RunDistanceKm += travelledKm;
            distanceSinceDailyBump += travelledKm;
            if (distanceSinceDailyBump >= 0.05f)
            {
                GameState.BumpDaily("distance", distanceSinceDailyBump);
                distanceSinceDailyBump = 0f;
            }

            RoadDistance = RoadPath.Wrap(RoadDistance + forwardTravel);
            var edge = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset + lateralVelocity * Time.deltaTime, -edge, edge);
            transform.position = RoadPath.Point(RoadDistance, LateralOffset, 0.48f + verticalOffset);
            var desiredRotation = RoadPath.Rotation(RoadDistance) * Quaternion.Euler(-airPitch, steer * 9f, -steer * 4f);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-8f * Time.deltaTime));
            
            // Only test ground collision if not flying high over cars
            if (!ClearsTraffic)
            {
                TrafficCarController.ResolvePlayerCollision(this);
            }

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.UpdateEngineAudio(SpeedKph, 150f * car.Speed, throttle, false);
            }
        }

        public void RefillNitro()
        {
            SpeedKph = Mathf.Min(SpeedKph + 25f, 220f);
        }

        public bool ApplyTrafficImpact(TrafficCarController traffic, float longitudinalGap, float lateralGap)
        {
            if (Time.time < nextImpactTime) return false;
            nextImpactTime = Time.time + 0.35f;
            var sideSwipe = Mathf.Abs(lateralGap) > 1.25f && Mathf.Abs(longitudinalGap) < 3.2f;

            // Anti-penetration is no longer done here. It used to push on both axes at
            // once with its own shrunken hull sizes, fighting the traffic's separate
            // resolution of the same contact; both now go through the single relaxation
            // pass in TrafficCarController, which runs after every vehicle has moved.

            // Armour is the reason to drive a truck: it cuts how much speed an impact
            // scrubs off and how far you get shoved. Upgrades and ram bars stack on top of the chassis.
            var ramArmorBonus = GameState.TuningRamBar switch
            {
                2 => 1.65f, // Titanium Battering Ram
                1 => 1.30f, // Steel Push-Bar
                _ => 1.0f
            };
            var armour = GameState.CurrentCar.Armour * (1f + GameState.UpgradeArmour * 0.06f) * ramArmorBonus;
            var absorb = Mathf.Clamp(1f / Mathf.Max(0.25f, armour), 0.28f, 1.75f);

            if (traffic.IsWreck)
            {
                GameState.Combo = 0;
                GameState.Show("WRECKAGE");
                GameState.ApplyDamage(6f * absorb);
            }
            else if (traffic.IsViolator)
            {
                GameState.Takedowns++;
                GameState.BumpDaily("takedowns", 1f);
                var label = traffic.Violation switch
                {
                    TrafficCarController.Offence.Weaving => "RECKLESS DRIVER",
                    TrafficCarController.Offence.Speeding => "SPEEDER",
                    TrafficCarController.Offence.WrongWay => "WRONG WAY",
                    _ => "TAILGATER",
                };
                GameState.Award(sideSwipe ? 150 : 250, label);
                if (RoadRagePolicePursuitDirector.Instance != null)
                    RoadRagePolicePursuitDirector.Instance.AddHeat(0.35f);
                if (RoadRageBoostDirector.Instance != null)
                    RoadRageBoostDirector.Instance.AddBoost(50f, "TAKEDOWN BOOST");
                GameState.ApplyDamage((sideSwipe ? 1.5f : 3f) * absorb);
            }
            else
            {
                GameState.Combo = 0;
                GameState.InnocentsHit++;
                GameState.Score = Mathf.Max(0, GameState.Score - 200);
                GameState.Show("INNOCENT DRIVER  -200");
                GameState.ApplyDamage(14f * absorb);
            }
            var contactPoint = transform.position + transform.forward * 2.2f + Vector3.up * 0.6f;
            CrashEffects.Active?.PlayAt(contactPoint);
            var audioVfx = GetComponent<RoadRageAudioAndVFX>();
            if (audioVfx != null)
                audioVfx.PlayCrashImpact(contactPoint, traffic.IsViolator);

            if (RoadRageHapticsDirector.Instance != null)
            {
                RoadRageHapticsDirector.Instance.TriggerMediumHaptic(sideSwipe ? 0.45f : 0.75f);
            }

            if (GameState.Integrity <= 0f && !GameState.IsAftertouchActive)
            {
                if (RoadRageHapticsDirector.Instance != null)
                {
                    RoadRageHapticsDirector.Instance.TriggerHeavyCrashHaptic(1.0f);
                }

                if (RoadRageAftertouchDirector.Instance != null)
                {
                    var tumbleVel = transform.forward * (SpeedKph / 3.6f * 0.92f) + (traffic.transform.position - transform.position).normalized * 7.5f + Vector3.up * 8.5f;
                    var tumbleTorque = new Vector3(Random.Range(-18f, 18f), Random.Range(12f, 28f), Random.Range(-30f, 30f));
                    RoadRageAftertouchDirector.Instance.TriggerAftertouch(transform, this, tumbleVel, tumbleTorque);
                    return true;
                }
            }

            if (traffic.IsViolator || SpeedKph >= 60f)
            {
                if (RoadRageTakedownDirector.Instance != null && !traffic.IsWreck)
                {
                    var impactNormal = (traffic.transform.position - transform.position).normalized;
                    RoadRageTakedownDirector.Instance.TriggerTakedown(traffic.transform, contactPoint, impactNormal, SpeedKph);
                }
            }

            if (sideSwipe)
                SpeedKph = Mathf.Max(28f, SpeedKph - 22f * absorb);
            else if (traffic.IsWreck || traffic.Direction < 0f)
                SpeedKph = Mathf.Min(SpeedKph, Mathf.Lerp(12f, SpeedKph, 1f - absorb));
            else
                SpeedKph = Mathf.Min(SpeedKph, Mathf.Lerp(38f, SpeedKph, 1f - absorb));

            var pushDirection = Mathf.Abs(lateralGap) < 0.05f ? -1f : -Mathf.Sign(lateralGap);
            lateralVelocity += pushDirection * (sideSwipe ? 3.5f : 6.5f) * absorb;
            var pushEdge = Mathf.Max(3f, RoadPath.HalfWidthAt(RoadDistance) - 1.4f);
            LateralOffset = Mathf.Clamp(LateralOffset + pushDirection * (sideSwipe ? 0.25f : 0.55f) * absorb, -pushEdge, pushEdge);
            return true;
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
            if (RoadRageHapticsDirector.Instance != null)
            {
                shakeOffset = RoadRageHapticsDirector.Instance.CurrentShakeOffset;
                shakeRot = RoadRageHapticsDirector.Instance.CurrentShakeRotation;
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

    /// Re-renders the street reflection probe on a timer. Every-frame capture means six
    /// extra scene renders per frame; at 85 km/h a ~7 Hz refresh is indistinguishable.
    public sealed class ReflectionProbeDriver : MonoBehaviour
    {
        public ReflectionProbe probe;
        public float interval = 0.25f;

        private float nextRefresh;

        private void LateUpdate()
        {
            if (probe == null || Time.time < nextRefresh) return;
            nextRefresh = Time.time + interval;
            probe.RenderProbe();
        }
    }

    public sealed class RoadRageHUD : MonoBehaviour
    {
        private ArcadeCarController car;
        private RoadRageBootstrap world;
        private GUIStyle titleStyle;
        private GUIStyle readoutStyle;
        private GUIStyle buttonStyle;
        private GUIStyle pickerTitleStyle;
        private GUIStyle lockedStyle;
        private Texture2D dimTexture;
        private bool garageOpen;
        private bool missionsOpen;
        private int garageBrowse = -1;

        public static RoadRageHUD Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
        }

        private RoadRageBootstrap World => world != null ? world : RoadRageBootstrap.Instance;
        private ArcadeCarController Car => car != null ? car : (World != null && World.PlayerCar != null ? World.PlayerCar.GetComponent<ArcadeCarController>() : null);

        public void Initialize(ArcadeCarController controller, RoadRageBootstrap bootstrap)
        {
            car = controller;
            world = bootstrap;
        }

        /// Font sizes are authored against a 900px-tall screen. Phones in landscape are
        /// ~390-500px tall, where fixed sizes overflow their controls, so every style is
        /// rescaled whenever the screen size changes.
        private int styledForHeight;
        private float UiScale => Mathf.Clamp(Screen.height / 900f, 0.5f, 1.3f);

        private void ApplyUiScale()
        {
            if (styledForHeight == Screen.height) return;
            styledForHeight = Screen.height;
            var s = UiScale;
            titleStyle.fontSize = Mathf.RoundToInt(27 * s);
            readoutStyle.fontSize = Mathf.RoundToInt(18 * s);
            buttonStyle.fontSize = Mathf.RoundToInt(22 * s);
            pickerTitleStyle.fontSize = Mathf.RoundToInt(34 * s);
            lockedStyle.fontSize = Mathf.RoundToInt(17 * s);
        }

        private static Texture2D cardGlassTex;
        private static Texture2D statCardGlassTex;
        private static Texture2D pillBadgeTex;
        private static Texture2D goldBadgeTex;
        private static Texture2D topBarTex;
        private static Texture2D greenBtnTex;
        private static Texture2D blueBtnTex;
        private static Texture2D orangeBtnTex;
        private static Texture2D rowEvenTex;
        private static Texture2D rowOddTex;
        private static Texture2D rowHighlightTex;

        private static Font arcadeFont;
        private static Font titleFont;

        private static Texture2D CreateAntiAliasedBox(int w, int h, int r, Color fill, Color border, float borderWidth = 1.0f, float topSheen = 0.08f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Max(r - x, 0, x - (w - 1 - r));
                var dy = Mathf.Max(r - y, 0, y - (h - 1 - r));
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > r - borderWidth)
                {
                    var edgeAlpha = Mathf.Clamp01(r - dist);
                    tex.SetPixel(x, y, Color.Lerp(Color.clear, border, edgeAlpha));
                }
                else
                {
                    var col = fill;
                    if (y > h * 0.5f && topSheen > 0f)
                    {
                        var frac = (float)(y - h * 0.5f) / (h * 0.5f);
                        col += new Color(topSheen * frac, topSheen * frac, topSheen * frac, 0f);
                    }
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateBeveledButton(int w, int h, int r, Color topCol, Color botCol, Color border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Max(r - x, 0, x - (w - 1 - r));
                var dy = Mathf.Max(r - y, 0, y - (h - 1 - r));
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > r - 1.2f)
                {
                    tex.SetPixel(x, y, border);
                }
                else
                {
                    var vert = (float)y / h;
                    var col = Color.Lerp(botCol, topCol, vert);
                    if (y > h * 0.55f)
                    {
                        var sheen = Mathf.Lerp(0f, 0.18f, (y - h * 0.55f) / (h * 0.45f));
                        col += new Color(sheen, sheen, sheen, 0f);
                    }
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateHeaderTexture(int w, int h, Color fill, Color bottomBorder)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (y <= 2)
                    tex.SetPixel(x, y, bottomBorder);
                else
                {
                    var grad = Mathf.Lerp(1.05f, 0.95f, (float)y / h);
                    tex.SetPixel(x, y, fill * grad);
                }
            }
            tex.Apply();
            return tex;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) { ApplyUiScale(); return; }

            arcadeFont = Resources.Load<Font>("Fonts/RobotoCondensed-Bold");
            titleFont = Resources.Load<Font>("Fonts/Play-Bold");
            if (arcadeFont == null) arcadeFont = titleFont;
            if (titleFont == null) titleFont = arcadeFont;

            titleStyle = new GUIStyle(GUI.skin.label) { font = titleFont, fontSize = 27, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            readoutStyle = new GUIStyle(GUI.skin.label) { font = arcadeFont, fontSize = 18, fontStyle = FontStyle.Bold };
            readoutStyle.normal.textColor = new Color(0.62f, 1f, 0.72f);
            buttonStyle = new GUIStyle(GUI.skin.button) { font = titleFont, fontSize = 22, fontStyle = FontStyle.Bold };
            pickerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                font = titleFont, fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            pickerTitleStyle.normal.textColor = Color.white;
            lockedStyle = new GUIStyle(GUI.skin.button) { font = titleFont, fontSize = 17, fontStyle = FontStyle.Bold };
            lockedStyle.normal.textColor = new Color(0.55f, 0.55f, 0.60f);
            lockedStyle.hover.textColor = lockedStyle.normal.textColor;

            dimTexture = new Texture2D(1, 1);
            dimTexture.SetPixel(0, 0, new Color(0.02f, 0.025f, 0.045f, 0.93f));
            dimTexture.Apply();

            // High-Resolution Premium Assets
            cardGlassTex = CreateAntiAliasedBox(256, 256, 18, new Color(0.04f, 0.06f, 0.11f, 0.94f), new Color(0.25f, 0.65f, 1f, 0.80f), 1.2f, 0.06f);
            statCardGlassTex = CreateAntiAliasedBox(256, 256, 14, new Color(0.03f, 0.04f, 0.08f, 0.90f), new Color(0.35f, 0.55f, 0.85f, 0.45f), 1.0f, 0.04f);
            goldBadgeTex = CreateBeveledButton(256, 128, 16, new Color(0.32f, 0.22f, 0.06f, 0.95f), new Color(0.16f, 0.10f, 0.02f, 0.95f), new Color(1f, 0.82f, 0.25f, 0.85f));
            pillBadgeTex = CreateAntiAliasedBox(256, 128, 16, new Color(0.06f, 0.09f, 0.16f, 0.92f), new Color(0.3f, 0.55f, 0.9f, 0.50f), 1.0f, 0.05f);
            topBarTex = CreateHeaderTexture(128, 64, new Color(0.04f, 0.05f, 0.09f, 0.94f), new Color(0.2f, 0.60f, 1f, 0.45f));

            greenBtnTex = CreateBeveledButton(256, 128, 14, new Color(0.15f, 0.85f, 0.45f), new Color(0.05f, 0.55f, 0.25f), new Color(0.4f, 1f, 0.65f));
            blueBtnTex = CreateBeveledButton(256, 128, 14, new Color(0.15f, 0.65f, 1f), new Color(0.05f, 0.35f, 0.75f), new Color(0.45f, 0.85f, 1f));
            orangeBtnTex = CreateBeveledButton(256, 128, 14, new Color(1f, 0.65f, 0.15f), new Color(0.75f, 0.35f, 0.05f), new Color(1f, 0.85f, 0.4f));
            rowEvenTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.06f, 0.08f, 0.14f, 0.75f), Color.clear, 0f, 0.02f);
            rowOddTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.04f, 0.05f, 0.10f, 0.75f), Color.clear, 0f, 0.02f);
            rowHighlightTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.18f, 0.14f, 0.04f, 0.90f), new Color(1f, 0.82f, 0.22f, 0.85f), 1.2f, 0.08f);

            ApplyUiScale();
        }

        private void Start()
        {
            // -ui=garage / -ui=missions opens a panel straight away so the screenshot
            // verification path can capture it without simulated input.
            var panel = RoadRageBootstrap.CommandLineValue("-ui=");
            if (panel == "garage") garageOpen = true;
            else if (panel == "missions") missionsOpen = true;
        }

        /// Store/press captures must not show the HUD, the mobile touch buttons or the
        /// development watermark - on a Steam page those read as "mobile port". Set by
        /// -cleanshot on the command line.
        public static bool HideForCapture;

        private void OnGUI()
        {
            if (HideForCapture) return;
            EnsureStyles();
            var w = World;
            if (w == null) return;
            if (w.PickerOpen)
            {
                DrawPicker();
                return;
            }
            if (garageOpen)
            {
                DrawGarage();
                return;
            }
            if (missionsOpen)
            {
                DrawMissions();
                return;
            }
            if (RoadRageLeaderboardDirector.Instance != null && RoadRageLeaderboardDirector.Instance.IsLeaderboardOpen)
            {
                DrawLeaderboardModal();
                return;
            }
            if (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive)
            {
                DrawLandingScreen();
                return;
            }
            var c = Car;
            if (c == null) return;

            var menuRect = new Rect(Screen.width - 240f, 20f, 100f, 44f);
            if (GUI.Button(menuRect, "🏠 MENU", buttonStyle))
            {
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.ReturnToLanding();
                return;
            }

            var garageRect = new Rect(Screen.width * 0.5f - 250f, 24f, 150f, 44f);
            if (GUI.Button(garageRect, "GARAGE", buttonStyle))
            {
                garageOpen = true;
                return;
            }

            var missionsRect = new Rect(Screen.width * 0.5f + 100f, 24f, 150f, 44f);
            if (GUI.Button(missionsRect, "MISSIONS", buttonStyle))
            {
                missionsOpen = true;
                return;
            }

            GUI.Label(new Rect(28f, 22f, 520f, 44f), "ROAD RAGE  /  UNITY REMAKE", titleStyle);
            GUI.Label(new Rect(30f, 62f, 620f, 32f),
                $"{w.BiomeNameAt(c.RoadDistance)}  |  {WeatherSystem.Label(w.Weather)}  |  {c.SpeedKph:0} km/h  |  {c.DistanceKm:0.00} km",
                readoutStyle);
            // Integrity bar - the run's clock. Without a visible failure state the player
            // has no reason to judge targets rather than ram everything.
            var barW = 220f * UiScale;
            // Text rows above use unscaled Y (62, 96), so the bar must too or it overlaps.
            var barRect = new Rect(30f, 134f, barW, 13f);
            GUI.DrawTexture(barRect, dimTexture);
            var frac = Mathf.Clamp01(GameState.Integrity / GameState.MaxIntegrity);
            var barColor = frac > 0.5f ? new Color(0.35f, 0.95f, 0.5f)
                : frac > 0.25f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
            var prev = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * frac, barRect.height),
                Texture2D.whiteTexture);
            GUI.color = prev;

            // Nitro Boost Meter Bar
            if (RoadRageBoostDirector.Instance != null)
            {
                var boostRect = new Rect(30f, 150f, barW, 11f);
                GUI.DrawTexture(boostRect, dimTexture);
                var bFrac = Mathf.Clamp01(RoadRageBoostDirector.Instance.BoostAmount / RoadRageBoostDirector.MaxBoost);
                var isFull = RoadRageBoostDirector.Instance.IsFullBoost;
                var isBurning = RoadRageBoostDirector.Instance.IsBoosting;
                var boostColor = isFull ? new Color(1f, 0.85f, 0.2f) : isBurning ? new Color(1f, 0.45f, 0.1f) : new Color(0.15f, 0.8f, 1f);
                var prevC = GUI.color;
                GUI.color = boostColor;
                GUI.DrawTexture(new Rect(boostRect.x, boostRect.y, boostRect.width * bFrac, boostRect.height), Texture2D.whiteTexture);
                GUI.color = prevC;

                var chain = RoadRageBoostDirector.Instance.BurnoutChain;
                var boostLabel = chain > 0 ? $"🔥 BURNOUT x{chain}" : (isFull ? "★ NITRO READY" : (isBurning ? "🔥 BOOSTING" : "NITRO"));
                GUI.Label(new Rect(35f + barW, 146f, 180f, 20f), boostLabel, readoutStyle);
            }

            GUI.Label(new Rect(30f, 96f, 520f, 32f),
                $"SCORE {GameState.Score:N0}   ${GameState.Cash:N0}   {GameState.Takedowns} TAKEDOWNS", readoutStyle);
            if (GameState.Combo > 0)
                GUI.Label(new Rect(30f, 130f, 520f, 34f), $"x{GameState.ComboMultiplier} COMBO", titleStyle);
            if (c.CountdownTimer > 0f)
            {
                var digit = Mathf.CeilToInt(c.CountdownTimer);
                var countdownColor = digit switch
                {
                    3 => new Color(1f, 0.85f, 0.2f), // Gold
                    2 => new Color(1f, 0.55f, 0.1f), // Orange
                    _ => new Color(1f, 0.25f, 0.2f)  // Coral Red
                };
                var prevC = pickerTitleStyle.normal.textColor;
                pickerTitleStyle.normal.textColor = countdownColor;
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.26f, 300f, 90f),
                    $"{digit}", pickerTitleStyle);
                pickerTitleStyle.normal.textColor = prevC;
            }
            else if (c.CountdownTimer > -0.9f)
            {
                var prevC = pickerTitleStyle.normal.textColor;
                pickerTitleStyle.normal.textColor = new Color(0.25f, 1f, 0.45f);
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.26f, 400f, 90f),
                    "GO! 🚀", pickerTitleStyle);
                pickerTitleStyle.normal.textColor = prevC;
            }
            else if (!string.IsNullOrEmpty(GameState.Message))
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.32f, 400f, 40f),
                    GameState.Message, titleStyle);
            }
            else if (c.SpeedKph < 4f && c.DistanceKm < 0.05f)
            {
                var pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f);
                var prevC = titleStyle.normal.textColor;
                titleStyle.normal.textColor = new Color(0.2f, 0.95f, 0.4f, pulse);
                GUI.Label(new Rect(Screen.width * 0.5f - 280f, Screen.height * 0.32f, 560f, 44f),
                    "🚦 READY! PRESS [GAS] / [W] TO DRIVE", titleStyle);
                titleStyle.normal.textColor = prevC;
            }

            if (RoadRagePolicePursuitDirector.Instance != null && RoadRagePolicePursuitDirector.Instance.IsPursuitActive)
            {
                var heat = RoadRagePolicePursuitDirector.Instance.HeatLevel;
                var stars = new string('★', heat) + new string('☆', 5 - heat);
                var heatText = $"🚨 WANTED: {stars}  (HEAT {heat})";
                var flash = Mathf.Sin(Time.unscaledTime * 8f) > 0f;
                var prevColor = titleStyle.normal.textColor;
                titleStyle.normal.textColor = flash ? new Color(1f, 0.25f, 0.2f) : new Color(0.2f, 0.65f, 1f);
                GUI.Label(new Rect(Screen.width * 0.5f - 180f, 76f, 360f, 36f), heatText, titleStyle);
                titleStyle.normal.textColor = prevColor;
            }
            GUI.Label(new Rect(Screen.width - 270f, 24f, 130f, 32f), $"{Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))} FPS", readoutStyle);
            
            var pauseRect = new Rect(Screen.width - 130f, 20f, 110f, 44f);
            if (GUI.Button(pauseRect, w.PickerOpen ? "RESUME" : "PAUSE", buttonStyle))
            {
                if (w.PickerOpen) w.ClosePicker();
                else w.OpenPicker();
                return;
            }

            var biomeRect = new Rect(Screen.width * 0.5f - 92f, 22f, 184f, 48f);
            if (GUI.Button(biomeRect, "BIOMES", buttonStyle))
            {
                w.OpenPicker();
                return;
            }

            if (GameState.IsAftertouchActive)
            {
                GUI.Label(new Rect(0f, 22f, Screen.width, 44f), "💥 IMPACT TIME — AFTERTOUCH 💥", pickerTitleStyle);
                GUI.Label(new Rect(0f, 66f, Screen.width, 32f),
                    $"STEER YOUR WRECK INTO TRAFFIC!   PILEUP: ${GameState.PileupDamage:N0}   TAKEDOWNS: {GameState.AftertouchTakedowns}", readoutStyle);

                if (GameState.CrashbreakerReady)
                {
                    var cbWidth = Mathf.Min(340f, Screen.width * 0.6f);
                    var cbRect = new Rect(Screen.width * 0.5f - cbWidth * 0.5f, Screen.height * 0.72f, cbWidth, 60f);
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.28f, 0.08f, 0.95f);
                    if (GUI.Button(cbRect, "💥 CRASHBREAKER (SPACE / TAP)", buttonStyle))
                    {
                        if (RoadRageAftertouchDirector.Instance != null)
                            RoadRageAftertouchDirector.Instance.DetonateCrashbreaker();
                    }
                    GUI.backgroundColor = prevBg;
                }

                var atSize = Mathf.Clamp(Screen.height * 0.14f, 84f, 136f);
                var atBottom = Screen.height - atSize - 24f;
                var atLeft = GUI.RepeatButton(new Rect(24f, atBottom, atSize, atSize), "◄ STEER", buttonStyle);
                var atRight = GUI.RepeatButton(new Rect(36f + atSize, atBottom, atSize, atSize), "STEER ►", buttonStyle);
                if (RoadRageAftertouchDirector.Instance != null)
                {
                    RoadRageAftertouchDirector.Instance.TouchAftertouchSteer = atLeft ? -1f : atRight ? 1f : 0f;
                }
                return;
            }

            if (GameState.RunOver)
            {
                DrawRunOverScreen();
                return;
            }

            var size = Mathf.Clamp(Screen.height * 0.13f, 76f, 126f);
            var bottom = Screen.height - size - 24f;
            var left = GUI.RepeatButton(new Rect(24f, bottom, size, size), "LEFT", buttonStyle);
            var right = GUI.RepeatButton(new Rect(36f + size, bottom, size, size), "RIGHT", buttonStyle);
            var nitro = GUI.RepeatButton(new Rect(Screen.width - size * 3.5f - 52f, bottom, size * 1.15f, size), "NITRO", buttonStyle);
            var brake = GUI.RepeatButton(new Rect(Screen.width - size * 2.25f - 38f, bottom, size, size), "BRAKE", buttonStyle);
            var gas = GUI.RepeatButton(new Rect(Screen.width - size - 24f, bottom, size, size), "GAS", buttonStyle);
            c.TouchSteer = left ? -1f : right ? 1f : 0f;
            c.TouchThrottle = gas ? 1f : brake ? -1f : 0f;
            if (RoadRageBoostDirector.Instance != null)
            {
                RoadRageBoostDirector.Instance.TouchNitroPressed = nitro;
            }
        }

        /// Garage: browse the catalogue, buy/select a car, and spend cash on the three
        /// upgrade tracks. Selecting a different car rebuilds the world so the new mesh,
        /// livery and handling stats take effect immediately.
        /// Horizontal 0-1 stat bar. Numbers alone do not communicate that the Juggernaut
        /// has twice the armour of the starter ute.
        private void StatBar(Rect rect, string label, float value, float max, Color fill)
        {
            GUI.Label(new Rect(rect.x, rect.y - 2f, 92f, rect.height), label, readoutStyle);
            var track = new Rect(rect.x + 96f, rect.y + 5f, rect.width - 150f, rect.height - 12f);
            GUI.DrawTexture(track, dimTexture);
            var t = Mathf.Clamp01(value / max);
            var old = GUI.color;
            GUI.color = fill;
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(track.xMax + 8f, rect.y - 2f, 60f, rect.height), $"{value:0.00}", readoutStyle);
        }

        private void DrawGarage()
        {
            if (garageBrowse < 0) garageBrowse = GameState.SelectedCar;
            var cars = GameState.Cars;
            garageBrowse = Mathf.Clamp(garageBrowse, 0, cars.Length - 1);
            var spec = cars[garageBrowse];
            var owned = GameState.OwnedCars.Contains(garageBrowse);
            var selected = GameState.SelectedCar == garageBrowse;

            // Live 3D vehicle behind the panel, slowly turning.
            world.EnsureShowroom(garageBrowse);
            world.SetShowroomActive(true, Time.unscaledTime * 16f);
            if (world.ShowroomCamera != null)
                world.ShowroomCamera.rect = new Rect(0f, 0f, 1f, 1f);

            var w = Screen.width;
            var h = Screen.height;
            var panelW = Mathf.Max(300f, w * 0.30f);
            GUI.DrawTexture(new Rect(0f, 0f, panelW, h), dimTexture);
            GUI.DrawTexture(new Rect(0f, 0f, w, h * 0.13f), dimTexture);
            GUI.DrawTexture(new Rect(0f, h * 0.88f, w, h * 0.12f), dimTexture);

            GUI.Label(new Rect(0f, h * 0.035f, w, 60f), "GARAGE", pickerTitleStyle);
            GUI.Label(new Rect(0f, h * 0.09f, w, 30f),
                $"${GameState.Cash:N0}   -   {GameState.NextCarGoal()}", readoutStyle);

            // ---- Left panel: identity, price, stats, description ----
            var pad = 22f;
            var y = h * 0.17f;
            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 40f), spec.Name, titleStyle);
            y += 40f;

            var status = selected ? "IN USE" : owned ? "OWNED" : $"${spec.Price:N0}";
            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 30f),
                $"{garageBrowse + 1} / {cars.Length}     {status}", readoutStyle);
            y += 42f;

            var barH = Mathf.Max(26f, 30f * UiScale);
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "SPEED", spec.Speed, 1.5f,
                new Color(0.35f, 0.85f, 1f, 0.95f)); y += barH + 6f;
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "ACCEL", spec.Acceleration, 1.5f,
                new Color(0.55f, 1f, 0.55f, 0.95f)); y += barH + 6f;
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "ARMOUR", spec.Armour, 2.5f,
                new Color(1f, 0.68f, 0.32f, 0.95f)); y += barH + 14f;

            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 110f), spec.Description, readoutStyle);

            // ---- Upgrades, applied to whichever vehicle you drive ----
            var upgrades = new[] { ("engine", "ENGINE"), ("armor", "ARMOUR"), ("boost", "BOOST") };
            var upY = h * 0.60f;
            var upH = Mathf.Max(40f, 52f * UiScale);
            for (var i = 0; i < upgrades.Length; i++)
            {
                var (key, title) = upgrades[i];
                var level = GameState.UpgradeLevel(key);
                var maxed = level >= GameState.UpgradeMax;
                var cost = GameState.UpgradeCost(level);
                var rect = new Rect(pad, upY + i * (upH + 8f), panelW - pad * 2f, upH);
                var label = maxed
                    ? $"{title}   MAX ({level})"
                    : $"{title}   {level}/{GameState.UpgradeMax}   ${cost:N0}";
                if (GUI.Button(rect, label, maxed || GameState.Cash < cost ? lockedStyle : buttonStyle) && !maxed)
                    GameState.BuyUpgrade(key);
            }

            // ---- Right side panel: Performance Tuning ----
            var rightPanelW = Mathf.Max(240f, w * 0.24f);
            var rightX = w - rightPanelW;
            GUI.DrawTexture(new Rect(rightX, 0f, rightPanelW, h), dimTexture);
            var tuneY = h * 0.16f;
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 32f), "PERFORMANCE TUNING", titleStyle);
            tuneY += 38f;

            // 1. Forced Induction Tuning
            var inductLabel = GameState.TuningInduction == 0 ? "SUPERCHARGER" : "TURBOCHARGER";
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"INDUCTION: {inductLabel}", buttonStyle))
            {
                GameState.TuningInduction = (GameState.TuningInduction + 1) % 2;
                GameState.Save();
            }
            tuneY += 46f;
            var inductDesc = GameState.TuningInduction == 0 ? "⚡ +22% Launch Acceleration" : "🔥 +22 km/h Top Speed";
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), inductDesc, readoutStyle);
            tuneY += 30f;

            // 2. Tire Compound Tuning
            var tireLabel = GameState.TuningTires == 0 ? "RACING GRIP" : "DRIFT COMPOUND";
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"TIRES: {tireLabel}", buttonStyle))
            {
                GameState.TuningTires = (GameState.TuningTires + 1) % 2;
                GameState.Save();
            }
            tuneY += 46f;
            var tireDesc = GameState.TuningTires == 0 ? "🎯 Laser-Sharp Lane Control" : "💨 Power-Slide Drift Boost";
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), tireDesc, readoutStyle);
            tuneY += 30f;

            // 3. Ramming Bar Tuning
            var ramName = GameState.TuningRamBar switch
            {
                2 => "TITANIUM RAM",
                1 => "STEEL PUSH-BAR",
                _ => "STOCK BUMPER"
            };
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"RAM: {ramName}", buttonStyle))
            {
                GameState.TuningRamBar = (GameState.TuningRamBar + 1) % 3;
                GameState.Save();
            }
            tuneY += 46f;
            var ramDesc = GameState.TuningRamBar switch
            {
                2 => "💥 +65% Ram Power / Heavy Armor",
                1 => "🛡️ +30% Takedown Power",
                _ => "Standard Bumper"
            };
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), ramDesc, readoutStyle);

            // ---- Bottom bar: browse, buy/select, back ----
            var barY = h * 0.90f;
            var btnH = Mathf.Max(42f, 52f * UiScale);
            var navW = Mathf.Max(104f, 124f * UiScale);

            if (GUI.Button(new Rect(pad, barY, navW, btnH), "< PREV", buttonStyle))
                garageBrowse = (garageBrowse - 1 + cars.Length) % cars.Length;
            if (GUI.Button(new Rect(pad + navW + 8f, barY, navW, btnH), "NEXT >", buttonStyle))
                garageBrowse = (garageBrowse + 1) % cars.Length;

            var actionW = Mathf.Min(260f, w * 0.22f);
            var actionRect = new Rect(w * 0.5f - actionW * 0.5f, barY, actionW, btnH);
            if (selected)
            {
                GUI.Button(actionRect, "SELECTED", lockedStyle);
            }
            else if (owned)
            {
                if (GUI.Button(actionRect, "DRIVE THIS", buttonStyle))
                {
                    GameState.SelectedCar = garageBrowse;
                    GameState.Save();
                    world.ReloadBiome(world.BiomeName);
                }
            }
            else
            {
                var afford = GameState.Cash >= spec.Price;
                if (GUI.Button(actionRect, $"BUY  ${spec.Price:N0}", afford ? buttonStyle : lockedStyle)
                    && afford && GameState.BuyCar(garageBrowse))
                {
                    GameState.SelectedCar = garageBrowse;
                    GameState.Save();
                    world.ReloadBiome(world.BiomeName);
                }
            }

            var backW = Mathf.Min(160f, w * 0.14f);
            if (GUI.Button(new Rect(w - backW - pad, barY, backW, btnH), "BACK", buttonStyle))
                CloseGarage();
        }

        private void CloseGarage()
        {
            garageOpen = false;
            garageBrowse = -1;
            world.SetShowroomActive(false);
        }

        /// Daily missions: three rolled per calendar day, with a login streak bonus.
        private void DrawMissions()
        {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTexture);
            GUI.Label(new Rect(0f, Screen.height * 0.08f, Screen.width, 60f), "DAILY MISSIONS", pickerTitleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 32f),
                $"DAY {GameState.LoginStreak} STREAK   •   TODAY'S BONUS +${GameState.LastLoginReward:N0}   •   ${GameState.Cash:N0}",
                readoutStyle);

            var rowY = Screen.height * 0.26f;
            for (var slot = 0; slot < GameState.MissionIds.Count; slot++)
            {
                var spec = GameState.MissionPool[GameState.MissionIds[slot]];
                var progress = GameState.MissionProgress(slot);
                var done = GameState.MissionDone(slot);
                var claimed = slot < GameState.MissionClaimed.Count && GameState.MissionClaimed[slot];
                var goalText = string.Format(spec.Description, spec.Goal % 1f == 0f ? $"{spec.Goal:0}" : $"{spec.Goal:0.#}");

                GUI.Label(new Rect(Screen.width * 0.5f - 380f, rowY + slot * 92f, 520f, 32f),
                    $"{goalText}   —   {Mathf.Min(progress, spec.Goal):0.#}/{spec.Goal:0.#}", readoutStyle);

                var rect = new Rect(Screen.width * 0.5f + 170f, rowY + slot * 92f - 8f, 210f, 54f);
                var label = claimed ? "CLAIMED" : done ? $"CLAIM  ${spec.Reward:N0}" : $"${spec.Reward:N0}";
                if (GUI.Button(rect, label, done && !claimed ? buttonStyle : lockedStyle) && done && !claimed)
                    GameState.ClaimMission(slot);
            }

            if (GUI.Button(new Rect(Screen.width * 0.5f - 90f, rowY + 300f, 180f, 52f), "BACK", buttonStyle))
                missionsOpen = false;
        }

        private bool settingsOpen = false;
        private bool sfxEnabled = true;
        private bool showFps = true;

        private void DrawLandingScreen()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            // Safe Area margins (protecting from mobile notches, camera cutouts, and rounded corners)
            var leftPad = Mathf.Max(safe.x, 36f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 36f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 14f);
            var botPad = Mathf.Max(safe.y, 14f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 650f, 0.48f, 1.15f);

            // 1. Top Status Bar
            var topBarH = Mathf.Clamp(usableH * 0.13f, 40f, 58f);
            var topBarY = topPad;
            if (topBarTex != null)
                GUI.DrawTexture(new Rect(0f, 0f, w, topBarY + topBarH), topBarTex);
            else
                GUI.DrawTexture(new Rect(0f, 0f, w, topBarY + topBarH), dimTexture);

            // Top Bar - Left: Player Profile Pill
            var badgeW = Mathf.Clamp(usableW * 0.25f, 150f, 250f);
            var rank = Mathf.Max(1, GameState.Takedowns / 5 + 1);
            if (goldBadgeTex != null)
                GUI.DrawTexture(new Rect(leftPad, topBarY + 3f, badgeW, topBarH - 6f), goldBadgeTex);

            var profileStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(leftPad + 8f, topBarY + 2f, badgeW - 16f, (topBarH - 6f) * 0.52f), $"👑 VIP PILOT • LVL {rank}", profileStyle);
            var streakStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            GUI.Label(new Rect(leftPad + 8f, topBarY + (topBarH - 6f) * 0.48f, badgeW - 16f, (topBarH - 6f) * 0.48f), $"🔥 {GameState.LoginStreak}-DAY STREAK", streakStyle);

            // Top Bar - Center: Cash & High Score
            var centerW = Mathf.Clamp(usableW * 0.45f, 220f, 440f);
            var centerX = w * 0.5f - centerW * 0.5f;
            if (pillBadgeTex != null)
                GUI.DrawTexture(new Rect(centerX, topBarY + 3f, centerW, topBarH - 6f), pillBadgeTex);

            var centerStatStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(centerX, topBarY + 2f, centerW, (topBarH - 6f) * 0.52f),
                $"💰 ${GameState.Cash:N0}    🏆 BEST: {GameState.HighScore:N0}", centerStatStyle);

            var activeBiome = World != null ? World.BiomeName : "Tire District";
            var trackInfoStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.45f, 0.95f, 0.65f) } };
            GUI.Label(new Rect(centerX, topBarY + (topBarH - 6f) * 0.48f, centerW, (topBarH - 6f) * 0.48f),
                $"TRACK: {activeBiome.ToUpper()}  •  {WeatherSystem.Label(World != null ? World.Weather : WeatherKind.Clear).ToUpper()}", trackInfoStyle);

            // Top Bar - Right: Settings Button
            var setBtnW = Mathf.Clamp(usableW * 0.15f, 85f, 125f);
            var setBtnH = topBarH - 6f;
            var setBtnX = w - rightPad - setBtnW;
            var setBtnStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(13 * s) };
            if (GUI.Button(new Rect(setBtnX, topBarY + 3f, setBtnW, setBtnH), "⚙️ SETTINGS", setBtnStyle))
            {
                settingsOpen = !settingsOpen;
            }

            // 2. Left Side: Brand Logo & Vehicle Specs Card
            var contentTop = topBarY + topBarH + Mathf.Max(6f, 8f * s);
            var cardW = Mathf.Clamp(usableW * 0.28f, 190f, 300f);
            var cardH = Mathf.Clamp(usableH * 0.44f, 95f, 150f);

            // Stylized Game Logo
            var logoH = Mathf.Clamp(usableH * 0.11f, 30f, 48f);
            var logoStyle = new GUIStyle(pickerTitleStyle) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(leftPad + 2f, contentTop + 1f, cardW, logoH * 0.65f), "ROAD RAGE", new GUIStyle(logoStyle) { normal = { textColor = Color.black } });
            GUI.Label(new Rect(leftPad, contentTop, cardW, logoH * 0.65f), "ROAD RAGE", logoStyle);

            var subLogoStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(11 * s), normal = { textColor = new Color(1f, 0.82f, 0.2f) } };
            GUI.Label(new Rect(leftPad, contentTop + logoH * 0.55f, cardW, logoH * 0.45f), "⚡ BURNOUT ARCADE RACING ⚡", subLogoStyle);

            // Vehicle Specs Glass Card
            var cardY = contentTop + logoH + 4f;
            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(leftPad, cardY, cardW, cardH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(leftPad, cardY, cardW, cardH), dimTexture);

            var curCar = GameState.CurrentCar;
            var ramName = GameState.TuningRamBar switch { 2 => "TITANIUM RAM", 1 => "STEEL PUSHBAR", _ => "STOCK BUMPER" };
            var inductName = GameState.TuningInduction == 1 ? "TURBOCHARGER (+22 KM/H)" : "SUPERCHARGER (+ACCEL)";

            var lineH = cardH / 4.2f;
            var padIn = 8f;
            var carNameStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(13 * s), normal = { textColor = Color.white } };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f, cardW - padIn * 2, lineH), $"🏎️ {curCar.Name.ToUpper()}", carNameStyle);

            var specStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s) };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH, cardW - padIn * 2, lineH), $"⚡ TOP: {curCar.Speed * 220f:0} KM/H  •  ACC: {curCar.Acceleration:0.0}", specStyle);
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH * 2, cardW - padIn * 2, lineH), $"🛡️ ARMOR: {curCar.Armour:0.0}  •  {ramName}", specStyle);
            var orangeStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), normal = { textColor = new Color(1f, 0.72f, 0.22f) } };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH * 3, cardW - padIn * 2, lineH), $"🔥 {inductName}", orangeStyle);

            // 3. Center-Bottom Hero CTA: "START RUN"
            var pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5.0f);
            var ctaW = Mathf.Clamp(usableW * 0.34f, 200f, 340f);
            var ctaH = Mathf.Clamp(usableH * 0.14f, 42f, 60f);
            var ctaX = w * 0.5f - ctaW * 0.5f;

            var dockBtnH = Mathf.Clamp(usableH * 0.11f, 34f, 46f);
            var dockY = h - botPad - dockBtnH;
            var ctaY = dockY - ctaH - Mathf.Max(6f, 10f * s);

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f * pulse, 1f * pulse, 0.5f * pulse, 1f);
            var ctaStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(20 * s) };
            if (GUI.Button(new Rect(ctaX, ctaY, ctaW, ctaH), "🏁  S T A R T   R U N", ctaStyle))
            {
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.LaunchRun();
            }
            GUI.backgroundColor = oldBg;

            var hintStyle = new GUIStyle(readoutStyle) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(10 * s), normal = { textColor = new Color(0.8f, 0.95f, 1f, 0.85f) } };
            GUI.Label(new Rect(0f, ctaY + ctaH + 1f, w, 16f), "PRESS [SPACE] / [ENTER] OR TAP TO RACE", hintStyle);

            // 4. Bottom Dock Navigation Buttons (Garage, Tracks, Missions, Leaderboard)
            var dockBtnW = Mathf.Clamp(usableW * 0.20f, 85f, 150f);
            var dockSpacing = Mathf.Clamp(usableW * 0.015f, 6f, 14f);
            var totalDockW = dockBtnW * 4 + dockSpacing * 3;
            var dockStartX = w * 0.5f - totalDockW * 0.5f;

            var dockBtnStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(12 * s) };

            // Dock Button 1: GARAGE
            if (GUI.Button(new Rect(dockStartX, dockY, dockBtnW, dockBtnH), "🏎️ GARAGE [G]", dockBtnStyle))
            {
                garageOpen = true;
            }

            // Dock Button 2: TRACKS / BIOMES
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing), dockY, dockBtnW, dockBtnH), "🌐 TRACKS [B]", dockBtnStyle))
            {
                if (World != null) World.OpenPicker();
            }

            // Dock Button 3: DAILY MISSIONS
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 2, dockY, dockBtnW, dockBtnH), "🎯 MISSIONS [M]", dockBtnStyle))
            {
                missionsOpen = true;
            }

            // Dock Button 4: LEADERBOARD
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 3, dockY, dockBtnW, dockBtnH), "🏆 BOARD [L]", dockBtnStyle))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.OpenLeaderboard();
            }

            // Hotkeys
            if (Event.current != null && Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.G) { garageOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.B && World != null) { World.OpenPicker(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.M) { missionsOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.L)
                {
                    if (RoadRageLeaderboardDirector.Instance != null)
                        RoadRageLeaderboardDirector.Instance.ToggleLeaderboard();
                    Event.current.Use();
                }
            }

            // 5. Settings Modal
            if (settingsOpen)
            {
                DrawSettingsModal();
            }
        }

        private bool hasSubmittedRunScore = false;
        private string tempPlayerName = "";

        private void DrawRunOverScreen()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            // Auto-submit score to Leaderboard once per run
            if (!hasSubmittedRunScore)
            {
                hasSubmittedRunScore = true;
                if (RoadRageLeaderboardDirector.Instance != null)
                {
                    RoadRageLeaderboardDirector.Instance.SubmitScore(
                        GameState.Score, GameState.Takedowns, GameState.CurrentCar.Name);
                }
            }

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            // Main AAA Modal Frame
            var modalW = Mathf.Clamp(usableW * 0.88f, 480f, 880f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 320f, 580f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            // Top Header: CRASH REPORT
            var headerH = Mathf.Clamp(modalH * 0.13f, 36f, 54f);
            var bannerStyle = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(26 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.35f, 0.25f) }
            };
            GUI.Label(new Rect(modalX, modalY + 6f, modalW, headerH * 0.65f), "💥 CRASH REPORT  •  RUN CONCLUDED 💥", bannerStyle);

            var subBannerStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.85f, 1f, 0.8f) }
            };
            GUI.Label(new Rect(modalX, modalY + headerH * 0.65f + 4f, modalW, headerH * 0.35f), "ALL BOUNTIES & CASH PERSISTED TO GARAGE VAULT", subBannerStyle);

            var contentY = modalY + headerH + 8f;
            var colSpacing = 16f * s;
            var colW = (modalW - 40f - colSpacing) * 0.5f;

            // --- LEFT CARD: HERO SCORE & COMBAT STATS ---
            var leftCardX = modalX + 20f;
            var cardH = Mathf.Clamp(modalH * 0.44f, 110f, 190f);

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(leftCardX, contentY, colW, cardH), statCardGlassTex);

            var isNewRecord = GameState.Score >= GameState.HighScore && GameState.Score > 0;
            var scoreHeadStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(1f, 0.8f, 0.3f) } };
            GUI.Label(new Rect(leftCardX + 16f, contentY + 8f, colW - 32f, 20f), "FINAL RUN SCORE", scoreHeadStyle);

            var bigScoreStyle = new GUIStyle(titleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(28 * s),
                normal = { textColor = isNewRecord ? new Color(1f, 0.90f, 0.2f) : Color.white }
            };
            var scoreText = $"{GameState.Score:N0}";
            if (isNewRecord) scoreText += "  ⭐ RECORD!";
            GUI.Label(new Rect(leftCardX + 16f, contentY + 26f * s + 4f, colW - 32f, 38f * s), scoreText, bigScoreStyle);

            // Combat mini metrics
            var combatRowY = contentY + 68f * s;
            var metricStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(13 * s), normal = { textColor = Color.white } };
            GUI.Label(new Rect(leftCardX + 16f, combatRowY, colW - 32f, 22f * s), $"💥 TAKEDOWNS: {GameState.Takedowns}  (+{GameState.AftertouchTakedowns} AFTERTOUCH)", metricStyle);
            GUI.Label(new Rect(leftCardX + 16f, combatRowY + 22f * s, colW - 32f, 22f * s), $"🚨 PILEUP WRECKAGE: ${GameState.PileupDamage:N0}", metricStyle);

            // --- RIGHT CARD: CASH EARNED & VEHICLE STATS ---
            var rightCardX = leftCardX + colW + colSpacing;
            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(rightCardX, contentY, colW, cardH), statCardGlassTex);

            var cashHeadStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(0.4f, 1f, 0.6f) } };
            GUI.Label(new Rect(rightCardX + 16f, contentY + 8f, colW - 32f, 20f), "TOTAL REWARDS BANKED", cashHeadStyle);

            var bigCashStyle = new GUIStyle(titleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(28 * s),
                normal = { textColor = new Color(0.35f, 1f, 0.55f) }
            };
            GUI.Label(new Rect(rightCardX + 16f, contentY + 26f * s + 4f, colW - 32f, 38f * s), $"+${GameState.LastRunCash:N0}", bigCashStyle);

            var carSpec = GameState.CurrentCar;
            GUI.Label(new Rect(rightCardX + 16f, combatRowY, colW - 32f, 22f * s), $"🏎️ PILOT CAR: {carSpec.Name.ToUpper()}", metricStyle);
            GUI.Label(new Rect(rightCardX + 16f, combatRowY + 22f * s, colW - 32f, 22f * s), $"🛣️ HIGHWAY DISTANCE: {GameState.RunDistanceKm:0.00} KM", metricStyle);

            // --- DRIVER XP / VIP RANK PROGRESSION ---
            var rankY = contentY + cardH + 10f;
            var rankH = Mathf.Clamp(modalH * 0.12f, 32f, 48f);
            var rankCardW = modalW - 40f;

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, rankY, rankCardW, rankH), statCardGlassTex);

            var rank = Mathf.Max(1, GameState.Takedowns / 5 + 1);
            var xpFrac = Mathf.Clamp01((GameState.Takedowns % 5) / 5f);

            var rankTitleStyle = new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.82f, 0.25f) } };
            GUI.Label(new Rect(modalX + 34f, rankY, rankCardW * 0.35f, rankH), $"👑 VIP PILOT • RANK {rank}", rankTitleStyle);

            var barX = modalX + 34f + rankCardW * 0.35f;
            var barW = rankCardW * 0.58f;
            var barH = rankH * 0.44f;
            var barY = rankY + (rankH - barH) * 0.5f;

            GUI.DrawTexture(new Rect(barX, barY, barW, barH), dimTexture);
            var prevGUIColor = GUI.color;
            GUI.color = new Color(1f, 0.78f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, barW * xpFrac, barH), Texture2D.whiteTexture);
            GUI.color = prevGUIColor;

            var xpLabelStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(11 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(barX, barY, barW, barH), $"{Mathf.RoundToInt(xpFrac * 100)}% TO RANK {rank + 1}", xpLabelStyle);

            // --- ACTION BUTTONS DOCK ---
            var btnRowY = rankY + rankH + 12f;
            var btnH = Mathf.Clamp(modalH * 0.15f, 42f, 56f);
            var btnSpacing = 10f * s;
            var btnW = (modalW - 40f - btnSpacing * 3) / 4f;

            var actionBtnStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(14 * s) };

            // Button 1: PLAY AGAIN
            var pulse = 0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f * pulse, 0.95f * pulse, 0.45f * pulse);
            if (greenBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, btnRowY, btnW, btnH), greenBtnTex);

            if (GUI.Button(new Rect(modalX + 20f, btnRowY, btnW, btnH), "🏁 PLAY AGAIN [SPACE]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space))
            {
                hasSubmittedRunScore = false;
                GameState.BeginRun();
                if (World != null) World.ReloadBiome(World.BiomeName);
                if (Event.current != null) Event.current.Use();
                return;
            }
            GUI.backgroundColor = oldBg;

            // Button 2: LEADERBOARD
            if (blueBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f + (btnW + btnSpacing), btnRowY, btnW, btnH), blueBtnTex);

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing), btnRowY, btnW, btnH), "🏆 BOARD [L]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.L))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.OpenLeaderboard();
                if (Event.current != null) Event.current.Use();
            }

            // Button 3: GARAGE
            if (orangeBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f + (btnW + btnSpacing) * 2, btnRowY, btnW, btnH), orangeBtnTex);

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 2, btnRowY, btnW, btnH), "🏎️ GARAGE [G]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.G))
            {
                hasSubmittedRunScore = false;
                garageOpen = true;
                if (Event.current != null) Event.current.Use();
            }

            // Button 4: MAIN MENU
            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 3, btnRowY, btnW, btnH), "🏠 MENU [ESC]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                hasSubmittedRunScore = false;
                GameState.BeginRun();
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.ReturnToLanding();
                if (World != null) World.ReloadBiome(World.BiomeName);
                if (Event.current != null) Event.current.Use();
            }
        }

        private void DrawLeaderboardModal()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            var modalW = Mathf.Clamp(usableW * 0.88f, 480f, 880f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 340f, 580f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            // Title & Status
            var titleStyleL = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(24 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.82f, 0.2f) }
            };
            GUI.Label(new Rect(modalX, modalY + 8f, modalW, 30f * s), "🏆 GLOBAL ARCADE LEADERBOARD 🏆", titleStyleL);

            var statusMsg = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.StatusMessage : "Ready";
            var statusStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.65f, 0.85f, 1f) }
            };
            GUI.Label(new Rect(modalX, modalY + 36f * s, modalW, 20f), statusMsg, statusStyle);

            // Pilot Tag Selector Bar
            var currentName = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.PlayerName : "RoadWarrior";
            if (string.IsNullOrEmpty(tempPlayerName)) tempPlayerName = currentName;

            var nameRowY = modalY + 56f * s;
            var nameTagW = 140f * s;
            var nameFieldW = 180f * s;
            var nameTotalW = nameTagW + nameFieldW;
            var nameStartX = modalX + modalW * 0.5f - nameTotalW * 0.5f;

            GUI.Label(new Rect(nameStartX, nameRowY, nameTagW, 26f), "✏️ YOUR PILOT TAG:", new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) });
            tempPlayerName = GUI.TextField(new Rect(nameStartX + nameTagW, nameRowY, nameFieldW, 26f), tempPlayerName, 14);
            if (tempPlayerName != currentName && RoadRageLeaderboardDirector.Instance != null)
            {
                RoadRageLeaderboardDirector.Instance.PlayerName = tempPlayerName;
            }

            // Table Header
            var tableHeaderY = nameRowY + 34f;
            var tableX = modalX + 24f;
            var tableW = modalW - 48f;
            var rowH = Mathf.Clamp((modalH - 180f) / 7.2f, 26f, 38f);

            var colRankW = 70f * s;
            var colNameW = 180f * s;
            var colCarW = 140f * s;
            var colTdW = 90f * s;
            var colScoreW = tableW - colRankW - colNameW - colCarW - colTdW;

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(tableX, tableHeaderY, tableW, 26f), statCardGlassTex);

            var thStyle = new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(0.6f, 0.8f, 1f) } };
            GUI.Label(new Rect(tableX + 8f, tableHeaderY + 2f, colRankW, 22f), "RANK", thStyle);
            GUI.Label(new Rect(tableX + colRankW, tableHeaderY + 2f, colNameW, 22f), "PILOT", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW, tableHeaderY + 2f, colCarW, 22f), "VEHICLE", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW, tableHeaderY + 2f, colTdW, 22f), "WRECKS", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW + colTdW, tableHeaderY + 2f, colScoreW - 8f, 22f), "HIGH SCORE", new GUIStyle(thStyle) { alignment = TextAnchor.MiddleRight });

            // Table Rows
            var entries = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.CachedEntries : new List<LeaderboardEntryData>();
            var rowStartY = tableHeaderY + 28f;

            var maxRows = Mathf.Min(7, entries.Count);
            for (int i = 0; i < maxRows; i++)
            {
                var entry = entries[i];
                var currentY = rowStartY + i * (rowH + 3f);
                var isMe = entry.Username == currentName;

                var bgTex = isMe ? rowHighlightTex : (i % 2 == 0 ? rowEvenTex : rowOddTex);
                if (bgTex != null)
                {
                    GUI.DrawTexture(new Rect(tableX, currentY, tableW, rowH), bgTex);
                }

                var rankBadge = entry.Rank switch
                {
                    1 => "🥇  #1",
                    2 => "🥈  #2",
                    3 => "🥉  #3",
                    _ => $"    #{entry.Rank}"
                };

                var rankColor = entry.Rank switch
                {
                    1 => new Color(1f, 0.85f, 0.25f),
                    2 => new Color(0.85f, 0.90f, 1f),
                    3 => new Color(0.95f, 0.65f, 0.40f),
                    _ => Color.white
                };

                var rowTextStyle = new GUIStyle(readoutStyle)
                {
                    font = arcadeFont,
                    fontSize = Mathf.RoundToInt(13 * s),
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = isMe ? new Color(1f, 0.90f, 0.25f) : rankColor }
                };

                var displayName = isMe ? $"{entry.Username}  (YOU)" : entry.Username;

                GUI.Label(new Rect(tableX + 8f, currentY, colRankW, rowH), rankBadge, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW, currentY, colNameW, rowH), displayName, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW, currentY, colCarW, rowH), entry.CarName, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW, currentY, colTdW, rowH), $"{entry.Takedowns} ⭐", rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW + colTdW, currentY, colScoreW - 8f, rowH), $"{entry.Score:N0} PTS", new GUIStyle(rowTextStyle) { alignment = TextAnchor.MiddleRight, font = titleFont });
            }

            // Bottom Buttons
            var botBtnY = modalY + modalH - 48f;
            var botBtnW = 140f * s;
            var botBtnStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) };

            if (blueBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + modalW * 0.5f - botBtnW - 10f, botBtnY, botBtnW, 40f), blueBtnTex);

            if (GUI.Button(new Rect(modalX + modalW * 0.5f - botBtnW - 10f, botBtnY, botBtnW, 40f), "🔄 REFRESH", botBtnStyle))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.FetchOnlineScores();
            }

            if (GUI.Button(new Rect(modalX + modalW * 0.5f + 10f, botBtnY, botBtnW, 40f), "✖ CLOSE [ESC]", botBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.L)))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.CloseLeaderboard();
                if (Event.current != null) Event.current.Use();
            }
        }

        private void DrawSettingsModal()
        {
            var w = Screen.width;
            var h = Screen.height;
            var modalW = Mathf.Min(w * 0.7f, 400f);
            var modalH = Mathf.Min(h * 0.65f, 300f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);
            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            var s = UiScale;
            var titleS = new GUIStyle(pickerTitleStyle) { fontSize = Mathf.RoundToInt(24 * s) };
            GUI.Label(new Rect(modalX, modalY + 14f, modalW, 30f), "SETTINGS", titleS);

            var rowY = modalY + 54f;
            var rowH = Mathf.Clamp(modalH * 0.14f, 32f, 42f);

            var btnS = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(14 * s) };

            // Audio SFX Toggle
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"SOUND EFFECTS: {(sfxEnabled ? "ON 🔊" : "OFF 🔇")}", btnS))
            {
                sfxEnabled = !sfxEnabled;
                AudioListener.volume = sfxEnabled ? 1f : 0f;
            }
            rowY += rowH + 8f;

            // FPS Display Toggle
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"FPS COUNTER: {(showFps ? "VISIBLE" : "HIDDEN")}", btnS))
            {
                showFps = !showFps;
            }
            rowY += rowH + 8f;

            // Target Framerate
            var currentFps = Application.targetFrameRate;
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"TARGET FPS: {currentFps} FPS", btnS))
            {
                Application.targetFrameRate = currentFps == 120 ? 60 : 120;
            }
            rowY += rowH + 14f;

            // Close Button
            if (GUI.Button(new Rect(modalX + modalW * 0.5f - 70f, rowY, 140f, rowH), "CLOSE", btnS))
            {
                settingsOpen = false;
            }
        }

        private static int pickerCursorIndex = 0;
        private static float lastNavTime = 0f;

        private void HandlePickerGamepadInput(IReadOnlyList<string> playable, int columns)
        {
            if (Event.current != null && Event.current.type != EventType.Layout) return;
            if (Time.unscaledTime - lastNavTime < 0.18f) return;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;

            var left = (pad != null && (pad.dpad.left.isPressed || pad.leftStick.left.isPressed)) ||
                       (kb != null && (kb.leftArrowKey.isPressed || kb.aKey.isPressed));
            var right = (pad != null && (pad.dpad.right.isPressed || pad.leftStick.right.isPressed)) ||
                        (kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed));
            var up = (pad != null && (pad.dpad.up.isPressed || pad.leftStick.up.isPressed)) ||
                     (kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed));
            var down = (pad != null && (pad.dpad.down.isPressed || pad.leftStick.down.isPressed)) ||
                       (kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed));
            var confirm = (pad != null && pad.buttonSouth.wasPressedThisFrame) ||
                          (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame));
            var cancel = (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) ||
                         (kb != null && kb.escapeKey.wasPressedThisFrame);

            if (left) { pickerCursorIndex = Mathf.Max(0, pickerCursorIndex - 1); lastNavTime = Time.unscaledTime; }
            if (right) { pickerCursorIndex = Mathf.Min(playable.Count - 1, pickerCursorIndex + 1); lastNavTime = Time.unscaledTime; }
            if (up) { pickerCursorIndex = Mathf.Max(0, pickerCursorIndex - columns); lastNavTime = Time.unscaledTime; }
            if (down) { pickerCursorIndex = Mathf.Min(playable.Count - 1, pickerCursorIndex + columns); lastNavTime = Time.unscaledTime; }

            if (confirm && pickerCursorIndex >= 0 && pickerCursorIndex < playable.Count)
            {
                Debug.Log($"[BIOME] Selected with gamepad/keyboard: {playable[pickerCursorIndex]}");
                var w = World;
                if (w != null) w.SelectBiome(playable[pickerCursorIndex]);
            }
        }

        private void Update()
        {
            var w = World;
            if (w == null || !w.PickerOpen) return;

            Vector2? pressPos = null;
            try
            {
                var touch = UnityEngine.InputSystem.Touchscreen.current;
                if (touch != null && (touch.primaryTouch.press.wasPressedThisFrame || touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
                {
                    var p = touch.primaryTouch.position.ReadValue();
                    pressPos = new Vector2(p.x, Screen.height - p.y);
                }
                else
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame))
                    {
                        var p = mouse.position.ReadValue();
                        pressPos = new Vector2(p.x, Screen.height - p.y);
                    }
                    else
                    {
                        var ptr = UnityEngine.InputSystem.Pointer.current;
                        if (ptr != null && (ptr.press.wasPressedThisFrame || ptr.press.isPressed || ptr.press.wasReleasedThisFrame))
                        {
                            var p = ptr.position.ReadValue();
                            pressPos = new Vector2(p.x, Screen.height - p.y);
                        }
                    }
                }
            }
            catch {}

            if (pressPos.HasValue)
            {
                var pos = pressPos.Value;
                var playable = RoadRageBootstrap.PlayableBiomes;
                var locked = RoadRageBootstrap.LockedBiomes;
                var columns = Screen.width < 720 ? 2 : 3;
                var panelWidth = Mathf.Min(Screen.width * 0.92f, 1040f);
                var left = (Screen.width - panelWidth) * 0.5f;
                var gap = 12f;
                var cardWidth = (panelWidth - gap * (columns - 1)) / columns;
                var rows = Mathf.CeilToInt((playable.Count + locked.Count) / (float)columns);
                var cardHeight = Mathf.Clamp((Screen.height * 0.62f - gap * (rows - 1)) / rows, 44f, 82f);
                var gridTop = Screen.height * 0.5f - (cardHeight * rows + gap * (rows - 1)) * 0.5f + 24f;

                for (var i = 0; i < playable.Count; i++)
                {
                    var rect = CardRect(left, gridTop, i, columns, cardWidth, cardHeight, gap);
                    if (rect.Contains(pos))
                    {
                        Debug.Log($"[BIOME] Card tapped in Update: {playable[i]}");
                        pickerCursorIndex = i;
                        w.SelectBiome(playable[i]);
                        return;
                    }
                }

                var closeWidth = Mathf.Min(panelWidth * 0.4f, 260f);
                var closeTop = gridTop + rows * (cardHeight + gap) + 14f;
                var driveRect = new Rect(Screen.width * 0.5f - closeWidth * 0.5f, closeTop, closeWidth, 52f);
                if (driveRect.Contains(pos))
                {
                    w.ClosePicker();
                    return;
                }
            }
        }

        private void DrawPicker()
        {
            var w = World;
            if (w == null) return;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTexture);

            var playable = RoadRageBootstrap.PlayableBiomes;
            var locked = RoadRageBootstrap.LockedBiomes;
            var columns = Screen.width < 720 ? 2 : 3;
            var panelWidth = Mathf.Min(Screen.width * 0.92f, 1040f);
            var left = (Screen.width - panelWidth) * 0.5f;
            var gap = 12f;
            var cardWidth = (panelWidth - gap * (columns - 1)) / columns;
            var rows = Mathf.CeilToInt((playable.Count + locked.Count) / (float)columns);
            var cardHeight = Mathf.Clamp((Screen.height * 0.62f - gap * (rows - 1)) / rows, 44f, 82f);
            var gridTop = Screen.height * 0.5f - (cardHeight * rows + gap * (rows - 1)) * 0.5f + 24f;

            HandlePickerGamepadInput(playable, columns);

            GUI.Label(new Rect(left, gridTop - 92f, panelWidth, 44f), "SELECT BIOME", pickerTitleStyle);
            GUI.Label(new Rect(left, gridTop - 50f, panelWidth, 28f),
                $"{playable.Count} PLAYABLE  •  {locked.Count} COMING SOON  (Click, D-Pad/A, or Keys 1-0)", readoutStyle);

            for (var i = 0; i < playable.Count; i++)
            {
                var rect = CardRect(left, gridTop, i, columns, cardWidth, cardHeight, gap);
                var isCurrent = playable[i] == w.BiomeName;
                var isSelected = pickerCursorIndex == i;
                var previousColor = GUI.backgroundColor;
                if (isCurrent) GUI.backgroundColor = new Color(0.28f, 0.92f, 0.55f);
                else if (isSelected) GUI.backgroundColor = new Color(0.35f, 0.70f, 1f);

                var digit = (i + 1) % 10;
                var label = isCurrent ? $"▶ [{digit}] {playable[i]}" : (isSelected ? $"★ [{digit}] {playable[i]}" : $"[{digit}] {playable[i]}");
                
                if (GUI.Button(rect, label, buttonStyle))
                {
                    pickerCursorIndex = i;
                    Debug.Log($"[BIOME] Card clicked in GUI.Button: {playable[i]}");
                    w.SelectBiome(playable[i]);
                    GUI.backgroundColor = previousColor;
                    return;
                }
                GUI.backgroundColor = previousColor;
            }

            for (var i = 0; i < locked.Count; i++)
            {
                var rect = CardRect(left, gridTop, playable.Count + i, columns, cardWidth, cardHeight, gap);
                var previousEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, $"{locked[i]}\nSOON", lockedStyle);
                GUI.enabled = previousEnabled;
            }

            var closeWidth = Mathf.Min(panelWidth * 0.4f, 260f);
            var closeTop = gridTop + rows * (cardHeight + gap) + 14f;
            var driveRect = new Rect(Screen.width * 0.5f - closeWidth * 0.5f, closeTop, closeWidth, 52f);
            if (GUI.Button(driveRect, "DRIVE (ESC / B)", buttonStyle))
            {
                w.ClosePicker();
                return;
            }
        }

        private static Rect CardRect(float left, float top, int index, int columns,
            float cardWidth, float cardHeight, float gap)
        {
            var column = index % columns;
            var row = index / columns;
            return new Rect(left + column * (cardWidth + gap), top + row * (cardHeight + gap), cardWidth, cardHeight);
        }
    }

    public static class GameInput
    {
        public static bool GetEscapePressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null && (pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame)) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetBKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.bKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.B)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetNKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.nKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.N)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetGKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.gKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.G)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetNumberKey(int digit)
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    var pressed = digit switch
                    {
                        1 => kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame,
                        2 => kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame,
                        3 => kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame,
                        4 => kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame,
                        5 => kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame,
                        6 => kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame,
                        7 => kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame,
                        8 => kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame,
                        9 => kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame,
                        0 => kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame,
                        _ => false
                    };
                    if (pressed) return true;
                }
            }
            catch {}

            try
            {
                return digit switch
                {
                    1 => Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1),
                    2 => Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2),
                    3 => Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3),
                    4 => Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4),
                    5 => Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5),
                    6 => Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6),
                    7 => Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7),
                    8 => Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8),
                    9 => Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9),
                    0 => Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0),
                    _ => false
                };
            }
            catch { return false; }
        }

        public static float GetSteer()
        {
            var steer = 0f;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                }
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null)
                {
                    var stick = pad.leftStick.x.ReadValue();
                    var dpad = pad.dpad.x.ReadValue();
                    if (Mathf.Abs(stick) > 0.12f) steer += stick;
                    else if (Mathf.Abs(dpad) > 0.12f) steer += dpad;
                }
            }
            catch {}

            try
            {
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;
                var axis = Input.GetAxisRaw("Horizontal");
                if (Mathf.Abs(axis) > 0.1f) steer += axis;
            }
            catch {}

            return Mathf.Clamp(steer, -1f, 1f);
        }

        public static float GetThrottle()
        {
            var throttle = 0f;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed || kb.spaceKey.isPressed) throttle -= 1f;
                }
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null)
                {
                    var rt = pad.rightTrigger.ReadValue();
                    var lt = pad.leftTrigger.ReadValue();
                    if (rt > 0.05f) throttle += rt;
                    if (lt > 0.05f) throttle -= lt;
                    if (pad.buttonSouth.isPressed) throttle += 1f; // A button
                    if (pad.buttonEast.isPressed || pad.buttonWest.isPressed) throttle -= 1f; // B/X button
                    var stickY = pad.leftStick.y.ReadValue();
                    if (Mathf.Abs(stickY) > 0.15f) throttle += stickY;
                }
            }
            catch {}

            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.Space)) throttle -= 1f;
                var axis = Input.GetAxisRaw("Vertical");
                if (Mathf.Abs(axis) > 0.1f) throttle += axis;
            }
            catch {}

            return Mathf.Clamp(throttle, -1f, 1f);
        }
    }

    /// <summary>
    /// Smoothly keeps the distant horizon mountain ring and sky dome centered on the camera
    /// so distant mountains and clouds are 100% static with zero popping and zero chunk rebuilds.
    /// </summary>
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
