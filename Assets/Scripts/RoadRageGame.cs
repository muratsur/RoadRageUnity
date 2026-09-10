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
        public static int HighScore
        {
            get => PlayerPrefs.GetInt("RR_HIGHSCORE", 125000);
            set
            {
                if (value > PlayerPrefs.GetInt("RR_HIGHSCORE", 125000))
                {
                    PlayerPrefs.SetInt("RR_HIGHSCORE", value);
                    PlayerPrefs.Save();
                }
            }
        }
        public static int Combo;
        public static float ComboTimer;
        public static string Message = string.Empty;
        public static float MessageTimer;
        public static int Takedowns;
        public static float RunDistanceKm;

        /// 0 at the start of a run, 1 once it has become as busy as it gets.
        ///
        /// Nothing scaled with distance before this. Traffic count, violator frequency
        /// and cruise speeds were all fixed at bootstrap, so minute twenty of a run was
        /// identical to minute one and the only thing that changed was the score. An
        /// endless game with a flat difficulty curve has no arc to it - there is nothing
        /// to survive, only something to continue.
        public static float RunIntensity => Mathf.Clamp01(RunDistanceKm / 6f);

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
        public static int TuningTires; // 0: Grip, 1: Drift
        public static int TuningInduction; // 0: Supercharger, 1: Turbocharger
        public static int TuningRamBar; // 0: Stock, 1: Heavy Push-Bar, 2: Battering Ram
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
            // SaveMissions() removed: writing to disk on every hit caused lag spikes.
            // Daily progress is now written once, when the run ends.
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
            LastRunFury = FuryForRun();
            AddFury(LastRunFury);
            SaveMissions();
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
            DoubleUsedThisRun = false;
            RevivesUsed = 0;
            LastRunFury = 0;
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
            RollWheelDay();
            RollFurySeason();
            var today = DayStamp(DateTime.Now);
            if (MissionDay == today && MissionIds.Count == 3) return;

            LoginStreak = MissionDay == DayStamp(DateTime.Now.AddDays(-1)) ? LoginStreak + 1 : 1;
            LastLoginReward = LoginBonus[Mathf.Min(LoginStreak - 1, LoginBonus.Length - 1)];
            Cash += LastLoginReward;
            if (LoginStreak >= GemsStreakDays) AddGems(GemsStreakBonus);

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
            // A claim also pays a wheel spin, so finishing dailies feeds the wheel
            // and the wheel feeds the garage - three spins a day on top of the free one.
            WheelSpins++;
            SaveWheel();

            TryPayDailySetBonus();

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

        // ------------------------------------------------------------ lucky wheel
        public enum WheelPrizeKind { Cash, DoubleCharge, FreeUpgrade, Respin }

        public struct WheelPrize
        {
            public string Label;
            public WheelPrizeKind Kind;
            public int Amount;
            public int Weight;
        }

        /// Eight wedges, weighted - big and small alternate around the rim so the
        /// jackpot never sits next to the other large prize and a near-miss reads as
        /// one. Cash wedges average ~600 per spin, which is deliberately under a
        /// mission reward: the wheel tops the loop up, it does not replace driving.
        public static readonly WheelPrize[] WheelPrizes =
        {
            new() { Label = "$250",     Kind = WheelPrizeKind.Cash,         Amount = 250,  Weight = 22 },
            new() { Label = "x2 CASH",  Kind = WheelPrizeKind.DoubleCharge, Amount = 1,    Weight = 14 },
            new() { Label = "$500",     Kind = WheelPrizeKind.Cash,         Amount = 500,  Weight = 18 },
            new() { Label = "SPIN AGAIN", Kind = WheelPrizeKind.Respin,     Amount = 1,    Weight = 12 },
            new() { Label = "$1,000",   Kind = WheelPrizeKind.Cash,         Amount = 1000, Weight = 12 },
            new() { Label = "UPGRADE",  Kind = WheelPrizeKind.FreeUpgrade,  Amount = 1,    Weight = 8  },
            new() { Label = "$2,500",   Kind = WheelPrizeKind.Cash,         Amount = 2500, Weight = 6  },
            new() { Label = "JACKPOT $7,500", Kind = WheelPrizeKind.Cash,   Amount = 7500, Weight = 2  },
        };

        public const int WheelPaidBase = 750;
        public const int WheelPaidStep = 600;
        public const int WheelPaidMaxPerDay = 4;

        public static int WheelSpins;
        public static int WheelPaidToday;
        public static string WheelDay = string.Empty;
        public static int DoubleCharges;
        public static bool DoubleUsedThisRun;

        public static int WheelWeightTotal
        {
            get
            {
                var total = 0;
                foreach (var prize in WheelPrizes) total += prize.Weight;
                return total;
            }
        }

        /// Paid spins get steadily dearer within a day and stop after four, so the
        /// wheel is a cash sink with a floor rather than a way to farm the jackpot.
        public static int WheelSpinCost => WheelPaidBase + WheelPaidToday * WheelPaidStep;
        public static bool CanBuySpin => WheelPaidToday < WheelPaidMaxPerDay && Cash >= WheelSpinCost;

        public static string WheelPrizeName(int index) =>
            index >= 0 && index < WheelPrizes.Length ? WheelPrizes[index].Label : string.Empty;

        /// One free spin per calendar day plus a reset of the paid-spin ladder. Called
        /// from RollDailyMissions ahead of its own same-day early return, so a player
        /// who never opens the missions panel still gets the spin.
        public static void RollWheelDay()
        {
            var today = DayStamp(DateTime.Now);
            if (WheelDay == today) return;
            WheelDay = today;
            WheelSpins += 1;
            WheelPaidToday = 0;
            SaveWheel();
        }

        /// Weighted pick. The prize is granted here, the instant the button is pressed -
        /// the UI only animates to the index this returns - so closing the panel while
        /// the wheel is still turning cannot lose it. Returns -1 with no spins left.
        public static int SpinWheel()
        {
            if (WheelSpins <= 0) return -1;
            WheelSpins--;

            var roll = UnityEngine.Random.Range(0, WheelWeightTotal);
            var index = WheelPrizes.Length - 1;
            for (var i = 0; i < WheelPrizes.Length; i++)
            {
                roll -= WheelPrizes[i].Weight;
                if (roll < 0) { index = i; break; }
            }

            GrantWheelPrize(index);
            return index;
        }

        public static bool BuySpin()
        {
            if (WheelPaidToday >= WheelPaidMaxPerDay) return false;
            var cost = WheelSpinCost;
            if (Cash < cost) return false;
            Cash -= cost;
            WheelPaidToday++;
            WheelSpins++;
            SaveWheel();
            Save();
            return true;
        }

        /// Wheel cash deliberately does not feed the "bank $N today" daily. That mission
        /// is meant to measure driving, and claiming a mission pays a spin - letting the
        /// spin pay the mission back would close the loop on itself.
        private static void GrantWheelPrize(int index)
        {
            if (index < 0 || index >= WheelPrizes.Length) return;
            var prize = WheelPrizes[index];
            switch (prize.Kind)
            {
                case WheelPrizeKind.Cash:
                    Cash += prize.Amount;
                    break;
                case WheelPrizeKind.DoubleCharge:
                    DoubleCharges += prize.Amount;
                    break;
                case WheelPrizeKind.FreeUpgrade:
                    GrantFreeUpgrade();
                    break;
                default:
                    WheelSpins += prize.Amount;
                    break;
            }
            SaveWheel();
            Save();
        }

        /// The cheapest track that is not maxed, so the wedge always lands on something
        /// the player has not already bought. Everything maxed: pay out its cash value
        /// rather than silently dropping the prize.
        private static void GrantFreeUpgrade()
        {
            var best = string.Empty;
            var bestLevel = int.MaxValue;
            foreach (var key in new[] { "engine", "armor", "boost" })
            {
                var level = UpgradeLevel(key);
                if (level >= UpgradeMax || level >= bestLevel) continue;
                best = key;
                bestLevel = level;
            }
            if (best.Length == 0)
            {
                Cash += UpgradeCost(UpgradeMax - 1);
                return;
            }
            switch (best)
            {
                case "engine": UpgradeEngine++; break;
                case "armor": UpgradeArmour++; break;
                default: UpgradeBoost++; break;
            }
        }

        // ------------------------------------------------------- double earnings
        public static bool CanDoubleEarnings =>
            RunOver && !DoubleUsedThisRun && DoubleCharges > 0 && LastRunCash > 0;

        /// Spends one charge to pay the run's banked cash a second time. Only the run
        /// payout doubles - the login bonus and mission rewards are not part of it -
        /// and only once per run, or the crash screen becomes an infinite cash button.
        public static bool DoubleEarnings()
        {
            if (!CanDoubleEarnings) return false;
            DoubleCharges--;
            DoubleUsedThisRun = true;
            var bonus = LastRunCash;
            Cash += bonus;
            LastRunCash += bonus;
            BumpDaily("cash", bonus);
            SaveWheel();
            SaveMissions();
            Save();
            return true;
        }

        public static void SaveWheel()
        {
            PlayerPrefs.SetInt("rr_wheel_spins", WheelSpins);
            PlayerPrefs.SetInt("rr_wheel_paid", WheelPaidToday);
            PlayerPrefs.SetString("rr_wheel_day", WheelDay);
            PlayerPrefs.SetInt("rr_double_charges", DoubleCharges);
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- gems
        // Gems are earned only. There is no shop that sells them, because there is no
        // IAP in this build - so to be worth existing at all they have to buy something
        // cash cannot, or they are just a second scoreboard.
        //
        // What they buy is time and convenience, never power: a wheel spin past the
        // daily cash cap, a revive that skips the escalating fee, a pass tier, the pro
        // lane. Every one of those is still reachable with cash or by simply playing, so
        // a player who ignores gems loses nothing but time. Cash stays the power
        // currency - cars and upgrades cannot be bought with gems at all.
        public const int GemsPerFuryTier = 1;
        public const int GemsPerProTier = 2;
        public const int GemsAllDailies = 5;
        public const int GemsStreakBonus = 10;
        public const int GemsStreakDays = 7;

        public const int GemSpinPrice = 10;
        public const int GemRevivePrice = 15;
        public const int GemTierSkipPrice = 40;
        public const int GemProPrice = 120;

        public static int Gems;
        public static string GemsDailyDay = string.Empty;

        /// Clearing the whole daily set pays gems, once a day. Per-set rather than
        /// per-mission so it rewards finishing, which is the harder half.
        public static bool TryPayDailySetBonus()
        {
            var today = DayStamp(DateTime.Now);
            if (GemsDailyDay == today || MissionClaimed.Count == 0 || !MissionClaimed.All(c => c)) return false;
            GemsDailyDay = today;
            AddGems(GemsAllDailies);
            return true;
        }

        public static void AddGems(int amount)
        {
            if (amount <= 0) return;
            Gems += amount;
            SaveGems();
        }

        public static bool SpendGems(int amount)
        {
            if (amount <= 0 || Gems < amount) return false;
            Gems -= amount;
            SaveGems();
            return true;
        }

        /// An extra spin for gems, with no daily cap - that cap is on the cash ladder,
        /// which is the anti-farming measure. Gems are their own limit.
        public static bool BuySpinWithGems()
        {
            if (!SpendGems(GemSpinPrice)) return false;
            WheelSpins++;
            SaveWheel();
            return true;
        }

        public static bool BuyFuryProWithGems()
        {
            if (FuryPro || !SpendGems(GemProPrice)) return false;
            FuryPro = true;
            SaveFury();
            return true;
        }

        /// Buys out the rest of the current tier. Refused on a finished track, so gems
        /// cannot be poured into a pass that has nothing left to give.
        public static bool SkipFuryTier()
        {
            if (FuryTier >= FuryTiers) return false;
            if (!SpendGems(GemTierSkipPrice)) return false;
            AddFury(FuryTierXp - FuryTierProgress);
            return true;
        }

        public static void SaveGems()
        {
            PlayerPrefs.SetInt("rr_gems", Gems);
            PlayerPrefs.SetString("rr_gems_daily_day", GemsDailyDay);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------- fury pass
        public enum FuryRewardKind { Cash, Spins, DoubleCharge, ReviveToken, Upgrade, Car }

        public struct FuryReward
        {
            public FuryRewardKind Kind;
            public int Amount;
            public string Label;
        }

        public const int FuryTiers = 20;
        public const int FuryTierXp = 1000;
        public const int FuryProPrice = 9000;
        public const int FurySeasonDays = 28;

        /// The origin of the season clock. There is no server, so seasons are worked out
        /// from a fixed date every install shares: same build, same schedule.
        ///
        /// It can be repointed per install (rr_fury_epoch) so a season can be started on
        /// demand - without that, testing the pass means either waiting out whatever is
        /// left of the current season or watching it wipe mid-test. The override is
        /// config rather than progression, so ResetProfile leaves it alone.
        private static readonly DateTime DefaultFurySeasonEpoch = new(2026, 1, 5);

        // Read on every CurrentFurySeason and FuryDaysLeft, both of which the pass panel
        // and the landing dock badge hit each frame, so the parse is cached.
        private static DateTime? furyEpochCache;

        public static DateTime FurySeasonEpoch
        {
            get
            {
                if (furyEpochCache.HasValue) return furyEpochCache.Value;
                var raw = PlayerPrefs.GetString("rr_fury_epoch", string.Empty);
                var epoch = DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) ? parsed.Date : DefaultFurySeasonEpoch;
                furyEpochCache = epoch;
                return epoch;
            }
        }

        public static bool FurySeasonEpochOverridden =>
            PlayerPrefs.GetString("rr_fury_epoch", string.Empty).Length > 0;

        /// Repoints the season clock. The season index moves, so the next RollFurySeason
        /// wipes the track - which is the point: it is a new season.
        public static void SetFurySeasonEpoch(DateTime epoch)
        {
            PlayerPrefs.SetString("rr_fury_epoch", epoch.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            furyEpochCache = null;
        }

        public static void ClearFurySeasonEpoch()
        {
            PlayerPrefs.DeleteKey("rr_fury_epoch");
            PlayerPrefs.Save();
            furyEpochCache = null;
        }

        /// First and last day of the season the clock is currently in.
        public static DateTime FurySeasonStart =>
            FurySeasonEpoch.AddDays((double)CurrentFurySeason * FurySeasonDays);

        public static DateTime FurySeasonEnd => FurySeasonStart.AddDays(FurySeasonDays);

        /// The free lane pays in cash and the odd wheel spin - enough that playing
        /// without buying anything still moves. Rewards climb with the tier so the
        /// back half is worth grinding for.
        public static readonly FuryReward[] FuryFreeTrack =
        {
            new() { Kind = FuryRewardKind.Cash,         Amount = 300,  Label = "$300"      },
            new() { Kind = FuryRewardKind.Cash,         Amount = 400,  Label = "$400"      },
            new() { Kind = FuryRewardKind.Spins,        Amount = 1,    Label = "1 SPIN"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 500,  Label = "$500"      },
            new() { Kind = FuryRewardKind.DoubleCharge, Amount = 1,    Label = "x2 CASH"   },
            new() { Kind = FuryRewardKind.Cash,         Amount = 600,  Label = "$600"      },
            new() { Kind = FuryRewardKind.Spins,        Amount = 1,    Label = "1 SPIN"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 700,  Label = "$700"      },
            new() { Kind = FuryRewardKind.Cash,         Amount = 800,  Label = "$800"      },
            new() { Kind = FuryRewardKind.DoubleCharge, Amount = 1,    Label = "x2 CASH"   },
            new() { Kind = FuryRewardKind.Cash,         Amount = 900,  Label = "$900"      },
            new() { Kind = FuryRewardKind.Spins,        Amount = 1,    Label = "1 SPIN"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 1000, Label = "$1,000"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 1100, Label = "$1,100"    },
            new() { Kind = FuryRewardKind.DoubleCharge, Amount = 1,    Label = "x2 CASH"   },
            new() { Kind = FuryRewardKind.Cash,         Amount = 1200, Label = "$1,200"    },
            new() { Kind = FuryRewardKind.Spins,        Amount = 1,    Label = "1 SPIN"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 1300, Label = "$1,300"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 1500, Label = "$1,500"    },
            new() { Kind = FuryRewardKind.Cash,         Amount = 2500, Label = "$2,500"    },
        };

        /// The pro lane is bought with cash, not money, and it is retroactive: buying it
        /// makes every tier already earned claimable at once. Its distinctive rewards are
        /// revive tokens and upgrade levels, with the RIG at tier 20 as the season prize.
        public static readonly FuryReward[] FuryProTrack =
        {
            new() { Kind = FuryRewardKind.Cash,        Amount = 600,  Label = "$600"      },
            new() { Kind = FuryRewardKind.Spins,       Amount = 2,    Label = "2 SPINS"   },
            new() { Kind = FuryRewardKind.ReviveToken, Amount = 1,    Label = "REVIVE"    },
            new() { Kind = FuryRewardKind.Cash,        Amount = 900,  Label = "$900"      },
            new() { Kind = FuryRewardKind.Upgrade,     Amount = 1,    Label = "UPGRADE"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 1200, Label = "$1,200"    },
            new() { Kind = FuryRewardKind.ReviveToken, Amount = 1,    Label = "REVIVE"    },
            new() { Kind = FuryRewardKind.Spins,       Amount = 2,    Label = "2 SPINS"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 1500, Label = "$1,500"    },
            new() { Kind = FuryRewardKind.Upgrade,     Amount = 1,    Label = "UPGRADE"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 1800, Label = "$1,800"    },
            new() { Kind = FuryRewardKind.ReviveToken, Amount = 1,    Label = "REVIVE"    },
            new() { Kind = FuryRewardKind.Spins,       Amount = 2,    Label = "2 SPINS"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 2100, Label = "$2,100"    },
            new() { Kind = FuryRewardKind.Upgrade,     Amount = 1,    Label = "UPGRADE"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 2400, Label = "$2,400"    },
            new() { Kind = FuryRewardKind.ReviveToken, Amount = 1,    Label = "REVIVE"    },
            new() { Kind = FuryRewardKind.Spins,       Amount = 3,    Label = "3 SPINS"   },
            new() { Kind = FuryRewardKind.Cash,        Amount = 3000, Label = "$3,000"    },
            new() { Kind = FuryRewardKind.Car,        Amount = 7,    Label = "RIG"        },
        };

        public static int FurySeason = -1;
        public static int FuryXp;
        public static bool FuryPro;
        public static int LastRunFury;
        public static List<bool> FuryFreeClaimed = new();
        public static List<bool> FuryProClaimed = new();

        public static int CurrentFurySeason =>
            (int)((DateTime.Now.Date - FurySeasonEpoch).TotalDays / FurySeasonDays);

        /// Completed tiers, 0..FuryTiers. Tier N's reward unlocks once this reaches N.
        public static int FuryTier => Mathf.Clamp(FuryXp / FuryTierXp, 0, FuryTiers);
        public static int FuryTierProgress => FuryTier >= FuryTiers ? FuryTierXp : FuryXp - FuryTier * FuryTierXp;

        public static int FuryDaysLeft =>
            Mathf.Max(0, FurySeasonDays - (int)(DateTime.Now.Date - FurySeasonStart).TotalDays);

        public static bool FuryClaimable(int tier, bool pro)
        {
            if (tier < 1 || tier > FuryTiers || FuryTier < tier) return false;
            if (pro && !FuryPro) return false;
            var claimed = pro ? FuryProClaimed : FuryFreeClaimed;
            return tier - 1 < claimed.Count && !claimed[tier - 1];
        }

        public static int FuryUnclaimedCount()
        {
            var count = 0;
            for (var tier = 1; tier <= FuryTiers; tier++)
            {
                if (FuryClaimable(tier, false)) count++;
                if (FuryClaimable(tier, true)) count++;
            }
            return count;
        }

        /// Wipes the track when the calendar rolls into a new 28-day season. Also
        /// repairs a short or missing claim list, which is what a first load looks like.
        public static void RollFurySeason()
        {
            var season = CurrentFurySeason;
            if (FurySeason == season && FuryFreeClaimed.Count == FuryTiers && FuryProClaimed.Count == FuryTiers) return;
            FurySeason = season;
            FuryXp = 0;
            FuryPro = false;
            FuryFreeClaimed = NewFuryFlags();
            FuryProClaimed = NewFuryFlags();
            SaveFury();
        }

        /// Fury for a run: takedowns and distance, the two things the game actually asks
        /// for. Granted once when the run ends rather than per event, so a pass cannot be
        /// advanced by hits that never became a finished run.
        public static int FuryForRun() =>
            Takedowns * 45
            + Mathf.RoundToInt(RunDistanceKm * 70f)
            + Mathf.Max(0, Score - RunStartScore) / 250;

        public static void AddFury(int amount)
        {
            FuryXp = Mathf.Max(0, FuryXp + amount);
            SaveFury();
        }

        public static bool BuyFuryPro()
        {
            if (FuryPro || Cash < FuryProPrice) return false;
            Cash -= FuryProPrice;
            FuryPro = true;
            SaveFury();
            Save();
            return true;
        }

        public static bool ClaimFury(int tier, bool pro)
        {
            if (!FuryClaimable(tier, pro)) return false;
            var claimed = pro ? FuryProClaimed : FuryFreeClaimed;
            claimed[tier - 1] = true;
            GrantFuryReward(pro ? FuryProTrack[tier - 1] : FuryFreeTrack[tier - 1]);
            AddGems(pro ? GemsPerProTier : GemsPerFuryTier);
            SaveFury();
            Save();
            return true;
        }

        private static void GrantFuryReward(FuryReward reward)
        {
            switch (reward.Kind)
            {
                case FuryRewardKind.Cash:
                    Cash += reward.Amount;
                    break;
                case FuryRewardKind.Spins:
                    WheelSpins += reward.Amount;
                    SaveWheel();
                    break;
                case FuryRewardKind.DoubleCharge:
                    DoubleCharges += reward.Amount;
                    SaveWheel();
                    break;
                case FuryRewardKind.ReviveToken:
                    ReviveTokens += reward.Amount;
                    break;
                case FuryRewardKind.Upgrade:
                    for (var i = 0; i < reward.Amount; i++) GrantFreeUpgrade();
                    break;
                default:
                    // Already own the season car (bought it meanwhile): pay its price
                    // instead, so the headline reward is never a dead tier.
                    var car = Mathf.Clamp(reward.Amount, 0, Cars.Length - 1);
                    if (OwnedCars.Contains(car)) Cash += Cars[car].Price;
                    else OwnedCars.Add(car);
                    break;
            }
        }

        private static List<bool> NewFuryFlags()
        {
            var flags = new List<bool>(FuryTiers);
            for (var i = 0; i < FuryTiers; i++) flags.Add(false);
            return flags;
        }

        // --------------------------------------------------- adrenaline revive
        public const int ReviveMaxPerRun = 2;
        public const float ReviveIntegrityFraction = 0.6f;

        public static int RevivesUsed;      // this run
        public static int ReviveTokens;     // persisted, earned on the pro pass

        public enum RevivePayment { Token, Cash, Gems }

        public static int ReviveCost => 1200 + RevivesUsed * 1800;
        public static bool ReviveUsesToken => ReviveTokens > 0;

        public static bool CanPayRevive(RevivePayment payment) => payment switch
        {
            RevivePayment.Token => ReviveTokens > 0,
            RevivePayment.Gems => Gems >= GemRevivePrice,
            _ => Cash >= ReviveCost,
        };

        /// Tokens first: they are the pro pass's reward and are worth nothing unspent.
        /// Then cash, then gems - gems are the scarcest, so they are the last resort.
        public static RevivePayment ReviveDefaultPayment =>
            ReviveTokens > 0 ? RevivePayment.Token
            : Cash >= ReviveCost ? RevivePayment.Cash
            : RevivePayment.Gems;

        public static bool CanRevive =>
            RunOver && !DoubleUsedThisRun && RevivesUsed < ReviveMaxPerRun
            && (CanPayRevive(RevivePayment.Token) || CanPayRevive(RevivePayment.Cash)
                || CanPayRevive(RevivePayment.Gems));

        /// Puts the player back on the road mid-run.
        ///
        /// EndRun banks the payout the moment integrity hits zero, so this has to unwind
        /// that payout rather than defer it - otherwise every revive pays the same run
        /// out again. The daily cash counter and the pass XP are unwound with it. The run
        /// is not restarted: RunStartScore is untouched, so when it really ends the payout
        /// covers the whole thing, revived length included.
        public static bool Revive() => Revive(ReviveDefaultPayment);

        public static bool Revive(RevivePayment payment)
        {
            if (!CanRevive || !CanPayRevive(payment)) return false;

            switch (payment)
            {
                case RevivePayment.Token: ReviveTokens--; break;
                case RevivePayment.Gems: Gems -= GemRevivePrice; break;
                default: Cash -= ReviveCost; break;
            }

            Cash -= LastRunCash;
            BumpDaily("cash", -LastRunCash);
            LastRunCash = 0;
            AddFury(-LastRunFury);
            LastRunFury = 0;

            RevivesUsed++;
            RunOver = false;
            Integrity = MaxIntegrity * ReviveIntegrityFraction;

            SaveFury();
            SaveGems();
            SaveMissions();
            Save();
            return true;
        }

        public static void SaveFury()
        {
            PlayerPrefs.SetInt("rr_fury_season", FurySeason);
            PlayerPrefs.SetInt("rr_fury_xp", FuryXp);
            PlayerPrefs.SetInt("rr_fury_pro", FuryPro ? 1 : 0);
            PlayerPrefs.SetInt("rr_revive_tokens", ReviveTokens);
            PlayerPrefs.SetString("rr_fury_free", FlagsToString(FuryFreeClaimed));
            PlayerPrefs.SetString("rr_fury_pro_claimed", FlagsToString(FuryProClaimed));
            PlayerPrefs.Save();
        }

        private static string FlagsToString(List<bool> flags) =>
            string.Join("", flags.Select(f => f ? "1" : "0"));

        private static List<bool> ParseFuryFlags(string raw)
        {
            var flags = NewFuryFlags();
            for (var i = 0; i < FuryTiers && i < raw.Length; i++) flags[i] = raw[i] == '1';
            return flags;
        }

        // ------------------------------------------------------ profile reset
        /// Every key this class persists. Listed explicitly rather than derived, so a
        /// reset deletes keys instead of overwriting them: a key that later stops being
        /// written would otherwise linger and be picked back up by the next Load.
        ///
        /// The per-day counters (rr_daily_*) are handled separately because their names
        /// come from the Daily dictionary. Three keys are deliberately absent -
        /// ROAD_RAGE_BIOME, RR_PLAYER_NAME and RR_HIGHSCORE are preferences and a
        /// record, not progression, and wiping the chosen track or the pilot tag is not
        /// what anyone means by "reset my profile".
        public static readonly string[] ProfileKeys =
        {
            "rr_cash",
            "rr_up_engine", "rr_up_armor", "rr_up_boost",
            "rr_tune_tires", "rr_tune_induct", "rr_tune_rambar",
            "rr_owned_cars", "rr_selected_car",
            "rr_mission_day", "rr_mission_ids", "rr_mission_claimed",
            "rr_login_streak", "rr_login_reward",
            "rr_wheel_spins", "rr_wheel_paid", "rr_wheel_day",
            "rr_double_charges", "rr_wheel_seeded",
            "rr_fury_season", "rr_fury_xp", "rr_fury_pro", "rr_revive_tokens",
            "rr_fury_free", "rr_fury_pro_claimed",
            "rr_gems", "rr_gems_daily_day",
        };

        /// Wipes the save back to a first-launch profile: no cash, the starter ute, no
        /// upgrades, an empty wheel, a fresh pass season and zero gems.
        ///
        /// Destructive and not undoable - the caller is responsible for confirming.
        /// Load and RollDailyMissions run afterwards so the profile comes back through
        /// the same first-launch paths a new install takes (the seeded x2 charge, day
        /// one's free spin, the first login bonus), and a game already running shows the
        /// reset state without needing a domain reload.
        public static void ResetProfile(bool keepHighScore = true)
        {
            var best = PlayerPrefs.GetInt("RR_HIGHSCORE", 125000);

            foreach (var key in ProfileKeys) PlayerPrefs.DeleteKey(key);
            foreach (var key in Daily.Keys.ToList()) PlayerPrefs.DeleteKey($"rr_daily_{key}");
            if (keepHighScore) PlayerPrefs.SetInt("RR_HIGHSCORE", best);
            else PlayerPrefs.DeleteKey("RR_HIGHSCORE");
            PlayerPrefs.Save();

            // In-memory state that Load does not itself clear.
            OwnedCars = new List<int> { 0 };
            SelectedCar = 0;
            MissionDay = string.Empty;
            MissionIds = new List<int>();
            MissionClaimed = new List<bool>();
            WheelDay = string.Empty;
            GemsDailyDay = string.Empty;
            FurySeason = -1;
            FuryFreeClaimed = new List<bool>();
            FuryProClaimed = new List<bool>();
            RevivesUsed = 0;
            LastRunCash = 0;
            LastRunFury = 0;
            DoubleUsedThisRun = false;

            ResetRun();
            Load();
            RollDailyMissions();
        }

        // ------------------------------------------------------- save snapshot
        /// A copy of everything that persists, so the self-test can hand the player's
        /// save back exactly as it found it.
        ///
        /// The test drives the economy directly - it ends runs, banks cash, spins the
        /// wheel, spends gems and rolls the pass. While the only persisted thing was
        /// cash that cost a few hundred dollars of progress. With the wheel, the pass,
        /// gems and revive tokens in the save it would rewrite a real profile, so the
        /// test now brackets itself with Snapshot/RestoreSnapshot.
        public sealed class SaveSnapshot
        {
            public int Cash, UpgradeEngine, UpgradeArmour, UpgradeBoost;
            public int TuningTires, TuningInduction, TuningRamBar, SelectedCar;
            public List<int> OwnedCars = new();
            public string MissionDay = string.Empty;
            public List<int> MissionIds = new();
            public List<bool> MissionClaimed = new();
            public int LoginStreak, LastLoginReward;
            public Dictionary<string, float> Daily = new();
            public int WheelSpins, WheelPaidToday, DoubleCharges;
            public string WheelDay = string.Empty;
            public int FurySeason, FuryXp, ReviveTokens;
            public bool FuryPro;
            public List<bool> FuryFreeClaimed = new();
            public List<bool> FuryProClaimed = new();
            public int Gems;
            public string GemsDailyDay = string.Empty;
            public int HighScore;
        }

        public static SaveSnapshot Snapshot() => new()
        {
            Cash = Cash,
            UpgradeEngine = UpgradeEngine,
            UpgradeArmour = UpgradeArmour,
            UpgradeBoost = UpgradeBoost,
            TuningTires = TuningTires,
            TuningInduction = TuningInduction,
            TuningRamBar = TuningRamBar,
            SelectedCar = SelectedCar,
            OwnedCars = new List<int>(OwnedCars),
            MissionDay = MissionDay,
            MissionIds = new List<int>(MissionIds),
            MissionClaimed = new List<bool>(MissionClaimed),
            LoginStreak = LoginStreak,
            LastLoginReward = LastLoginReward,
            Daily = new Dictionary<string, float>(Daily),
            WheelSpins = WheelSpins,
            WheelPaidToday = WheelPaidToday,
            WheelDay = WheelDay,
            DoubleCharges = DoubleCharges,
            FurySeason = FurySeason,
            FuryXp = FuryXp,
            FuryPro = FuryPro,
            ReviveTokens = ReviveTokens,
            FuryFreeClaimed = new List<bool>(FuryFreeClaimed),
            FuryProClaimed = new List<bool>(FuryProClaimed),
            Gems = Gems,
            GemsDailyDay = GemsDailyDay,
            HighScore = PlayerPrefs.GetInt("RR_HIGHSCORE", 125000),
        };

        public static void RestoreSnapshot(SaveSnapshot snap)
        {
            if (snap == null) return;

            Cash = snap.Cash;
            UpgradeEngine = snap.UpgradeEngine;
            UpgradeArmour = snap.UpgradeArmour;
            UpgradeBoost = snap.UpgradeBoost;
            TuningTires = snap.TuningTires;
            TuningInduction = snap.TuningInduction;
            TuningRamBar = snap.TuningRamBar;
            SelectedCar = snap.SelectedCar;
            OwnedCars = new List<int>(snap.OwnedCars);
            MissionDay = snap.MissionDay;
            MissionIds = new List<int>(snap.MissionIds);
            MissionClaimed = new List<bool>(snap.MissionClaimed);
            LoginStreak = snap.LoginStreak;
            LastLoginReward = snap.LastLoginReward;
            foreach (var key in Daily.Keys.ToList())
                Daily[key] = snap.Daily.TryGetValue(key, out var value) ? value : 0f;
            WheelSpins = snap.WheelSpins;
            WheelPaidToday = snap.WheelPaidToday;
            WheelDay = snap.WheelDay;
            DoubleCharges = snap.DoubleCharges;
            FurySeason = snap.FurySeason;
            FuryXp = snap.FuryXp;
            FuryPro = snap.FuryPro;
            ReviveTokens = snap.ReviveTokens;
            FuryFreeClaimed = new List<bool>(snap.FuryFreeClaimed);
            FuryProClaimed = new List<bool>(snap.FuryProClaimed);
            Gems = snap.Gems;
            GemsDailyDay = snap.GemsDailyDay;

            // Written straight to the key: the HighScore setter only ever raises, so it
            // cannot put back a value the test pushed up.
            PlayerPrefs.SetInt("RR_HIGHSCORE", snap.HighScore);

            Save();
            SaveMissions();
            SaveWheel();
            SaveFury();
            SaveGems();
        }

        // ------------------------------------------------------------ persistence
        public static void Save()
        {
            PlayerPrefs.SetInt("rr_cash", Cash);
            PlayerPrefs.SetInt("rr_up_engine", UpgradeEngine);
            PlayerPrefs.SetInt("rr_up_armor", UpgradeArmour);
            PlayerPrefs.SetInt("rr_up_boost", UpgradeBoost);
            PlayerPrefs.SetInt("rr_tune_tires", TuningTires);
            PlayerPrefs.SetInt("rr_tune_induct", TuningInduction);
            PlayerPrefs.SetInt("rr_tune_rambar", TuningRamBar);
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
            TuningTires = PlayerPrefs.GetInt("rr_tune_tires", 0);
            TuningInduction = PlayerPrefs.GetInt("rr_tune_induct", 0);
            TuningRamBar = PlayerPrefs.GetInt("rr_tune_rambar", 0);
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

            WheelSpins = PlayerPrefs.GetInt("rr_wheel_spins", 0);
            WheelPaidToday = PlayerPrefs.GetInt("rr_wheel_paid", 0);
            WheelDay = PlayerPrefs.GetString("rr_wheel_day", string.Empty);
            DoubleCharges = PlayerPrefs.GetInt("rr_double_charges", 0);

            furyEpochCache = null;

            Gems = PlayerPrefs.GetInt("rr_gems", 0);
            GemsDailyDay = PlayerPrefs.GetString("rr_gems_daily_day", string.Empty);

            FurySeason = PlayerPrefs.GetInt("rr_fury_season", -1);
            FuryXp = PlayerPrefs.GetInt("rr_fury_xp", 0);
            FuryPro = PlayerPrefs.GetInt("rr_fury_pro", 0) == 1;
            ReviveTokens = PlayerPrefs.GetInt("rr_revive_tokens", 0);
            FuryFreeClaimed = ParseFuryFlags(PlayerPrefs.GetString("rr_fury_free", string.Empty));
            FuryProClaimed = ParseFuryFlags(PlayerPrefs.GetString("rr_fury_pro_claimed", string.Empty));
            if (PlayerPrefs.GetInt("rr_wheel_seeded", 0) == 0)
            {
                // First launch seeds one x2 charge, so a new player meets the doubler on
                // the crash screen before the wheel has had a chance to pay one out.
                // The spin itself is not seeded here - RollWheelDay grants day one's.
                PlayerPrefs.SetInt("rr_wheel_seeded", 1);
                DoubleCharges += 1;
                SaveWheel();
            }
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
            if (Active != null) return Active;
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
