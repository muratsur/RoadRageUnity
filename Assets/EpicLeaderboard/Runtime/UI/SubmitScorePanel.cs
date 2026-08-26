using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace EpicLeaderboard
{
    public class SubmitScoreValues
    {
        public string username;
        public double score;
    }

    public class SubmitScorePanel : MonoBehaviour
    {
        [Header("References (drag from hierarchy)")] //
        [SerializeField]
        Image bg;

        // Texts
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text usernameStatus;

        // Inputs
        [SerializeField] TMP_InputField usernameInput;
        [SerializeField] TMP_InputField scoreInput;

        // Buttons
        [SerializeField] Button submitButton;
        [SerializeField] Button closeButton;

        // Definition
        private EpicLeaderboardGame game; // Game definition

        public Action<SubmitScoreValues> OnSubmit;

        public void SetUsername(string username)
        {
            usernameInput.text = username;
        }

        private void OnUsernameChanged(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                usernameStatus.text = "";
                return;
            }

            usernameStatus.text = "Checking...";
            usernameStatus.color = Color.gray;

            this.Debounce("username_check", 0.5f, () =>
            {
                EpicLeaderboardClient.IsUsernameAvailable(game, username, epicResult =>
                {
                    usernameStatus.text = epicResult.Value switch
                    {
                        UsernameAvailability.Available => "Available",
                        UsernameAvailability.Taken => "Username taken",
                        UsernameAvailability.Invalid => "Invalid characters",
                        UsernameAvailability.Profanity => "Not allowed",
                        _ => ""
                    };

                    usernameStatus.color = epicResult.Value == UsernameAvailability.Available
                        ? Color.green
                        : Color.red;
                });
            });
        }

        void OnValidate()
        {
            bg.color = new Color(0, 0, 0, 0.85f);
        }

        void Start()
        {
            // buttons
            submitButton.onClick.AddListener(OnSubmitButtonClick);
            closeButton.onClick.AddListener(OnCloseButtonClick);

            // inputs
            usernameInput.onValueChanged.AddListener(OnUsernameChanged);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var selected = EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>();

                Selectable next = Input.GetKey(KeyCode.LeftShift)
                    ? selected.FindSelectableOnUp()
                    : selected.FindSelectableOnDown();

                if (next != null)
                {
                    EventSystem.current.SetSelectedGameObject(next.gameObject, new BaseEventData(EventSystem.current));
                }
            }
        }

        void OnSubmitButtonClick()
        {
            OnSubmit?.Invoke(new SubmitScoreValues
            {
                username = usernameInput.text,
                score = double.Parse(scoreInput.text)
            });
        }

        void OnCloseButtonClick()
        {
            Destroy(gameObject);
        }

        public void Configure(EpicLeaderboardGame game)
        {
            this.game = game;
        }
    }
}