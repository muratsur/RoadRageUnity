using System.Collections.Generic;
using UnityEngine;

namespace EpicLeaderboard
{
    /// <summary>
    /// Lightweight JSON helpers that work within Unity's JsonUtility limitations.
    /// No external dependencies. Handles the specific JSON shapes of the Epic Leaderboard API.
    /// </summary>
    public static class JsonHelper
    {
        // ---------------------------------------------------------------------
        // GetScores response parsing
        // ---------------------------------------------------------------------

        /// <summary>
        /// Parse the full getScores JSON response.
        /// We parse manually because Unity's JsonUtility cannot handle:
        /// - Nullable objects (playerscore can be absent)
        /// - double fields (JsonUtility only does float)
        /// - Nested arrays of objects reliably
        /// </summary>
        public static GetScoresResponse ParseGetScoresResponse(string json)
        {
            var response = new GetScoresResponse();

            // Use Unity's built-in JSON parser via the internal SimpleJSON-style approach
            // We wrap in a try-catch since we're parsing untrusted server data
            try
            {
                var wrapper = JsonUtility.FromJson<GetScoresResponseRaw>(json);

                if (wrapper.scores != null)
                {
                    foreach (var raw in wrapper.scores)
                    {
                        response.scores.Add(raw.ToScoreEntry());
                    }
                }

                response.totalEntries = wrapper.totalEntries;

                if (wrapper.playerscore != null && !string.IsNullOrEmpty(wrapper.playerscore.username))
                {
                    response.playerScore = wrapper.playerscore.ToScoreEntry();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EpicLeaderboard] Failed to parse getScores response: {e.Message}");
            }

            return response;
        }

        // Raw types for JsonUtility deserialization
        [System.Serializable]
        private class GetScoresResponseRaw
        {
            public ScoreEntryRaw[] scores;
            public int totalEntries;
            public ScoreEntryRaw playerscore;
        }

        [System.Serializable]
        private class ScoreEntryRaw
        {
            public int rank;
            public string username;
            public string score;
            public string country;
            public string meta;

            public ScoreEntry ToScoreEntry()
            {
                return new ScoreEntry
                {
                    rank = rank,
                    username = username ?? "",
                    score = score ?? "",
                    country = country ?? "",
                    meta = meta ?? ""
                };
            }
        }

        // Wrapper needed because JsonUtility can't deserialize top-level arrays
        [System.Serializable]
        private class ScoreEntryArray
        {
            public ScoreEntryRaw[] items;
        }

        // ---------------------------------------------------------------------
        // Metadata string map parsing
        // ---------------------------------------------------------------------

        /// <summary>
        /// Deserialize a flat JSON object into a string→string dictionary.
        /// Handles the meta field format: {"character":"warrior","time":"02:34"}
        /// </summary>
        public static Dictionary<string, string> DeserializeStringMap(string json)
        {
            var map = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(json))
                return map;

            // Simple manual parser for flat {"key":"value",...} objects
            // This avoids pulling in a full JSON library just for metadata
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return map;

            // Remove outer braces
            json = json.Substring(1, json.Length - 2).Trim();
            if (string.IsNullOrEmpty(json))
                return map;

            // State machine parser for proper quote/escape handling
            int i = 0;
            while (i < json.Length)
            {
                // Skip whitespace
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length) break;

                // Parse key
                string key = ParseQuotedString(json, ref i);
                if (key == null) break;

                // Skip whitespace and colon
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ':')) i++;

                // Parse value
                string value = ParseQuotedString(json, ref i);
                if (value == null) break;

                map[key] = value;

                // Skip whitespace and comma
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
            }

            return map;
        }

        /// <summary>
        /// Serialize a string→string dictionary into a flat JSON object.
        /// </summary>
        public static string SerializeStringMap(Dictionary<string, string> map)
        {
            if (map == null || map.Count == 0)
                return "";

            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kvp in map)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"');
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":\"");
                sb.Append(EscapeJsonString(kvp.Value));
                sb.Append('"');
            }

            sb.Append('}');
            return sb.ToString();
        }

        // ---------------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------------

        private static string ParseQuotedString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
                return null;

            i++; // skip opening quote
            var sb = new System.Text.StringBuilder();

            while (i < json.Length)
            {
                char c = json[i];

                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    char escaped = json[i];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(escaped); break;
                    }
                }
                else if (c == '"')
                {
                    i++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }

                i++;
            }

            return sb.ToString(); // unterminated string, return what we have
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}