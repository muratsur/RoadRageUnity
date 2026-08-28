using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Game systems ported from the shipped Godot build (RoadRage3D/game.gd).
    /// Godot persisted to ConfigFile; Unity uses PlayerPrefs so it works on every
    /// platform without a writable path, but the keys and semantics match 1:1 so a
    /// player's numbers mean the same thing in both builds.
    public static class GameState
    {
        public const int UpgradeMax = 6;
        public const float ComboWindow = 3.2f;

        // Consecutive-day login rewards, capped at the final entry.
        public static readonly int[] LoginBonus = { 200, 300, 400, 600, 800, 1000, 1500 };

        public struct CarSpec
        {
            public string Name;
            public string Mesh;
            public int Price;
            public float Speed;
            public float Acceleration;
            public float Armour;
            public string Description;
            public string Livery;
        }

        /// Truck-led lineup: this is a ram-the-rule-breakers game, so armour is the stat
        /// that matters and pickups earn their place. Meshes are Synty Street Racer
        /// presets sharing PolygonStreetRacer_Texture_01_A. Armour scales impact
        /// resistance; speed/acceleration multiply the car's handling.
        public static readonly CarSpec[] Cars =
        {
            new() { Name = "RUSTY UTE",  Mesh = "SK_Veh_Preset_Ute_01",    Price = 0,     Speed = 1.00f, Acceleration = 1.00f, Armour = 1.15f, Description = "Workhorse pickup. Free, and already hits harder than a car.", Livery = "PolygonStreetRacer_Veh_Tex_24_Rust"},
            new() { Name = "STREET BIKE",Mesh = "SK_Veh_Preset_Motorbike_01", Price = 1500, Speed = 1.28f, Acceleration = 1.34f, Armour = 0.55f, Description = "Fast and fragile. Every hit hurts — for players who dodge.", Livery = "PolygonStreetRacer_Veh_Tex_07_Race_Yellow"},
            new() { Name = "STREET UTE", Mesh = "SK_Veh_Preset_Ute_02",    Price = 2500,  Speed = 1.12f, Acceleration = 1.12f, Armour = 1.10f, Description = "Car-bodied ute. Quick, low, still has a tray.", Livery = "PolygonStreetRacer_Veh_Tex_RR_Orange"},
            new() { Name = "TRAIL 4X4",  Mesh = "SK_Veh_Preset_Ute_03",    Price = 4200,  Speed = 1.04f, Acceleration = 0.98f, Armour = 1.45f, Description = "Lifted pickup with a light bar. Built for shoving.", Livery = "PolygonStreetRacer_Veh_Tex_07_Race_Yellow"},
            new() { Name = "BRUTE PICKUP", Mesh = "SK_Veh_Preset_Ute_04",  Price = 6500,  Speed = 1.08f, Acceleration = 1.00f, Armour = 1.60f, Description = "The big pickup. Heavy front end, hard to stop.", Livery = "PolygonStreetRacer_Veh_Tex_13_Race_Blue"},
            new() { Name = "SUPERBIKE",  Mesh = "SK_Veh_Preset_Motorbike_03", Price = 8000, Speed = 1.44f, Acceleration = 1.48f, Armour = 0.60f, Description = "Fastest thing in the game. Touch anything and it's over.", Livery = "PolygonStreetRacer_Veh_Tex_03_Carbon_Fibre"},
            new() { Name = "BOX TRUCK",  Mesh = "SK_Veh_Preset_Truck_03",  Price = 9000,  Speed = 0.98f, Acceleration = 0.88f, Armour = 1.90f, Description = "Rigid box truck. Slow off the line, moves anything.", Livery = "PolygonStreetRacer_Veh_Tex_13_Race_Blue"},
            new() { Name = "RIG",        Mesh = "SK_Veh_Preset_Truck_01",  Price = 12000, Speed = 1.06f, Acceleration = 0.90f, Armour = 2.05f, Description = "Semi cab. Full-size rig — traffic gets out of the way.", Livery = "PolygonStreetRacer_Veh_Tex_RR_Orange"},
            new() { Name = "ENFORCER",   Mesh = "SK_Veh_Preset_Truck_02",  Price = 15000, Speed = 1.12f, Acceleration = 0.95f, Armour = 2.20f, Description = "Armoured rig. The vigilante's truck.", Livery = "PolygonStreetRacer_Veh_Tex_24_Rust"},
            new() { Name = "JUGGERNAUT", Mesh = "SK_Veh_Preset_Truck_04",  Price = 22000, Speed = 1.22f, Acceleration = 1.02f, Armour = 2.45f, Description = "The ultimate ram. Fast, and effectively unstoppable.", Livery = "PolygonStreetRacer_Veh_Tex_03_Carbon_Fibre"},
        };

        public struct MissionSpec
        {
            public string Key;
            public float Goal;
            public int Reward;
            public string Description;
        }

        public static readonly MissionSpec[] MissionPool =
        {
            new() { Key = "takedowns", Goal = 12,   Reward = 600,  Description = "Take down {0} rule-breakers" },
            new() { Key = "takedowns", Goal = 25,   Reward = 1300, Description = "Take down {0} rule-breakers" },
            new() { Key = "distance",  Goal = 6,    Reward = 700,  Description = "Drive {0} km (any mode)" },
            new() { Key = "distance",  Goal = 12,   Reward = 1500, Description = "Drive {0} km (any mode)" },
            new() { Key = "combo",     Goal = 8,    Reward = 800,  Description = "Hit a x{0} combo" },
            new() { Key = "nearmiss",  Goal = 15,   Reward = 600,  Description = "Pull off {0} near misses" },
            new() { Key = "cash",      Goal = 2500, Reward = 900,  Description = "Bank ${0} today" },
            new() { Key = "endless",   Goal = 4,    Reward = 1000, Description = "Reach {0} km in Endless Chase" },
        };

        // ---- run state (not persisted) ----
        public static int Score;
        public static int Combo;
        public static float ComboTimer;
        public static string Message = string.Empty;
        public static float MessageTimer;
        public static int Takedowns;
        public static float RunDistanceKm;

        /// Run state. Without these the run never ended, AwardCash was never called, and
        /// the garage economy was unreachable by playing - only the login bonus fed it.
        public const float MaxIntegrity = 100f;
        public static float Integrity = MaxIntegrity;
        public static bool RunOver;
        public static int LastRunCash;
        public static int InnocentsHit;
        public static int RunStartScore;

        // ---- Aftertouch & Crashbreaker state ----
        public static bool IsAftertouchActive;
        public static int AftertouchTakedowns;
        public static int PileupDamage;
        public static bool CrashbreakerReady;
        public static bool CrashbreakerUsed;

        // ---- persisted ----
        public static int Cash;
        public static int UpgradeEngine;
        public static int UpgradeArmour;
        public static int UpgradeBoost;
        public static List<int> OwnedCars = new() { 0 };
        public static int SelectedCar;

        public static string MissionDay = string.Empty;
        public static List<int> MissionIds = new();
        public static List<bool> MissionClaimed = new();
        public static int LoginStreak;
        public static int LastLoginReward;
        public static readonly Dictionary<string, float> Daily = new()
        {
            { "takedowns", 0f }, { "distance", 0f }, { "combo", 0f },
            { "nearmiss", 0f }, { "cash", 0f }, { "endless", 0f },
        };

        public static int ComboMultiplier => Mathf.Clamp(1 + Combo / 3, 1, 10);
        public static int UpgradeCost(int level) => 800 + level * 700;
        public static CarSpec CurrentCar => Cars[Mathf.Clamp(SelectedCar, 0, Cars.Length - 1)];

        public static int UpgradeLevel(string key) => key switch
        {
            "engine" => UpgradeEngine,
            "armor" => UpgradeArmour,
            _ => UpgradeBoost,
        };

        /// Arcade scoring event: scales by the current combo multiplier and extends the combo.
        public static void Award(int points, string message)
        {
            var multiplier = ComboMultiplier;
            Score += points * multiplier;
            Combo++;
            ComboTimer = ComboWindow;
            BumpDaily("combo", ComboMultiplier, true);
            Show($"{message}  +{points * multiplier}");
        }

        public static void Show(string text)
        {
            Message = text;
            MessageTimer = 1.6f;
        }

        public static void Tick(float delta)
        {
            if (ComboTimer > 0f)
            {
                ComboTimer -= delta;
                if (ComboTimer <= 0f) Combo = 0;
            }
            if (MessageTimer > 0f)
            {
                MessageTimer -= delta;
                if (MessageTimer <= 0f) Message = string.Empty;
            }
        }

        /// Cumulative counter, or a running max for combo/endless.
        public static void BumpDaily(string key, float amount, bool isMax = false)
        {
            Daily.TryGetValue(key, out var current);
            Daily[key] = isMax ? Mathf.Max(current, amount) : current + amount;
            SaveMissions();
        }

        /// Damage from a bad decision. Returns true when the run ends on this hit.
        public static bool ApplyDamage(float amount)
        {
            if (RunOver) return false;
            Integrity = Mathf.Max(0f, Integrity - amount);
            if (Integrity > 0f) return false;
            EndRun();
            return true;
        }

        public static void EndRun()
        {
            if (RunOver) return;
            RunOver = true;
            // Cash scales with how far the run got, so a better truck paying for longer
            // survival is the progression: run -> cash -> garage -> longer run.
            LastRunCash = AwardCash(Mathf.Clamp01(RunDistanceKm / 8f), RunStartScore);
            Save();
        }

        public static void BeginRun()
        {
            Integrity = MaxIntegrity;
            RunOver = false;
            InnocentsHit = 0;
            Takedowns = 0;
            RunDistanceKm = 0f;
            Combo = 0;
            RunStartScore = Score;
            IsAftertouchActive = false;
            AftertouchTakedowns = 0;
            PileupDamage = 0;
            CrashbreakerReady = false;
            CrashbreakerUsed = false;
        }

        public static int AwardCash(float completionFraction, int runStartScore)
        {
            var points = Mathf.Max(0, Score - runStartScore);
            var pileupBonus = Mathf.RoundToInt(PileupDamage * 0.05f) + (AftertouchTakedowns * 400);
            var earned = (int)(points * 0.1f) + (int)(completionFraction * 1200f) + 150 + pileupBonus;
            Cash += earned;
            BumpDaily("cash", earned);
            Save();
            return earned;
        }

        public static bool BuyUpgrade(string key)
        {
            var level = UpgradeLevel(key);
            if (level >= UpgradeMax) return false;
            var cost = UpgradeCost(level);
            if (Cash < cost) return false;
            Cash -= cost;
            switch (key)
            {
                case "engine": UpgradeEngine++; break;
                case "armor": UpgradeArmour++; break;
                default: UpgradeBoost++; break;
            }
            Save();
            return true;
        }

        public static bool BuyCar(int index)
        {
            if (index < 0 || index >= Cars.Length || OwnedCars.Contains(index)) return false;
            if (Cash < Cars[index].Price) return false;
            Cash -= Cars[index].Price;
            OwnedCars.Add(index);
            SelectedCar = index;
            Save();
            return true;
        }

        // ------------------------------------------------------------ missions
        private static string DayStamp(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// Rolls a fresh set of 3 dailies when the calendar day changes and advances the
        /// login streak. The streak only continues if the last play was *yesterday* - any
        /// longer gap resets it, otherwise returning after months pays the same as daily play.
        public static void RollDailyMissions()
        {
            var today = DayStamp(DateTime.Now);
            if (MissionDay == today && MissionIds.Count == 3) return;

            LoginStreak = MissionDay == DayStamp(DateTime.Now.AddDays(-1)) ? LoginStreak + 1 : 1;
            LastLoginReward = LoginBonus[Mathf.Min(LoginStreak - 1, LoginBonus.Length - 1)];
            Cash += LastLoginReward;

            var pool = Enumerable.Range(0, MissionPool.Length).OrderBy(_ => UnityEngine.Random.value).ToList();
            MissionIds = pool.Take(3).ToList();
            MissionClaimed = new List<bool> { false, false, false };
            foreach (var key in Daily.Keys.ToList()) Daily[key] = 0f;
            MissionDay = today;
            SaveMissions();
            Save();
        }

        public static float MissionProgress(int slot)
        {
            if (slot < 0 || slot >= MissionIds.Count) return 0f;
            Daily.TryGetValue(MissionPool[MissionIds[slot]].Key, out var value);
            return value;
        }

        public static bool MissionDone(int slot) =>
            slot >= 0 && slot < MissionIds.Count && MissionProgress(slot) >= MissionPool[MissionIds[slot]].Goal;

        public static bool ClaimMission(int slot)
        {
            if (!MissionDone(slot) || MissionClaimed[slot]) return false;
            Cash += MissionPool[MissionIds[slot]].Reward;
            MissionClaimed[slot] = true;
            SaveMissions();
            Save();
            return true;
        }

        /// The cheapest unowned car - the player's next target, which drives the return loop.
        public static string NextCarGoal()
        {
            var best = -1;
            for (var i = 0; i < Cars.Length; i++)
            {
                if (OwnedCars.Contains(i)) continue;
                if (best < 0 || Cars[i].Price < Cars[best].Price) best = i;
            }
            if (best < 0) return "You own every ride. Legend.";
            var toGo = Mathf.Max(0, Cars[best].Price - Cash);
            return toGo <= 0
                ? $"NEXT RIDE:  {Cars[best].Name}  —  affordable now! (Garage)"
                : $"NEXT RIDE:  {Cars[best].Name}  —  ${toGo:N0} to go";
        }

        // ------------------------------------------------------------ persistence
        public static void Save()
        {
            PlayerPrefs.SetInt("rr_cash", Cash);
            PlayerPrefs.SetInt("rr_up_engine", UpgradeEngine);
            PlayerPrefs.SetInt("rr_up_armor", UpgradeArmour);
            PlayerPrefs.SetInt("rr_up_boost", UpgradeBoost);
            PlayerPrefs.SetString("rr_owned_cars", string.Join(",", OwnedCars));
            PlayerPrefs.SetInt("rr_selected_car", SelectedCar);
            PlayerPrefs.Save();
        }

        public static void SaveMissions()
        {
            PlayerPrefs.SetString("rr_mission_day", MissionDay);
            PlayerPrefs.SetString("rr_mission_ids", string.Join(",", MissionIds));
            PlayerPrefs.SetString("rr_mission_claimed", string.Join(",", MissionClaimed.Select(c => c ? "1" : "0")));
            PlayerPrefs.SetInt("rr_login_streak", LoginStreak);
            PlayerPrefs.SetInt("rr_login_reward", LastLoginReward);
            foreach (var pair in Daily) PlayerPrefs.SetFloat($"rr_daily_{pair.Key}", pair.Value);
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            Cash = PlayerPrefs.GetInt("rr_cash", 0);
            UpgradeEngine = PlayerPrefs.GetInt("rr_up_engine", 0);
            UpgradeArmour = PlayerPrefs.GetInt("rr_up_armor", 0);
            UpgradeBoost = PlayerPrefs.GetInt("rr_up_boost", 0);
            OwnedCars = ParseInts(PlayerPrefs.GetString("rr_owned_cars", "0"));
            if (OwnedCars.Count == 0) OwnedCars.Add(0);
            SelectedCar = PlayerPrefs.GetInt("rr_selected_car", 0);

            MissionDay = PlayerPrefs.GetString("rr_mission_day", string.Empty);
            MissionIds = ParseInts(PlayerPrefs.GetString("rr_mission_ids", string.Empty));
            MissionClaimed = PlayerPrefs.GetString("rr_mission_claimed", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c == "1").ToList();
            while (MissionClaimed.Count < MissionIds.Count) MissionClaimed.Add(false);
            LoginStreak = PlayerPrefs.GetInt("rr_login_streak", 0);
            LastLoginReward = PlayerPrefs.GetInt("rr_login_reward", 0);
            foreach (var key in Daily.Keys.ToList()) Daily[key] = PlayerPrefs.GetFloat($"rr_daily_{key}", 0f);
        }

        private static List<int> ParseInts(string raw) => raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var value) ? value : -1)
            .Where(value => value >= 0)
            .ToList();

        public static void ResetRun()
        {
            Score = 0;
            Combo = 0;
            ComboTimer = 0f;
            Takedowns = 0;
            RunDistanceKm = 0f;
            Message = string.Empty;
            MessageTimer = 0f;
        }
    }

    /// Impact debris. The shipped build deliberately uses a soft dust puff rather than
    /// flying cubes, and keeps the particle count low so rapid hits don't litter the screen.
    public sealed class CrashEffects : MonoBehaviour
    {
        public static CrashEffects Active { get; private set; }

        private ParticleSystem puff;
        private ParticleSystem sparks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActive() => Active = null;

        public static CrashEffects Create(Material puffMaterial)
        {
            var root = new GameObject("Crash Effects");
            var effects = root.AddComponent<CrashEffects>();
            Active = effects;
            effects.puff = effects.BuildPuff(puffMaterial);
            effects.sparks = effects.BuildSparks(puffMaterial);
            return effects;
        }

        /// Particles must never use an opaque surface material. The caller historically
        /// passed "White Paint" (URP Lit, opaque), which drew each particle as a hard
        /// white rectangle instead of a soft puff.
        private static Material ParticleMaterial(Material fallback)
        {
            var soft = Resources.Load<Material>("WeatherParticle");
            return soft != null ? new Material(soft) : fallback;
        }

        private ParticleSystem BuildPuff(Material material)
        {
            material = ParticleMaterial(material);
            var system = new GameObject("Impact Puff").AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetParent(transform, false);
            var main = system.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 3.2f;
            main.startSize = 0.9f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.70f, 0.66f, 0.55f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var overLifetime = system.sizeOverLifetime;
            overLifetime.enabled = true;
            overLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return system;
        }

        private ParticleSystem BuildSparks(Material material)
        {
            material = ParticleMaterial(material);
            var system = new GameObject("Impact Sparks").AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetParent(transform, false);
            var main = system.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.35f;
            main.startSpeed = 8f;
            main.startSize = 0.22f;
            main.gravityModifier = 1.6f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.25f), new Color(1f, 0.35f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 42f;
            shape.radius = 0.2f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return system;
        }

        public void PlayAt(Vector3 position)
        {
            puff.transform.position = position;
            sparks.transform.position = position;
            puff.Play();
            sparks.Play();
        }
    }
}
