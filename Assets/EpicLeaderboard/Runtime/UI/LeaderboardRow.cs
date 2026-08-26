using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace EpicLeaderboard
{
    public class LeaderboardRow : MonoBehaviour
    {
        [Header("References (drag from hierarchy)")] [SerializeField]
        Image bg;

        [SerializeField] TMP_Text rankText;
        [SerializeField] TMP_Text rankSuffix;
        [SerializeField] TMP_Text usernameText;
        [SerializeField] TMP_Text scoreText;
        [SerializeField] Image countryFlag;

        [Header("Data")] [SerializeField] int rank = 1;
        [SerializeField] string countryCode = "DE";
        [SerializeField] string username = "Player123";
        [SerializeField] string score = "12345";
        [SerializeField] bool highlightRow = false;

        [Header("Flag Atlas")] [SerializeField]
        SpriteAtlas flagAtlas;

        string getRankSuffix(int rank)
        {
            if (rank % 100 >= 11 && rank % 100 <= 13)
                return "th";

            return (rank % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        void OnValidate()
        {
            if (rankText != null)
            {
                rankText.text = rank.ToString();
            }

            if (rankSuffix != null)
            {
                rankSuffix.text = getRankSuffix(rank);
            }

            if (usernameText != null)
            {
                usernameText.text = username;
            }

            if (scoreText != null)
            {
                scoreText.text = score;
            }

            if (countryFlag != null)
            {
                var sprite = flagAtlas.GetSprite(countryCode.ToUpper());
                countryFlag.sprite = sprite;
            }

            if (highlightRow)
            {
                bg.color = Color.red;
            }
            else
            {
                bg.color = (rank % 2 == 0) ? new Color(0, 0, 0, 0.5f) : Color.clear;
            }
        }

        public void Populate(ScoreEntry entry, bool highlight = false)
        {
            rank = entry.rank;
            username = entry.username;
            score = entry.score;
            countryCode = entry.country;
            highlightRow = highlight;
            OnValidate();
        }
    }
}