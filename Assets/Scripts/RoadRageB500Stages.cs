using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Time of day for a run on the B500, rolled fresh every run (see
    /// RoadRageBootstrap.RollRunConditions) so the same road looks different each time.
    public enum DayTime
    {
        Morning,
        Midday,
        Evening,
        Dusk,
    }

    /// The B500 run as a set of stages, OutRun style: a clock counts down to the next
    /// real stop on the road (Buehlerhoehe, Hundseck, Mummelsee, Ruhestein ...). Reach it
    /// and the time for the next stage is added to what is left; run out and the run is
    /// over. Takedowns buy a few seconds, so fighting and driving pay into the same
    /// clock. Each full stage's time is kept as a personal best to beat.
    ///
    /// Also the Blitzer speed traps, Forza style: pass a speed camera fast and it
    /// flashes, scores the speed with up to three stars, keeps a best per camera - and
    /// a really fast pass puts the police on you.
    ///
    /// Only active on a road with a route (Greenwood's B500); elsewhere it does nothing.
    public sealed class RoadRageB500Stages : MonoBehaviour
    {
        public static RoadRageB500Stages Instance { get; private set; }

        /// A stage clock is running this frame, so place names come from here rather
        /// than from the bootstrap's plain announcement.
        public static bool Running => Instance != null && Instance.armed;

        /// Par pace for a stage's time allowance. Generous on purpose: traffic, bends
        /// and fights all cost time, and the carried-over surplus is the reward.
        private const float ParSpeed = 92f / 3.6f;
        private const float OpeningGrace = 12f;
        private const float TakedownBonus = 3f;
        private const float ReviveBonus = 30f;
        private const float BlitzerLimit = 100f;

        private ArcadeCarController car;
        private readonly List<float> blitzers = new();
        private bool armed;
        private bool timedOut;
        private float timeLeft;
        private float runClock;
        private float stageStart;
        private float lastFold;
        private int from = -1;
        private int next = -1;
        private int lastTakedowns;
        private int lastWarnSecond = -1;
        private float flash;
        private string banner = string.Empty;
        private float bannerUntil;

        private GUIStyle timerStyle;
        private GUIStyle lineStyle;
        private GUIStyle bannerStyle;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime * 3.5f);
            var route = RoadPath.Route;
            if (route == null || route.Places.Length < 2)
            {
                armed = false;
                return;
            }
            if (car == null)
            {
                car = FindAnyObjectByType<ArcadeCarController>();
                if (car == null) return;
            }
            var landing = RoadRageLandingDirector.Instance != null &&
                          (RoadRageLandingDirector.Instance.IsLandingActive ||
                           RoadRageLandingDirector.Instance.IsTransitioningToRace);
            if (landing || car.CountdownTimer > 0f)
            {
                armed = false;
                return;
            }
            if (!armed) Arm(route);

            if (GameState.RunOver) return;
            if (timedOut)
            {
                // Revived after the clock ran out: a fresh stretch of time to use it.
                timedOut = false;
                timeLeft = ReviveBonus;
                Show($"⏱ +{ReviveBonus:0}s  BACK ON THE CLOCK");
            }

            var u = route.Fold(car.RoadDistance);
            var moved = Mathf.Abs(u - lastFold) < 60f && u != lastFold;
            if (!GameState.IsAftertouchActive)
            {
                var dt = Time.deltaTime;
                runClock += dt;
                timeLeft -= dt;
            }

            if (GameState.Takedowns > lastTakedowns)
            {
                var gained = (GameState.Takedowns - lastTakedowns) * TakedownBonus;
                timeLeft += gained;
                Show($"⏱ +{gained:0}s TAKEDOWN");
            }
            lastTakedowns = GameState.Takedowns;

            if (moved)
            {
                var target = Mathf.Min(route.Places[next].Distance, route.Length - 5f);
                if ((lastFold - target) * (u - target) <= 0f) ReachCheckpoint(route, Mathf.Sign(u - lastFold));
                foreach (var (site, index) in Sites())
                    if ((lastFold - site) * (u - site) <= 0f) Blitz(index);
            }
            lastFold = u;

            var seconds = Mathf.CeilToInt(timeLeft);
            if (seconds <= 10 && seconds > 0 && seconds != lastWarnSecond)
            {
                lastWarnSecond = seconds;
                RoadRageHaptics.Light();
            }
            if (timeLeft <= 0f)
            {
                timeLeft = 0f;
                timedOut = true;
                GameState.RunEndReason = "TIME UP";
                GameState.Show("⏱ TIME UP!");
                RoadRageHaptics.Heavy();
                GameState.EndRun();
            }
        }

        private IEnumerable<(float, int)> Sites()
        {
            for (var i = 0; i < blitzers.Count; i++) yield return (blitzers[i], i);
        }

        private void Arm(RoadRoute route)
        {
            var d = car.RoadDistance;
            var u = route.Fold(d);
            var forward = Mathf.Repeat(d, 2f * route.Length) < route.Length ? 1f : -1f;
            next = NextPlace(route, u, forward);
            from = -1;   // the first stage starts mid-road: no record for it
            timeLeft = Mathf.Abs(route.Places[next].Distance - u) / ParSpeed + OpeningGrace;
            runClock = 0f;
            stageStart = 0f;
            lastFold = u;
            lastTakedowns = GameState.Takedowns;
            lastWarnSecond = -1;
            timedOut = false;
            blitzers.Clear();
            blitzers.AddRange(RoadRageBootstrap.BlitzerSites());
            armed = true;
            Show($"NEXT STOP: {route.Places[next].Name.ToUpperInvariant()}");
        }

        /// The next stop in the direction of travel; past the last one the road turns
        /// back, so it is the nearest one the other way.
        private static int NextPlace(RoadRoute route, float u, float direction)
        {
            var places = route.Places;
            if (direction >= 0f)
            {
                for (var i = 0; i < places.Length; i++)
                    if (places[i].Distance > u + 1f) return i;
                return Mathf.Max(0, places.Length - 2);
            }
            for (var i = places.Length - 1; i >= 0; i--)
                if (places[i].Distance < u - 1f) return i;
            return Mathf.Min(1, places.Length - 1);
        }

        private void ReachCheckpoint(RoadRoute route, float direction)
        {
            var place = route.Places[next];
            var stageTime = runClock - stageStart;
            var record = string.Empty;
            if (from >= 0)
            {
                var key = $"RR_STAGE_{from}_{next}";
                var best = PlayerPrefs.GetFloat(key, 0f);
                if (best <= 0f || stageTime < best)
                {
                    PlayerPrefs.SetFloat(key, stageTime);
                    PlayerPrefs.Save();
                    record = best > 0f ? $"NEW RECORD {Clock(stageTime)} (-{best - stageTime:0.0}s)" : $"RECORD SET {Clock(stageTime)}";
                }
                else
                {
                    record = $"{Clock(stageTime)}  (+{stageTime - best:0.0}s)";
                }
            }
            GameState.Award(500 + Mathf.RoundToInt(timeLeft) * 20, "✅ STAGE CLEAR");

            from = next;
            next = NextPlace(route, place.Distance, direction);
            var added = Mathf.Abs(route.Places[next].Distance - place.Distance) / ParSpeed;
            timeLeft += added;
            stageStart = runClock;
            lastWarnSecond = -1;
            Show($"📍 {place.Name.ToUpperInvariant()}   ⏱ +{added:0}s\n{record}");
            RoadRageHaptics.Medium();
        }

        private void Blitz(int index)
        {
            var speed = car.SpeedKph;
            if (speed < BlitzerLimit) return;
            flash = 1f;
            var stars = speed >= 180f ? 3 : speed >= 150f ? 2 : speed >= 120f ? 1 : 0;
            var points = Mathf.RoundToInt(speed * 8f) * (1 + stars);
            var key = $"RR_BLITZ_{index}";
            var best = PlayerPrefs.GetFloat(key, 0f);
            var record = string.Empty;
            if (speed > best)
            {
                PlayerPrefs.SetFloat(key, speed);
                PlayerPrefs.Save();
                record = best > 0f ? "   NEW BEST!" : string.Empty;
            }
            else
            {
                record = $"   BEST {best:0}";
            }
            GameState.Award(points, "📸 SPEED TRAP");
            Show($"📸 BLITZER  {speed:0} km/h  {new string('★', stars)}{new string('☆', 3 - stars)}\n+{points:N0}{record}");
            RoadRageHaptics.Light();
            if (RoadRageAudioBridge.Instance != null) RoadRageAudioBridge.Instance.PlayCameraShutter();
            // Photographed doing 140+ on a 100 road: the police have your plate.
            if (speed >= 140f && RoadRagePolicePursuitDirector.Instance != null)
                RoadRagePolicePursuitDirector.Instance.AddHeat(speed >= 170f ? 1f : 0.5f);
        }

        private void Show(string text)
        {
            banner = text;
            bannerUntil = Time.unscaledTime + 2.6f;
        }

        private static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            var m = Mathf.FloorToInt(seconds / 60f);
            return $"{m}:{seconds - m * 60f:00.0}";
        }

        /// Drawn by RoadRageHUD during a run: the camera flash, the clock with the next
        /// stop and its best time, and the stage banner.
        public void DrawHud(GUIStyle title, GUIStyle readout)
        {
            if (!armed || RoadPath.Route == null) return;
            if (timerStyle == null)
            {
                timerStyle = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(title.fontSize, 30) + 10 };
                lineStyle = new GUIStyle(readout) { alignment = TextAnchor.MiddleCenter };
                bannerStyle = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            if (flash > 0f)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, flash * 0.75f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            var route = RoadPath.Route;
            var urgent = timeLeft < 10f;
            var pulse = urgent ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
            var colour = timerStyle.normal.textColor;
            timerStyle.normal.textColor = urgent ? new Color(1f, 0.3f, 0.25f, pulse) : new Color(1f, 0.92f, 0.55f);
            GUI.Label(new Rect(Screen.width * 0.5f - 180f, 70f, 360f, 54f), $"⏱ {Clock(timeLeft)}", timerStyle);
            timerStyle.normal.textColor = colour;

            var place = route.Places[next];
            var toGo = Mathf.Abs(place.Distance - route.Fold(car != null ? car.RoadDistance : 0f)) / 1000f;
            var best = from >= 0 ? PlayerPrefs.GetFloat($"RR_STAGE_{from}_{next}", 0f) : 0f;
            var line = $"NEXT  {place.Name.ToUpperInvariant()}  {toGo:0.0} km" + (best > 0f ? $"   •   BEST {Clock(best)}" : string.Empty);
            GUI.Label(new Rect(Screen.width * 0.5f - 300f, 122f, 600f, 28f), line, lineStyle);

            if (Time.unscaledTime < bannerUntil)
                GUI.Label(new Rect(Screen.width * 0.5f - 320f, Screen.height * 0.2f, 640f, 90f), banner, bannerStyle);
        }
    }
}
