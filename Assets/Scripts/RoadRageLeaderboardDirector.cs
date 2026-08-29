using System;
using System.Collections.Generic;
using UnityEngine;
using EpicLeaderboard;

namespace RoadRage.UnityRemake
{
    [Serializable]
    public struct LeaderboardEntryData
    {
        public int Rank;
        public string Username;
        public int Score;
        public string CarName;
        public int Takedowns;
        public string Country;
    }

    public sealed class RoadRageLeaderboardDirector : MonoBehaviour
    {
        public static RoadRageLeaderboardDirector Instance { get; private set; }

        public bool IsLeaderboardOpen { get; set; }
        public bool IsLoading { get; private set; }
        public string StatusMessage { get; private set; } = "Ready";

        public string PlayerName
        {
            get => PlayerPrefs.GetString("RR_PLAYER_NAME", "RoadWarrior");
            set
            {
                var clean = string.IsNullOrEmpty(value) ? "RoadWarrior" : value.Trim();
                PlayerPrefs.SetString("RR_PLAYER_NAME", clean);
                PlayerPrefs.Save();
            }
        }

        public List<LeaderboardEntryData> CachedEntries { get; private set; } = new();

        private EpicLeaderboardGame gameDef;
        private BoardDefinition boardDef;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Load EpicLeaderboard Demo assets or fallback definitions
            gameDef = Resources.Load<EpicLeaderboardGame>("DemoGame");
            if (gameDef == null)
            {
                gameDef = EpicLeaderboardGame.Create("6657286243a2ea2c8ee39221", "05e53664638847feccf3a0f3d4ab37aa");
            }

            boardDef = Resources.Load<BoardDefinition>("DemoLeaderboard");
            if (boardDef == null)
            {
                boardDef = BoardDefinition.Create("RoadRage_Arcade", "HighScores");
            }

            // Populate initial fallback leaderboard
            PopulateFallbackScores();
            FetchOnlineScores();
        }

        public void OpenLeaderboard()
        {
            IsLeaderboardOpen = true;
            FetchOnlineScores();
        }

        public void CloseLeaderboard()
        {
            IsLeaderboardOpen = false;
        }

        public void ToggleLeaderboard()
        {
            if (IsLeaderboardOpen) CloseLeaderboard();
            else OpenLeaderboard();
        }

        public void SubmitScore(int score, int takedowns, string carName)
        {
            if (score <= 0) return;

            // Check if better than local score
            if (score > GameState.HighScore)
            {
                GameState.HighScore = score;
            }

            // Prepare metadata
            var meta = new Dictionary<string, string>
            {
                { "Car", carName },
                { "Takedowns", takedowns.ToString() },
                { "Distance", GameState.RunDistanceKm.ToString("0.0") }
            };

            StatusMessage = "Submitting score...";
            if (gameDef != null && boardDef != null)
            {
                EpicLeaderboardClient.SubmitScore(gameDef, boardDef, PlayerName, score, meta, result =>
                {
                    if (result.Success)
                    {
                        StatusMessage = result.Value.WasNewBest ? "🔥 NEW PERSONAL BEST!" : "Score submitted!";
                        FetchOnlineScores();
                    }
                    else
                    {
                        StatusMessage = "Submitted locally";
                        UpdateLocalPlayerScore(score, carName, takedowns);
                    }
                });
            }
            else
            {
                UpdateLocalPlayerScore(score, carName, takedowns);
            }
        }

        public void FetchOnlineScores()
        {
            if (gameDef == null || boardDef == null)
            {
                PopulateFallbackScores();
                return;
            }

            IsLoading = true;
            StatusMessage = "Loading leaderboard...";

            EpicLeaderboardClient.GetScores(gameDef, boardDef, result =>
            {
                IsLoading = false;
                if (result.Success && result.Value != null && result.Value.scores != null && result.Value.scores.Count > 0)
                {
                    CachedEntries.Clear();
                    foreach (var s in result.Value.scores)
                    {
                        var metadata = s.GetMetadata();
                        metadata.TryGetValue("Car", out var car);
                        metadata.TryGetValue("Takedowns", out var tdStr);
                        int.TryParse(tdStr, out var td);

                        int.TryParse(s.score, out var scoreVal);

                        CachedEntries.Add(new LeaderboardEntryData
                        {
                            Rank = s.rank,
                            Username = s.username,
                            Score = scoreVal,
                            CarName = string.IsNullOrEmpty(car) ? "BRUTE" : car,
                            Takedowns = td,
                            Country = string.IsNullOrEmpty(s.country) ? "US" : s.country
                        });
                    }
                    StatusMessage = $"Updated: {CachedEntries.Count} racers";
                }
                else
                {
                    StatusMessage = "Offline / Local Standings";
                    PopulateFallbackScores();
                }
            }, username: PlayerName);
        }

        private void UpdateLocalPlayerScore(int score, string carName, int takedowns)
        {
            var found = false;
            for (int i = 0; i < CachedEntries.Count; i++)
            {
                if (CachedEntries[i].Username == PlayerName)
                {
                    if (score > CachedEntries[i].Score)
                    {
                        var e = CachedEntries[i];
                        e.Score = score;
                        e.CarName = carName;
                        e.Takedowns = takedowns;
                        CachedEntries[i] = e;
                    }
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                CachedEntries.Add(new LeaderboardEntryData
                {
                    Rank = CachedEntries.Count + 1,
                    Username = PlayerName,
                    Score = score,
                    CarName = carName,
                    Takedowns = takedowns,
                    Country = "US"
                });
            }

            CachedEntries.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < CachedEntries.Count; i++)
            {
                var item = CachedEntries[i];
                item.Rank = i + 1;
                CachedEntries[i] = item;
            }
        }

        private void PopulateFallbackScores()
        {
            if (CachedEntries.Count > 0 && CachedEntries[0].Score > 0) return;

            CachedEntries = new List<LeaderboardEntryData>
            {
                new() { Rank = 1, Username = "Viper_99", Score = 482500, CarName = "JUGGERNAUT", Takedowns = 24, Country = "US" },
                new() { Rank = 2, Username = "ApexDrifter", Score = 415000, CarName = "SUPERBIKE", Takedowns = 19, Country = "JP" },
                new() { Rank = 3, Username = "HighwayReaper", Score = 378200, CarName = "ENFORCER", Takedowns = 16, Country = "DE" },
                new() { Rank = 4, Username = PlayerName, Score = Mathf.Max(GameState.HighScore, 285000), CarName = GameState.CurrentCar.Name, Takedowns = GameState.Takedowns, Country = "US" },
                new() { Rank = 5, Username = "TurboGhost", Score = 246000, CarName = "STREET UTE", Takedowns = 12, Country = "GB" },
                new() { Rank = 6, Username = "NitroNomad", Score = 198500, CarName = "TRAIL 4X4", Takedowns = 9, Country = "FR" },
                new() { Rank = 7, Username = "SpeedDemon_X", Score = 162000, CarName = "RUSTY UTE", Takedowns = 7, Country = "CA" }
            };

            CachedEntries.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < CachedEntries.Count; i++)
            {
                var item = CachedEntries[i];
                item.Rank = i + 1;
                CachedEntries[i] = item;
            }
        }
    }
}
