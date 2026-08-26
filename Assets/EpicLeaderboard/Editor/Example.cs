using UnityEngine;
using EpicLeaderboard;

namespace EpicLeaderboard.Examples
{
    /// <summary>
    /// Quick-start example demonstrating both workflows:
    /// 1. Inspector: Drag & drop ScriptableObject assets
    /// 2. Code: Create references dynamically
    ///
    /// To use:
    /// 1. Create an EpicLeaderboardGame asset (Right Click → Create → Epic Leaderboard → Game)
    /// 2. Create a LeaderboardDefinition asset (Right Click → Create → Epic Leaderboard → Leaderboard)
    /// 3. Fill in your credentials and IDs
    /// 4. Drag both assets into this component's fields
    /// 5. Press Play and use the context menu or call the methods from your code
    /// </summary>
    public class EpicLeaderboardExample : MonoBehaviour
    {
        [Header("References (drag ScriptableObject assets here)")] [SerializeField]
        private EpicLeaderboardGame game;

        [SerializeField] private BoardDefinition boardDefinition;

        [Header("Test Data")] [SerializeField] private string testUsername = "TestPlayer";
        [SerializeField] private double testScore = 1000;

        // =================================================================
        // Inspector Workflow — ScriptableObject references
        // =================================================================

        [ContextMenu("Get Scores (Inspector Workflow)")]
        public void GetScoresInspector()
        {
            EpicLeaderboardClient.GetScores(game, boardDefinition, result =>
                {
                    if (result.Success)
                    {
                        var data = result.Value;
                        Debug.Log($"[EpicLeaderboard] Got {data.scores.Count} scores " +
                                  $"(total: {data.totalEntries})");

                        foreach (var entry in data.scores)
                            Debug.Log($"  {entry}");

                        if (data.playerScore != null)
                            Debug.Log($"  Player: {data.playerScore}");
                    }
                    else
                    {
                        Debug.LogError($"[EpicLeaderboard] GetScores failed: {result.Error}");
                    }
                },
                username: testUsername);
        }

        [ContextMenu("Submit Score (Inspector Workflow)")]
        public void SubmitScoreInspector()
        {
            EpicLeaderboardClient.SubmitScore(game, boardDefinition, testUsername, testScore, result =>
            {
                if (result.Success)
                {
                    var data = result.Value;
                    Debug.Log($"[EpicLeaderboard] Score submitted! " +
                              $"New best: {data.WasNewBest}, " +
                              $"Updated: {data.updatedTimeframes}");
                }
                else
                {
                    Debug.LogError($"[EpicLeaderboard] SubmitScore failed: {result.Error}");
                }
            });
        }

        // =================================================================
        // Code Workflow — Dynamic / UGC
        // =================================================================

        [ContextMenu("Get Scores (Dynamic Workflow)")]
        public void GetScoresDynamic()
        {
            // Create references at runtime — for UGC, procedural levels, etc.
            var dynamicGame = EpicLeaderboardGame.Create("your-game-id", "your-game-key");
            var dynamicBoard = BoardDefinition.Create("ugc_map_abc123", "speedrun");

            EpicLeaderboardClient.GetScores(dynamicGame, dynamicBoard, result =>
            {
                if (result.Success)
                    Debug.Log($"[EpicLeaderboard] UGC board has {result.Value.totalEntries} entries");
                else
                    Debug.LogError($"[EpicLeaderboard] Failed: {result.Error}");
            });
        }

        // =================================================================
        // Username Validation
        // =================================================================

        [ContextMenu("Check Username")]
        public void CheckUsername()
        {
            EpicLeaderboardClient.IsUsernameAvailable(game, testUsername, result =>
            {
                if (result.Success)
                {
                    switch (result.Value)
                    {
                        case UsernameAvailability.Available:
                            Debug.Log($"[EpicLeaderboard] '{testUsername}' is available!");
                            break;
                        case UsernameAvailability.Taken:
                            Debug.Log($"[EpicLeaderboard] '{testUsername}' is already taken.");
                            break;
                        case UsernameAvailability.Profanity:
                            Debug.Log($"[EpicLeaderboard] '{testUsername}' contains profanity.");
                            break;
                        case UsernameAvailability.Invalid:
                            Debug.Log($"[EpicLeaderboard] '{testUsername}' is invalid.");
                            break;
                    }
                }
                else
                {
                    Debug.LogError($"[EpicLeaderboard] Check failed: {result.Error}");
                }
            });
        }

        // =================================================================
        // Submit with Metadata
        // =================================================================

        [ContextMenu("Submit Score with Metadata")]
        public void SubmitScoreWithMetadata()
        {
            var metadata = new System.Collections.Generic.Dictionary<string, string>
            {
                ["character"] = "warrior",
                ["time"] = "02:34",
                ["level"] = "5"
            };

            EpicLeaderboardClient.SubmitScore(game, boardDefinition, testUsername, testScore, metadata, result =>
            {
                if (result.Success)
                {
                    Debug.Log($"[EpicLeaderboard] Score with metadata submitted! " +
                              $"Updated timeframes: {result.Value.updatedTimeframes}");
                }
                else
                {
                    Debug.LogError($"[EpicLeaderboard] Failed: {result.Error}");
                }
            });
        }
    }
}