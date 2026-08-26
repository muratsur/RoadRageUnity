using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EpicLeaderboard
{
    /// <summary>
    /// Main API client for Epic Leaderboard.
    /// All methods are static and use callbacks — no MonoBehaviour needed on the caller side.
    /// An internal hidden GameObject runs the coroutines.
    ///
    /// <code>
    /// // ScriptableObject workflow (Inspector)
    /// EpicLeaderboardClient.GetScores(game, board, result =&gt; {
    ///     if (result.Success)
    ///         Debug.Log($"Got {result.Value.scores.Count} scores!");
    /// });
    ///
    /// // Dynamic workflow (UGC)
    /// EpicLeaderboardClient.GetScores(game, "ugc_map_123", "", result =&gt; { ... });
    /// </code>
    /// </summary>
    public static class EpicLeaderboardClient
    {
        private const string BASE_URL = "https://epicleaderboard.com/api";
        private const string USER_AGENT = "X-EpicLeaderboard Unity";

        // Internal coroutine runner — created lazily, survives scene loads
        private static CoroutineRunner _runner;

        private static CoroutineRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("[EpicLeaderboard]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _runner = go.AddComponent<CoroutineRunner>();
                }

                return _runner;
            }
        }

        private class CoroutineRunner : MonoBehaviour
        {
        }

        // =====================================================================
        // GetScores
        // =====================================================================

        /// <summary>
        /// Get leaderboard scores using ScriptableObject references.
        /// </summary>
        public static void GetScores(
            EpicLeaderboardGame game,
            BoardDefinition boardDefinition,
            Action<EpicResult<GetScoresResponse>> callback,
            string username = "",
            Timeframe timeframe = Timeframe.AllTime,
            bool aroundPlayer = false,
            bool localCountryOnly = false)
        {
            GetScores(game, boardDefinition.primaryID, boardDefinition.secondaryID, callback,
                username, timeframe, aroundPlayer, localCountryOnly);
        }

        /// <summary>
        /// Get leaderboard scores using raw string IDs (for dynamic/UGC boards).
        /// </summary>
        public static void GetScores(
            EpicLeaderboardGame game,
            string primaryID,
            string secondaryID,
            Action<EpicResult<GetScoresResponse>> callback,
            string username = "",
            Timeframe timeframe = Timeframe.AllTime,
            bool aroundPlayer = false,
            bool localCountryOnly = false)
        {
            if (!ValidateGame(game, callback))
                return;

            Runner.StartCoroutine(GetScoresCoroutine(
                game, primaryID, secondaryID, callback,
                username, timeframe, aroundPlayer, localCountryOnly));
        }

        private static IEnumerator GetScoresCoroutine(
            EpicLeaderboardGame game,
            string primaryID,
            string secondaryID,
            Action<EpicResult<GetScoresResponse>> callback,
            string username,
            Timeframe timeframe,
            bool aroundPlayer,
            bool localCountryOnly)
        {
            var query = new Dictionary<string, string>
            {
                ["gameID"] = game.gameID,
                ["primaryID"] = primaryID,
                ["timeframe"] = ((int)timeframe).ToString(),
                ["around"] = aroundPlayer ? "1" : "0",
                ["local"] = localCountryOnly ? "1" : "0"
            };

            if (!string.IsNullOrEmpty(secondaryID))
                query["secondaryID"] = secondaryID;

            if (!string.IsNullOrEmpty(username))
                query["username"] = username;

            string url = BASE_URL + "/getScores?" + EncodeQueryParams(query);

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("User-Agent", USER_AGENT);
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(EpicResult<GetScoresResponse>.Fail(
                        request.error, request.responseCode));
                    yield break;
                }

                var response = JsonHelper.ParseGetScoresResponse(
                    request.downloadHandler.text);

                callback?.Invoke(EpicResult<GetScoresResponse>.Ok(
                    response, request.responseCode));
            }
        }

        // =====================================================================
        // SubmitScore
        // =====================================================================

        /// <summary>
        /// Submit a score using ScriptableObject references.
        /// </summary>
        public static void SubmitScore(
            EpicLeaderboardGame game,
            BoardDefinition boardDefinition,
            string username,
            double score,
            Action<EpicResult<SubmitScoreResponse>> callback,
            string meta = "")
        {
            SubmitScore(game, boardDefinition.primaryID, boardDefinition.secondaryID,
                username, score, callback, meta);
        }

        /// <summary>
        /// Submit a score with a metadata dictionary.
        /// </summary>
        public static void SubmitScore(
            EpicLeaderboardGame game,
            BoardDefinition boardDefinition,
            string username,
            double score,
            Dictionary<string, string> metadata,
            Action<EpicResult<SubmitScoreResponse>> callback)
        {
            string meta = JsonHelper.SerializeStringMap(metadata);
            SubmitScore(game, boardDefinition.primaryID, boardDefinition.secondaryID,
                username, score, callback, meta);
        }

        /// <summary>
        /// Submit a score using raw string IDs (for dynamic/UGC boards).
        /// </summary>
        public static void SubmitScore(
            EpicLeaderboardGame game,
            string primaryID,
            string secondaryID,
            string username,
            double score,
            Action<EpicResult<SubmitScoreResponse>> callback,
            string meta = "")
        {
            if (!ValidateGame(game, callback))
                return;

            Runner.StartCoroutine(SubmitScoreCoroutine(
                game, primaryID, secondaryID, username, score, callback, meta));
        }

        private static IEnumerator SubmitScoreCoroutine(
            EpicLeaderboardGame game,
            string primaryID,
            string secondaryID,
            string username,
            double score,
            Action<EpicResult<SubmitScoreResponse>> callback,
            string meta)
        {
            var form = new Dictionary<string, string>
            {
                ["gameID"] = game.gameID,
                ["gameKey"] = game.gameKey,
                ["primaryID"] = primaryID,
                ["username"] = username,
                ["score"] = score.ToString("G17")
            };

            if (!string.IsNullOrEmpty(secondaryID))
                form["secondaryID"] = secondaryID;

            if (!string.IsNullOrEmpty(meta))
                form["meta"] = meta;

            string body = EncodeQueryParams(form);

            using (var request = new UnityWebRequest(BASE_URL + "/submitScore", "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                request.SetRequestHeader("User-Agent", USER_AGENT);
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(EpicResult<SubmitScoreResponse>.Fail(
                        request.error, request.responseCode));
                    yield break;
                }

                if (request.responseCode != 200)
                {
                    callback?.Invoke(EpicResult<SubmitScoreResponse>.Fail(
                        request.downloadHandler.text, request.responseCode));
                    yield break;
                }

                // Parse bitflag response
                var response = new SubmitScoreResponse();
                string responseText = request.downloadHandler.text.Trim();

                if (int.TryParse(responseText, out int flags))
                {
                    response.updatedTimeframes = (TimeframeUpdateResult)flags;
                }

                callback?.Invoke(EpicResult<SubmitScoreResponse>.Ok(
                    response, request.responseCode));
            }
        }

        // =====================================================================
        // IsUsernameAvailable
        // =====================================================================

        /// <summary>
        /// Check if a username is available (v2 endpoint with detailed result).
        /// </summary>
        public static void IsUsernameAvailable(
            EpicLeaderboardGame game,
            string username,
            Action<EpicResult<UsernameAvailability>> callback)
        {
            if (!ValidateGame(game, callback))
                return;

            Runner.StartCoroutine(IsUsernameAvailableCoroutine(
                game, username, callback));
        }

        private static IEnumerator IsUsernameAvailableCoroutine(
            EpicLeaderboardGame game,
            string username,
            Action<EpicResult<UsernameAvailability>> callback)
        {
            var query = new Dictionary<string, string>
            {
                ["gameID"] = game.gameID,
                ["username"] = username
            };

            string url = BASE_URL + "/isUsernameAvailable_v2?" + EncodeQueryParams(query);

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("User-Agent", USER_AGENT);
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(EpicResult<UsernameAvailability>.Fail(
                        request.error, request.responseCode));
                    yield break;
                }

                string responseText = request.downloadHandler.text.Trim();

                if (int.TryParse(responseText, out int code) && code >= 0 && code <= 3)
                {
                    callback?.Invoke(EpicResult<UsernameAvailability>.Ok(
                        (UsernameAvailability)code, request.responseCode));
                }
                else
                {
                    callback?.Invoke(EpicResult<UsernameAvailability>.Fail(
                        $"Unexpected response: {responseText}", request.responseCode));
                }
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static bool ValidateGame<T>(EpicLeaderboardGame game, Action<EpicResult<T>> callback)
        {
            if (game == null)
            {
                Debug.LogError("[EpicLeaderboard] Game reference is null. " +
                               "Assign an EpicLeaderboardGame asset or use EpicLeaderboardGame.Create().");
                callback?.Invoke(EpicResult<T>.Fail("Game reference is null"));
                return false;
            }

            if (!game.IsValid())
            {
                Debug.LogError("[EpicLeaderboard] Game ID or Game Key is empty. " +
                               "Configure them in the EpicLeaderboardGame asset.");
                callback?.Invoke(EpicResult<T>.Fail("Game ID or Game Key is empty"));
                return false;
            }

            return true;
        }

        private static string EncodeQueryParams(Dictionary<string, string> parameters)
        {
            var sb = new System.Text.StringBuilder();
            bool first = true;

            foreach (var kvp in parameters)
            {
                if (!first) sb.Append('&');
                first = false;
                sb.Append(UnityWebRequest.EscapeURL(kvp.Key));
                sb.Append('=');
                sb.Append(UnityWebRequest.EscapeURL(kvp.Value));
            }

            return sb.ToString();
        }
    }
}