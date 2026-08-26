using System;
using UnityEngine;
using UnityEngine.UI;

namespace EpicLeaderboard
{
    public class DemoController : MonoBehaviour
    {
        [Header("Configuration")] // 
        [SerializeField]
        private EpicLeaderboardGame game; // Game definition

        [SerializeField] private BoardDefinition boardDefinition; // Leaderboard definition

        [Header("References")] //
        [SerializeField]
        private LeaderboardPanel leaderboardUI;

        [SerializeField] private Button submitScoreButton;

        [SerializeField] private Toggle aroundPlayerToggle;
        [SerializeField] private Toggle LocalCountryToggle;

        [SerializeField] public GameObject submitScoreModalPrefab;

        // data
        private string demoUsername;
        private GameObject submitScoreModal;

        private void Start()
        {
            submitScoreButton.onClick.AddListener(OnClickSubmitScore);
            aroundPlayerToggle.onValueChanged.AddListener(_ => RefreshLeaderboard());
            LocalCountryToggle.onValueChanged.AddListener(_ => RefreshLeaderboard());

            // set username from Storage if available 
            demoUsername = EpicLeaderboardStorage.Username;

            // Leaderboard laden
            RefreshLeaderboard();
        }

        public void RefreshLeaderboard()
        {
            EpicLeaderboardClient.GetScores(game, boardDefinition, (result) =>
            {
                if (result.Success)
                {
                    leaderboardUI.DisplayResult(result.Value, demoUsername);
                }
            }, username: demoUsername, Timeframe.AllTime, aroundPlayerToggle.isOn, LocalCountryToggle.isOn);
        }

        public void OnClickSubmitScore()
        {
            Console.WriteLine("Submitting score...");

            if (submitScoreModal)
            {
                DestroyImmediate(submitScoreModal);
            }

            submitScoreModal = Instantiate(submitScoreModalPrefab, Vector3.zero, Quaternion.identity);
            submitScoreModal.transform.SetAsLastSibling();

            var submitScorePanel = submitScoreModal.GetComponent<SubmitScorePanel>();
            submitScorePanel.SetUsername(demoUsername);
            submitScorePanel.OnSubmit = (values) =>
            {
                EpicLeaderboardClient.SubmitScore(game, boardDefinition, values.username, values.score,
                    (result) =>
                    {
                        if (result.Success)
                        {
                            //close the submit modal
                            DestroyImmediate(submitScoreModal);
                            submitScoreModal = null;

                            demoUsername = values.username;

                            // store submitted username
                            EpicLeaderboardStorage.Username = values.username;

                            // refetch Leaderboard
                            RefreshLeaderboard();
                        }
                        else
                        {
                            Debug.LogError($"[EpicLeaderboard] SubmitScore failed: {result.Error}");
                        }
                    });
            };
            submitScorePanel.Configure(game);
        }
    }
}