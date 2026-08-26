using UnityEngine;
using UnityEditor;

namespace EpicLeaderboard.Editor
{
    [CustomEditor(typeof(EpicLeaderboardGame))]
    public class EpicLeaderboardGameEditor : UnityEditor.Editor
    {
        private SerializedProperty _gameID;
        private SerializedProperty _gameKey;
        private bool _showKey = false;

        private void OnEnable()
        {
            _gameID = serializedObject.FindProperty("gameID");
            _gameKey = serializedObject.FindProperty("gameKey");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Epic Leaderboard — Game Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            // Game ID
            EditorGUILayout.PropertyField(_gameID, new GUIContent("Game ID", "From the Epic Leaderboard dashboard."));

            // Game Key with show/hide toggle
            EditorGUILayout.BeginHorizontal();
            if (_showKey)
            {
                EditorGUILayout.PropertyField(_gameKey,
                    new GUIContent("Game Key", "Secret key — treat like a password."));
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                string masked = string.IsNullOrEmpty(_gameKey.stringValue)
                    ? ""
                    : new string('•', Mathf.Min(_gameKey.stringValue.Length, 32));
                EditorGUILayout.TextField(new GUIContent("Game Key", "Secret key — treat like a password."), masked);
                EditorGUI.EndDisabledGroup();
            }

            if (GUILayout.Button(_showKey ? "Hide" : "Show", GUILayout.Width(50)))
            {
                _showKey = !_showKey;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Validation
            var game = (EpicLeaderboardGame)target;
            if (!game.IsValid())
            {
                EditorGUILayout.HelpBox(
                    "Game ID and Game Key are required. Get them from epicleaderboard.com.",
                    MessageType.Warning);
            }

            // Dashboard link
            EditorGUILayout.Space(4);
            if (GUILayout.Button("Open Epic Leaderboard Dashboard"))
            {
                Application.OpenURL("https://epicleaderboard.com");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}