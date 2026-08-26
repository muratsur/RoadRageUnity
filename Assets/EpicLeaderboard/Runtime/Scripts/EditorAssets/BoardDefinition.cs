using UnityEngine;

namespace EpicLeaderboard
{
    /// <summary>
    /// Identifies a specific leaderboard within a game.
    /// Create as an Editor asset (Right Click → Create → Epic Leaderboard → Leaderboard)
    /// or instantiate at runtime via <see cref="Create"/> for dynamic/UGC boards.
    /// </summary>
    [CreateAssetMenu(fileName = "Leaderboard", menuName = "Epic Leaderboard/Leaderboard", order = 1)]
    public class BoardDefinition : ScriptableObject
    {
        [Tooltip("Primary leaderboard identifier (e.g. level name, game mode).")]
        public string primaryID;

        [Tooltip("Optional secondary identifier (e.g. difficulty, character class). Leave empty if not needed.")]
        public string secondaryID;

        /// <summary>
        /// Create a runtime instance (not saved to disk).
        /// Useful for user-generated content or procedural levels.
        /// </summary>
        public static BoardDefinition Create(string primaryID, string secondaryID = "")
        {
            var instance = CreateInstance<BoardDefinition>();
            instance.primaryID = primaryID;
            instance.secondaryID = secondaryID;
            return instance;
        }

        /// <summary>
        /// Validates that the primary ID is set 
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(primaryID);
        }
    }
}