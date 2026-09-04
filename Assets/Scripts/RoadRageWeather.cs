using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRage.UnityRemake
{
    public enum WeatherKind
    {
        Clear,
        Rain,
        Storm,
        Snow,
        /// No precipitation, just air you cannot see through. Cheap - it is fog density
        /// and a tint, no particles at all - and it is the one condition that changes how
        /// a city reads rather than how wet it is: a canyon of towers with the tops lost
        /// is a different street from the same one in the rain.
        Fog,
    }

    /// Weather layers on top of a biome's BiomeMood rather than replacing it: the biome
    /// still sets its own palette, and weather shifts fog/sun/wetness relative to that.
    /// Precipitation is a single camera-parented emitter in world simulation space, so the
    /// particles stay put in the world while the box follows the player - one system for
    /// the whole storm instead of emitters scattered down the road.
    public sealed class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Active { get; private set; }

        public WeatherKind Kind { get; private set; }

        private ParticleSystem precipitation;
        private ParticleSystem spray;
        private Transform followTarget;

        public struct WeatherEffect
        {
            public float FogDensityScale;
            public Color FogTint;
            public float FogTintAmount;
            public float SunScale;
            public float WetnessAdd;
            public float ExposureAdd;
        }

        public static WeatherEffect EffectFor(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => new WeatherEffect
            {
                FogDensityScale = 1.45f, FogTint = new Color(0.34f, 0.38f, 0.45f), FogTintAmount = 0.40f,
                SunScale = 0.68f, WetnessAdd = 0.85f, ExposureAdd = 0.02f
            },
            WeatherKind.Storm => new WeatherEffect
            {
                FogDensityScale = 2.0f, FogTint = new Color(0.24f, 0.27f, 0.33f), FogTintAmount = 0.55f,
                SunScale = 0.48f, WetnessAdd = 1f, ExposureAdd = -0.08f
            },
            WeatherKind.Snow => new WeatherEffect
            {
                FogDensityScale = 2.4f, FogTint = new Color(0.72f, 0.78f, 0.86f), FogTintAmount = 0.55f,
                SunScale = 0.62f, WetnessAdd = 0.25f, ExposureAdd = -0.05f
            },
            // Denser than storm and much paler, because fog scatters light rather than
            // blocking it. A little wetness so the road still catches the streetlamps -
            // dry asphalt under heavy fog reads as dusty, not damp.
            WeatherKind.Fog => new WeatherEffect
            {
                FogDensityScale = 3.4f, FogTint = new Color(0.58f, 0.61f, 0.66f), FogTintAmount = 0.70f,
                SunScale = 0.42f, WetnessAdd = 0.35f, ExposureAdd = -0.02f
            },
            _ => new WeatherEffect
            {
                FogDensityScale = 1f, FogTint = Color.white, FogTintAmount = 0f,
                SunScale = 1f, WetnessAdd = 0f, ExposureAdd = 0f
            },
        };

        /// Which weather each biome can roll. Sewer is enclosed so it stays clear; the
        /// ice station is always snowing; the neon cities lean wet because that is what
        /// makes their reflections read.
        public static WeatherKind[] OptionsForBiome(int biomeIndex) => biomeIndex switch
        {
            1 => new[] { WeatherKind.Snow, WeatherKind.Snow, WeatherKind.Clear },          // SNOW STATION
            2 => new[] { WeatherKind.Clear },                                              // SEWER (enclosed)
            3 => new[] { WeatherKind.Clear, WeatherKind.Rain },                            // TIRE DISTRICT
            4 => new[] { WeatherKind.Clear, WeatherKind.Rain },                            // ALIEN BIOMASS
            5 => new[] { WeatherKind.Rain, WeatherKind.Clear, WeatherKind.Storm, WeatherKind.Fog }, // NEON CITY
            6 => new[] { WeatherKind.Clear },                                              // RED CANYON (desert)
            7 => new[] { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Fog },            // HONG KONG
            8 => new[] { WeatherKind.Rain, WeatherKind.Storm, WeatherKind.Clear, WeatherKind.Fog }, // MANHATTAN
            9 => new[] { WeatherKind.Clear, WeatherKind.Clear, WeatherKind.Rain },          // HOLLYWOOD HILLS
            _ => new[] { WeatherKind.Clear, WeatherKind.Clear, WeatherKind.Rain },         // GREENWOOD
        };

        public static WeatherKind Roll(int biomeIndex)
        {
            var options = OptionsForBiome(biomeIndex);
            return options[Random.Range(0, options.Length)];
        }

        public void Configure(WeatherKind kind, Transform follow, Material particleMaterial)
        {
            Active = this;
            Kind = kind;
            followTarget = follow;

            if (precipitation != null) Destroy(precipitation.gameObject);
            if (spray != null) Destroy(spray.gameObject);
            // Fog has no particles. It is entirely fog density, tint and sun scale, which
            // BuildLighting already applies from the effect - so there is nothing to emit
            // and nothing to spray off the tyres beyond the wetness it adds.
            if (kind == WeatherKind.Clear || kind == WeatherKind.Fog) return;

            precipitation = BuildPrecipitation(kind, particleMaterial);
            if (kind != WeatherKind.Snow) spray = BuildSpray(particleMaterial);
        }

        /// Precipitation is transparent overdraw, which is the single most expensive thing
        /// you can put on a mobile GPU. Desktop absorbed a 1177 -> 66 FPS drop; phones
        /// will not, so the particle budget scales with the quality tier.
        private static float ParticleBudget =>
            Application.isMobilePlatform || QualitySettings.GetQualityLevel() <= 1 ? 0.3f : 1f;

        private ParticleSystem BuildPrecipitation(WeatherKind kind, Material particleMaterial)
        {
            var snow = kind == WeatherKind.Snow;
            var storm = kind == WeatherKind.Storm;
            var budget = ParticleBudget;
            var system = new GameObject($"{kind} Precipitation").AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetParent(transform, false);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = snow ? 4.5f : 1.4f;
            main.startSpeed = snow ? 2.4f : storm ? 34f : 24f;
            // Drops must be thin: at 0.14 they read as falling sticks, not rain.
            main.startSize = snow ? 0.16f : 0.035f;
            main.maxParticles = Mathf.RoundToInt((snow ? 2200 : storm ? 4200 : 2800) * budget);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(snow
                ? new Color(1f, 1f, 1f, 0.75f)
                : new Color(0.78f, 0.85f, 0.96f, 0.30f));
            // Rain streaks fall near-vertical with a wind lean; snow drifts.
            main.gravityModifier = snow ? 0.05f : 0f;

            var emission = system.emission;
            emission.rateOverTime = (snow ? 700f : storm ? 2600f : 1600f) * budget;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Narrower emitter on constrained hardware: fewer particles spread over a
            // smaller volume keeps apparent density instead of thinning the rain out.
            shape.scale = new Vector3(budget < 1f ? 42f : 70f, 1f, budget < 1f ? 42f : 70f);
            shape.position = new Vector3(0f, snow ? 22f : 26f, 12f);
            shape.rotation = new Vector3(90f, 0f, 0f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // The wind cue: a constant lateral drift, stronger in a storm.
            velocity.x = new ParticleSystem.MinMaxCurve(snow ? 2.2f : storm ? 9f : 4f);
            velocity.z = new ParticleSystem.MinMaxCurve(snow ? -1.4f : -2f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = particleMaterial;
            renderer.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            if (!snow)
            {
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = storm ? 4.5f : 3.2f;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
            system.Play();
            return system;
        }

        /// Wheel spray - sells wetness far better than the road material alone.
        private ParticleSystem BuildSpray(Material particleMaterial)
        {
            var system = new GameObject("Road Spray").AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetParent(transform, false);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.88f, 0.95f, 0.09f));

            var emission = system.emission;
            emission.rateOverTime = 90f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.4f, 0.1f, 1.2f);

            var overLifetime = system.sizeOverLifetime;
            overLifetime.enabled = true;
            overLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private void LateUpdate()
        {
            if (followTarget == null) return;
            // Keep the emitter volume over the player. World simulation space means the
            // already-spawned particles stay where they were, so moving the box does not
            // drag the storm along with the car.
            if (precipitation != null)
                precipitation.transform.position = followTarget.position;
            if (spray != null)
                spray.transform.position = followTarget.position - followTarget.forward * 2.6f + Vector3.up * 0.15f;
        }

        public static string Label(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => "RAIN",
            WeatherKind.Storm => "STORM",
            WeatherKind.Snow => "SNOW",
            _ => "CLEAR",
        };
    }
}
