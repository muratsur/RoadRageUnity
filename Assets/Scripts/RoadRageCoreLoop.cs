using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    // Centralises the run lifecycle that was scattered across
    // RoadRageBootstrap:231 (picker), RoadRageBootstrap:235 (Update input),
    // RoadRageHUD:4683 (garage/missions) and GameState:178 (BeginRun/EndRun).
    // Single source of truth for phase → use GameLoop.Instance.Phase for UI/logic.
    public enum GamePhase { Boot, Picker, Driving, Paused, GameOver, Garage, Missions }

    public sealed class RoadRageCoreLoop : MonoBehaviour
    {
        public static RoadRageCoreLoop Instance { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Boot;

        private RoadRageBootstrap world;
        private ArcadeCarController player;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start() => BindWorld();

        private void OnSceneLoaded(Scene s, LoadSceneMode m) => BindWorld();

        private void BindWorld()
        {
            world = FindAnyObjectByType<RoadRageBootstrap>();
            player = FindAnyObjectByType<ArcadeCarController>();
            if (world == null) return;
            // First boot always shows picker unless deep-linked via -biome=
            Phase = world.PickerOpen ? GamePhase.Picker : GamePhase.Driving;
            Time.timeScale = Phase == GamePhase.Picker ? 0f : 1f;
        }

        private void Update()
        {
            if (world == null) world = FindAnyObjectByType<RoadRageBootstrap>();
            if (player == null) player = FindAnyObjectByType<ArcadeCarController>();
            if (world == null) return;

            // Global input - handles Keyboard ESC and Gamepad Start / Menu button
            if (GameInput.GetEscapePressed())
            {
                if (Phase == GamePhase.Picker || world.PickerOpen) ClosePicker();
                else if (Phase == GamePhase.Driving) OpenPicker();
                else if (Phase == GamePhase.Garage || Phase == GamePhase.Missions) ReturnToDriving();
                else if (Phase == GamePhase.Paused) Resume();
                else if (Phase == GamePhase.GameOver) RestartRun();
            }

            // Run-over detection
            if (Phase == GamePhase.Driving && GameState.RunOver)
                SetPhase(GamePhase.GameOver);

            // Tick score/combo timers
            if (Phase == GamePhase.Driving) GameState.Tick(Time.deltaTime);
        }

        // ---- Transitions ----
        public void OpenPicker()
        {
            world.OpenPicker(); // RoadRageBootstrap:200 sets Time.timeScale 0
            SetPhase(GamePhase.Picker);
        }

        public void ClosePicker()
        {
            world.ClosePicker(); // RoadRageBootstrap:206 restores timescale
            SetPhase(GamePhase.Driving);
        }

        public void SelectBiome(string biome) // called from picker UI
        {
            world.SelectBiome(biome); // RoadRageBootstrap:213
            // SelectBiome either closes picker (same biome) or ReloadBiome
            if (Phase == GamePhase.Picker) SetPhase(GamePhase.Driving);
        }

        public void OpenGarage() => SetPhase(GamePhase.Garage);
        public void OpenMissions() => SetPhase(GamePhase.Missions);
        public void ReturnToDriving() => SetPhase(GamePhase.Driving);

        public void Pause() { Time.timeScale = 0f; SetPhase(GamePhase.Paused); }
        public void Resume() { Time.timeScale = 1f; SetPhase(GamePhase.Driving); }

        public void RestartRun()
        {
            GameState.BeginRun(); // GameState:178 resets Integrity, Score, Distance
            world.ReloadBiome(world.BiomeName); // RoadRageBootstrap:220
            SetPhase(GamePhase.Driving);
        }

        // Called by TrafficCarController:452 / ArcadeCarController:4438 when integrity hits 0
        public void NotifyRunOver() => SetPhase(GamePhase.GameOver);

        private void SetPhase(GamePhase next)
        {
            if (Phase == next) return;
            var prev = Phase;
            Phase = next;
            // Keep legacy flags in sync so existing HUD/bootstrap don't diverge
            if (next == GamePhase.Driving && prev == GamePhase.Picker) world.ClosePicker();
            if (next == GamePhase.Picker && prev != GamePhase.Picker) world.OpenPicker();
            Debug.Log($"RR_PHASE {prev}->{next} integrity={GameState.Integrity:0} score={GameState.Score} km={GameState.RunDistanceKm:0.00}");
        }
    }
}
