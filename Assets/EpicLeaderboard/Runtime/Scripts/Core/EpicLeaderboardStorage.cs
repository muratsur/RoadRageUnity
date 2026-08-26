using UnityEngine;

namespace EpicLeaderboard
{
    public static class EpicLeaderboardStorage
    {
        private const string PREFIX = "EpicLeaderboard_";

        public static string Username
        {
            get => PlayerPrefs.GetString(PREFIX + "Username", "");
            set
            {
                PlayerPrefs.SetString(PREFIX + "Username", value);
                PlayerPrefs.Save();
            }
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PREFIX + "Username");
        }
    }
}