using UnityEngine;

namespace EpicLeaderboard
{
    /// <summary>
    /// Represents a game registered on Epic Leaderboard.
    /// Create as an Editor asset (Right Click → Create → Epic Leaderboard → Game)
    /// or instantiate at runtime via <see cref="Create"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "EpicLeaderboardGame", menuName = "Epic Leaderboard/Game", order = 0)]
    public class EpicLeaderboardGame : ScriptableObject
    {
        [Tooltip("The Game ID from the Epic Leaderboard dashboard.")]
        public string gameID;

        [Tooltip("The secret Game Key. Treat this like a password.")]
        public string gameKey;

        /// <summary>
        /// Create a runtime instance (not saved to disk).
        /// Useful for dynamic or user-generated content scenarios.
        /// </summary>
        public static EpicLeaderboardGame Create(string gameID, string gameKey)
        {
            var instance = CreateInstance<EpicLeaderboardGame>();
            instance.gameID = gameID;
            instance.gameKey = gameKey;
            return instance;
        }

        /// <summary>
        /// Validates that both gameID and gameKey are set.
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(gameID) && !string.IsNullOrEmpty(gameKey);
        }
    }
}