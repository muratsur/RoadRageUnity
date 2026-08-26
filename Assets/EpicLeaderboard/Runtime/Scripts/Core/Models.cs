using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace EpicLeaderboard
{
    // -------------------------------------------------------------------------
    // Enums
    // -------------------------------------------------------------------------

    /// <summary>
    /// Timeframe filter for score queries.
    /// Timeframes other than AllTime require the game owner to have an active subscription.
    /// </summary>
    public enum Timeframe
    {
        AllTime = 0,
        Year = 1,
        Month = 2,
        Week = 3,
        Day = 4
    }

    /// <summary>
    /// Bitflag result from submitScore indicating which timeframe leaderboards were updated.
    /// Multiple flags can be combined (e.g. AllTime | Month | Day = 21).
    /// </summary>
    [Flags]
    public enum TimeframeUpdateResult
    {
        None = 0,
        AllTime = 1,
        Year = 2,
        Month = 4,
        Week = 8,
        Day = 16
    }

    /// <summary>
    /// Detailed result of a username availability check (v2 endpoint).
    /// </summary>
    public enum UsernameAvailability
    {
        Available = 0,
        Invalid = 1,
        Profanity = 2,
        Taken = 3
    }

    // -------------------------------------------------------------------------
    // Data Types
    // -------------------------------------------------------------------------

    /// <summary>
    /// A single entry on a leaderboard.
    /// </summary>
    [Serializable]
    public class ScoreEntry
    {
        /// <summary>1-based rank on the leaderboard.</summary>
        public int rank;

        /// <summary>Player display name.</summary>
        public string username;

        /// <summary>The player's score, formatted by the server.</summary>
        public string score;

        /// <summary>ISO 3166-1 alpha-2 country code (e.g. "US", "DE"). May be empty.</summary>
        public string country;

        /// <summary>Raw metadata JSON string as stored on the server.</summary>
        public string meta;

        /// <summary>Lazily parsed metadata dictionary.</summary>
        private Dictionary<string, string> _metadata;

        /// <summary>
        /// Get the metadata as a string→string dictionary.
        /// Parsed lazily from the raw <see cref="meta"/> JSON string.
        /// Returns an empty dictionary if meta is null/empty or invalid JSON.
        /// </summary>
        public Dictionary<string, string> GetMetadata()
        {
            if (_metadata != null)
                return _metadata;

            _metadata = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(meta))
                return _metadata;

            _metadata = JsonHelper.DeserializeStringMap(meta);
            return _metadata;
        }

        public override string ToString()
        {
            return $"#{rank} {username}: {score} ({country})";
        }
    }

    // -------------------------------------------------------------------------
    // Response Types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Response from getScores endpoint.
    /// </summary>
    [Serializable]
    public class GetScoresResponse
    {
        /// <summary>List of score entries, sorted by rank. Up to 50 entries.</summary>
        public List<ScoreEntry> scores = new List<ScoreEntry>();

        /// <summary>Total number of entries in this leaderboard for the active filters.</summary>
        public int totalEntries;

        /// <summary>
        /// The requesting player's own score if they provided a username
        /// and are not already in the scores list. Null otherwise.
        /// </summary>
        [CanBeNull] public ScoreEntry playerScore;
    }

    /// <summary>
    /// Response from submitScore endpoint.
    /// </summary>
    [Serializable]
    public class SubmitScoreResponse
    {
        /// <summary>
        /// Bitflag indicating which timeframe leaderboards were updated.
        /// </summary>
        public TimeframeUpdateResult updatedTimeframes;

        /// <summary>Check if a specific timeframe was updated.</summary>
        public bool WasUpdated(TimeframeUpdateResult flag)
        {
            return (updatedTimeframes & flag) != 0;
        }

        /// <summary>True if at least one timeframe was updated (score was a new best).</summary>
        public bool WasNewBest => updatedTimeframes != TimeframeUpdateResult.None;
    }

    // -------------------------------------------------------------------------
    // Result Wrapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Result wrapper for all API calls. Contains either a value or an error.
    /// Inspired by Rust's Result type — no exceptions, explicit error handling.
    /// </summary>
    public class EpicResult<T>
    {
        /// <summary>True if the request succeeded.</summary>
        public bool Success { get; private set; }

        /// <summary>The result value. Only valid when Success is true.</summary>
        public T Value { get; private set; }

        /// <summary>Error message. Only valid when Success is false.</summary>
        public string Error { get; private set; }

        /// <summary>HTTP status code. 0 if the request failed to connect.</summary>
        public long StatusCode { get; private set; }

        private EpicResult()
        {
        }

        public static EpicResult<T> Ok(T value, long statusCode = 200)
        {
            return new EpicResult<T>
            {
                Success = true,
                Value = value,
                StatusCode = statusCode
            };
        }

        public static EpicResult<T> Fail(string error, long statusCode = 0)
        {
            return new EpicResult<T>
            {
                Success = false,
                Error = error,
                StatusCode = statusCode
            };
        }

        public override string ToString()
        {
            return Success ? $"OK: {Value}" : $"Error ({StatusCode}): {Error}";
        }
    }
}