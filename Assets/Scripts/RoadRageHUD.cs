using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    public sealed class RoadRageHUD : MonoBehaviour
    {
        private ArcadeCarController car;
        private RoadRageBootstrap world;
        private GUIStyle titleStyle;
        private GUIStyle readoutStyle;
        private GUIStyle buttonStyle;
        private GUIStyle pickerTitleStyle;
        private GUIStyle lockedStyle;
        private GUIStyle markerStyle;
        private GUIStyle markerSubStyle;
        private Texture2D arrowTex;
        private readonly List<TrafficCarController> markedViolators = new();
        private Texture2D dimTexture;
        private bool garageOpen;
        private bool missionsOpen;
        private bool wheelOpen;
        private int garageBrowse = -1;

        public static RoadRageHUD Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
        }

        private RoadRageBootstrap World => world != null ? world : RoadRageBootstrap.Instance;
        private ArcadeCarController Car => car != null ? car : (World != null && World.PlayerCar != null ? World.PlayerCar.GetComponent<ArcadeCarController>() : null);

        public void Initialize(ArcadeCarController controller, RoadRageBootstrap bootstrap)
        {
            car = controller;
            world = bootstrap;
        }

        /// Font sizes are authored against a 900px-tall screen. Phones in landscape are
        /// ~390-500px tall, where fixed sizes overflow their controls, so every style is
        /// rescaled whenever the screen size changes.
        private int styledForHeight;
        private float UiScale => Mathf.Clamp(Screen.height / 900f, 0.5f, 1.3f);

        private void ApplyUiScale()
        {
            if (styledForHeight == Screen.height) return;
            styledForHeight = Screen.height;
            var s = UiScale;
            titleStyle.fontSize = Mathf.RoundToInt(27 * s);
            readoutStyle.fontSize = Mathf.RoundToInt(18 * s);
            buttonStyle.fontSize = Mathf.RoundToInt(22 * s);
            pickerTitleStyle.fontSize = Mathf.RoundToInt(34 * s);
            lockedStyle.fontSize = Mathf.RoundToInt(17 * s);
        }

        private static Texture2D cardGlassTex;
        private static Texture2D statCardGlassTex;
        private static Texture2D pillBadgeTex;
        private static Texture2D goldBadgeTex;
        private static Texture2D topBarTex;
        private static Texture2D greenBtnTex;
        private static Texture2D blueBtnTex;
        private static Texture2D orangeBtnTex;
        private static Texture2D rowEvenTex;
        private static Texture2D rowOddTex;
        private static Texture2D rowHighlightTex;

        private static Font arcadeFont;
        private static Font titleFont;

        private static Texture2D CreateAntiAliasedBox(int w, int h, int r, Color fill, Color border, float borderWidth = 1.0f, float topSheen = 0.08f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Max(r - x, 0, x - (w - 1 - r));
                var dy = Mathf.Max(r - y, 0, y - (h - 1 - r));
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > r - borderWidth)
                {
                    var edgeAlpha = Mathf.Clamp01(r - dist);
                    tex.SetPixel(x, y, Color.Lerp(Color.clear, border, edgeAlpha));
                }
                else
                {
                    var col = fill;
                    if (y > h * 0.5f && topSheen > 0f)
                    {
                        var frac = (float)(y - h * 0.5f) / (h * 0.5f);
                        col += new Color(topSheen * frac, topSheen * frac, topSheen * frac, 0f);
                    }
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return tex;
        }

        /// Radial wheel face: N wedges in alternating colours with a gold rim, hub and
        /// spokes. Baked once into a texture so the spin animation is a single rotated
        /// blit per frame rather than per-frame geometry.
        ///
        /// Generated in texture space, where row 0 is the bottom - GUI.DrawTexture keeps
        /// that orientation - so atan2(dx, dy) is the angle measured clockwise from
        /// straight up on screen, which is where the pointer sits.
        private static Texture2D CreateWheelFace(int size, int wedges)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var radius = centre - 1f;
            var wedgeArc = Mathf.PI * 2f / Mathf.Max(1, wedges);
            var faceA = new Color(0.10f, 0.13f, 0.22f);
            var faceB = new Color(0.17f, 0.21f, 0.34f);
            var accent = new Color(0.72f, 0.18f, 0.14f);
            var rim = new Color(1f, 0.82f, 0.22f);
            var clear = new Color32(0, 0, 0, 0);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var index = y * size + x;
                    var dx = x - centre;
                    var dy = y - centre;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > radius)
                    {
                        pixels[index] = clear;
                        continue;
                    }

                    var angle = Mathf.Atan2(dx, dy);
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    var wedge = Mathf.Min(wedges - 1, (int)(angle / wedgeArc));
                    var colour = wedge % 2 == 1 ? accent : wedge % 4 == 0 ? faceA : faceB;

                    // Rim, hub and the dividing spokes, all in gold. The spoke test is in
                    // arc length, not radians, so the lines stay a constant width.
                    var edge = radius - dist;
                    var offset = angle - wedge * wedgeArc;
                    var spoke = Mathf.Min(offset, wedgeArc - offset) * dist;
                    if (edge < radius * 0.055f || dist < radius * 0.11f || spoke < 1.3f) colour = rim;

                    // One pixel of feathering at the outside so the disc is not jagged.
                    colour.a = Mathf.Clamp01(edge);
                    pixels[index] = colour;
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateBeveledButton(int w, int h, int r, Color topCol, Color botCol, Color border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Max(r - x, 0, x - (w - 1 - r));
                var dy = Mathf.Max(r - y, 0, y - (h - 1 - r));
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > r - 1.2f)
                {
                    tex.SetPixel(x, y, border);
                }
                else
                {
                    var vert = (float)y / h;
                    var col = Color.Lerp(botCol, topCol, vert);
                    if (y > h * 0.55f)
                    {
                        var sheen = Mathf.Lerp(0f, 0.18f, (y - h * 0.55f) / (h * 0.45f));
                        col += new Color(sheen, sheen, sheen, 0f);
                    }
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateHeaderTexture(int w, int h, Color fill, Color bottomBorder)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (y <= 2)
                    tex.SetPixel(x, y, bottomBorder);
                else
                {
                    var grad = Mathf.Lerp(1.05f, 0.95f, (float)y / h);
                    tex.SetPixel(x, y, fill * grad);
                }
            }
            tex.Apply();
            return tex;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) { ApplyUiScale(); return; }

            arcadeFont = Resources.Load<Font>("Fonts/RobotoCondensed-Bold");
            titleFont = Resources.Load<Font>("Fonts/Play-Bold");
            if (arcadeFont == null) arcadeFont = titleFont;
            if (titleFont == null) titleFont = arcadeFont;

            titleStyle = new GUIStyle(GUI.skin.label) { font = titleFont, fontSize = 27, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            readoutStyle = new GUIStyle(GUI.skin.label) { font = arcadeFont, fontSize = 18, fontStyle = FontStyle.Bold };
            readoutStyle.normal.textColor = new Color(0.62f, 1f, 0.72f);
            buttonStyle = new GUIStyle(GUI.skin.button) { font = titleFont, fontSize = 22, fontStyle = FontStyle.Bold };
            pickerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                font = titleFont, fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            pickerTitleStyle.normal.textColor = Color.white;
            lockedStyle = new GUIStyle(GUI.skin.button) { font = titleFont, fontSize = 17, fontStyle = FontStyle.Bold };
            lockedStyle.normal.textColor = new Color(0.55f, 0.55f, 0.60f);
            lockedStyle.hover.textColor = lockedStyle.normal.textColor;
            markerStyle = new GUIStyle(GUI.skin.label)
            {
                font = titleFont, fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            markerStyle.normal.textColor = Color.white;
            markerSubStyle = new GUIStyle(GUI.skin.label)
            {
                font = arcadeFont, fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            markerSubStyle.normal.textColor = Color.white;

            dimTexture = new Texture2D(1, 1);
            dimTexture.SetPixel(0, 0, new Color(0.02f, 0.025f, 0.045f, 0.93f));
            dimTexture.Apply();
            arrowTex = CreateArrowTexture(64);

            // High-Resolution Premium Assets
            cardGlassTex = CreateAntiAliasedBox(256, 256, 18, new Color(0.04f, 0.06f, 0.11f, 0.94f), new Color(0.25f, 0.65f, 1f, 0.80f), 1.2f, 0.06f);
            statCardGlassTex = CreateAntiAliasedBox(256, 256, 14, new Color(0.03f, 0.04f, 0.08f, 0.90f), new Color(0.35f, 0.55f, 0.85f, 0.45f), 1.0f, 0.04f);
            goldBadgeTex = CreateBeveledButton(256, 128, 16, new Color(0.32f, 0.22f, 0.06f, 0.95f), new Color(0.16f, 0.10f, 0.02f, 0.95f), new Color(1f, 0.82f, 0.25f, 0.85f));
            pillBadgeTex = CreateAntiAliasedBox(256, 128, 16, new Color(0.06f, 0.09f, 0.16f, 0.92f), new Color(0.3f, 0.55f, 0.9f, 0.50f), 1.0f, 0.05f);
            topBarTex = CreateHeaderTexture(128, 64, new Color(0.04f, 0.05f, 0.09f, 0.94f), new Color(0.2f, 0.60f, 1f, 0.45f));

            greenBtnTex = CreateBeveledButton(256, 128, 14, new Color(0.15f, 0.85f, 0.45f), new Color(0.05f, 0.55f, 0.25f), new Color(0.4f, 1f, 0.65f));
            blueBtnTex = CreateBeveledButton(256, 128, 14, new Color(0.15f, 0.65f, 1f), new Color(0.05f, 0.35f, 0.75f), new Color(0.45f, 0.85f, 1f));
            orangeBtnTex = CreateBeveledButton(256, 128, 14, new Color(1f, 0.65f, 0.15f), new Color(0.75f, 0.35f, 0.05f), new Color(1f, 0.85f, 0.4f));
            rowEvenTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.06f, 0.08f, 0.14f, 0.75f), Color.clear, 0f, 0.02f);
            rowOddTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.04f, 0.05f, 0.10f, 0.75f), Color.clear, 0f, 0.02f);
            rowHighlightTex = CreateAntiAliasedBox(256, 64, 8, new Color(0.18f, 0.14f, 0.04f, 0.90f), new Color(1f, 0.82f, 0.22f, 0.85f), 1.2f, 0.08f);

            ApplyUiScale();
        }

        private void Start()
        {
            // -ui=garage / -ui=missions opens a panel straight away so the screenshot
            // verification path can capture it without simulated input.
            var panel = RoadRageBootstrap.CommandLineValue("-ui=");
            if (panel == "garage") garageOpen = true;
            else if (panel == "missions") missionsOpen = true;
            else if (panel == "wheel") wheelOpen = true;
            else if (panel == "fury") furyOpen = true;
        }

        /// Store/press captures must not show the HUD, the mobile touch buttons or the
        /// development watermark - on a Steam page those read as "mobile port". Set by
        /// -cleanshot on the command line.
        public static bool HideForCapture;

        /// Rule-breakers must be findable at chase speed. Project every live violator
        /// to screen space and tag it with its offence; the nearest same-direction
        /// runner ahead is the quarry and gets the loud marker. Innocents stay
        /// unmarked - the contrast is what teaches "hit the tagged car, spare the rest".
        private void DrawQuarryMarkers()
        {
            var cam = Camera.main;
            if (cam == null) return;

            markedViolators.Clear();
            TrafficCarController quarry = null;
            var quarryGap = float.MaxValue;
            foreach (var traffic in TrafficCarController.All)
            {
                if (traffic == null || !traffic.IsViolator) continue;
                var gap = traffic.GapToPlayer;
                if (gap < -25f || gap > 240f) continue;
                markedViolators.Add(traffic);
                if (traffic.Direction > 0f && gap >= 0f && gap < quarryGap)
                {
                    quarryGap = gap;
                    quarry = traffic;
                }
            }
            if (markedViolators.Count == 0) return;

            const float width = 46f, height = 52f;
            float lastY = -9999f, lastX = -9999f;
            for (var i = 0; i < markedViolators.Count; i++)
            {
                var traffic = markedViolators[i];
                var isQuarry = traffic == quarry;
                if (!isQuarry && traffic.GapToPlayer > 160f) continue;

                var vp = cam.WorldToViewportPoint(traffic.transform.position + Vector3.up * 2.6f);
                var onScreen = vp.z > 0f && vp.x >= -0.06f && vp.x <= 1.06f && vp.y >= -0.02f && vp.y <= 1.04f;

                // One red exclamation mark per rule-breaker - no offence text. The
                // player reads red = hunt, and nothing else.
                const string label = "!";
                var tint = new Color(0.95f, 0.15f, 0.12f);

                // Curves and takedown camera swings throw the quarry out of frame; the
                // edge arrow keeps the chase target locatable at all times.
                if (!onScreen)
                {
                    if (isQuarry) DrawQuarryEdgeArrow(vp, tint, quarryGap);
                    continue;
                }

                var x = vp.x * Screen.width - width * 0.5f;
                var y = (1f - vp.y) * Screen.height - height;
                // Clustered traffic stacked their chips into one unreadable smudge;
                // nudge later chips upward when they land on the previous one.
                if (Mathf.Abs(y - lastY) < 30f && Mathf.Abs(x - lastX) < width + 24f)
                    y = lastY - (height + 8f);
                lastY = y;
                lastX = x;

                var pulse = traffic.IsFleeing && isQuarry
                    ? 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f))
                    : 1f;

                var prev = GUI.color;
                // The mark alone. It used to sit on a black panel at 42-66% alpha with a
                // distance readout under it, which is three pieces of chrome doing the job
                // of one - and the panel is the part you actually see first, so the read
                // was "dark box" before "red !".
                //
                // The panel was there for contrast against a bright sky, so that job moves
                // to an outline: the glyph is drawn four times in black at one-pixel
                // offsets and once in red on top. Costs four extra GUI.Labels on at most a
                // handful of markers and needs no texture at all.
                var markerRect = new Rect(x, y, width, height);
                GUI.color = new Color(0f, 0f, 0f, (isQuarry ? 0.9f : 0.7f) * pulse);
                for (var ox = -1; ox <= 1; ox += 2)
                for (var oy = -1; oy <= 1; oy += 2)
                    GUI.Label(new Rect(x + ox * 1.5f, y + oy * 1.5f, width, height), label, markerStyle);
                GUI.color = new Color(tint.r, tint.g, tint.b, (isQuarry ? 1f : 0.85f) * pulse);
                GUI.Label(markerRect, label, markerStyle);
                GUI.color = prev;
            }
        }

        /// Solid right-pointing chevron for the off-screen quarry arrow. Rasterised
        /// once via an even-odd point-in-polygon test - IMGUI has no polygon primitive.
        private static Texture2D CreateArrowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var poly = new[]
            {
                new Vector2(0.14f, 0.08f), new Vector2(0.62f, 0.50f),
                new Vector2(0.14f, 0.92f), new Vector2(0.30f, 0.50f),
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, 1f - (y + 0.5f) / size);
                    var inside = false;
                    for (int a = 0, b = poly.Length - 1; a < poly.Length; b = a++)
                    {
                        if (((poly[a].y > p.y) != (poly[b].y > p.y)) &&
                            p.x < (poly[b].x - poly[a].x) * (p.y - poly[a].y) / (poly[b].y - poly[a].y) + poly[a].x)
                            inside = !inside;
                    }
                    pixels[y * size + x] = inside ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// The quarry can leave the frame on curves or mid-takedown camera swings;
        /// clamp a rotated chevron to the screen edge so the chase never loses its
        /// target, with the closing gap written under it.
        private void DrawQuarryEdgeArrow(Vector3 vp, Color tint, float gap)
        {
            if (arrowTex == null) return;
            var dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (vp.z <= 0f) dir = -dir;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(1f, 0f);
            dir.Normalize();

            var mx = Mathf.Min(96f, Screen.width * 0.12f);
            var my = Mathf.Min(120f, Screen.height * 0.16f);
            var tx = Mathf.Abs(dir.x) > 0.0001f ? (Screen.width * 0.5f - mx) / Mathf.Abs(dir.x) : float.MaxValue;
            var ty = Mathf.Abs(dir.y) > 0.0001f ? (Screen.height * 0.5f - my) / Mathf.Abs(dir.y) : float.MaxValue;
            var px = Screen.width * 0.5f + dir.x * Mathf.Min(tx, ty);
            var py = Screen.height * 0.5f - dir.y * Mathf.Min(tx, ty);

            const float size = 46f;
            var angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(px, py));
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.95f);
            GUI.DrawTexture(new Rect(px - size * 0.5f, py - size * 0.5f, size, size), arrowTex,
                ScaleMode.ScaleToFit, true);
            GUI.matrix = old;

            var chipRect = new Rect(px - 60f, py + size * 0.45f, 120f, 22f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(chipRect, dimTexture);
            GUI.color = Color.white;
            GUI.Label(chipRect, $"{gap:0} m", markerSubStyle);
        }

        private void OnGUI()
        {
            if (HideForCapture) return;
            EnsureStyles();
            var w = World;
            if (w == null) return;
            if (w.PickerOpen)
            {
                DrawPicker();
                return;
            }
            if (garageOpen)
            {
                DrawGarage();
                return;
            }
            if (missionsOpen)
            {
                DrawMissions();
                return;
            }
            if (wheelOpen)
            {
                DrawWheelScreen();
                return;
            }
            if (furyOpen)
            {
                DrawFuryPass();
                return;
            }
            if (RoadRageLeaderboardDirector.Instance != null && RoadRageLeaderboardDirector.Instance.IsLeaderboardOpen)
            {
                DrawLeaderboardModal();
                return;
            }
            if (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive)
            {
                DrawLandingScreen();
                return;
            }
            var c = Car;
            if (c == null) return;

            DrawQuarryMarkers();

            var menuRect = new Rect(Screen.width - 240f, 20f, 100f, 44f);
            if (GUI.Button(menuRect, "🏠 MENU", buttonStyle))
            {
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.ReturnToLanding();
                return;
            }

            var garageRect = new Rect(Screen.width * 0.5f - 250f, 24f, 150f, 44f);
            if (GUI.Button(garageRect, "GARAGE", buttonStyle))
            {
                garageOpen = true;
                return;
            }

            var missionsRect = new Rect(Screen.width * 0.5f + 100f, 24f, 150f, 44f);
            if (GUI.Button(missionsRect, "MISSIONS", buttonStyle))
            {
                missionsOpen = true;
                return;
            }

            GUI.Label(new Rect(28f, 22f, 520f, 44f), "ROAD RAGE  /  UNITY REMAKE", titleStyle);
            GUI.Label(new Rect(30f, 62f, 620f, 32f),
                $"{w.BiomeNameAt(c.RoadDistance)}  |  {WeatherSystem.Label(w.Weather)}  |  {c.SpeedKph:0} km/h  |  {c.DistanceKm:0.00} km",
                readoutStyle);
            // Integrity bar - the run's clock. Without a visible failure state the player
            // has no reason to judge targets rather than ram everything.
            var barW = 220f * UiScale;
            // Text rows above use unscaled Y (62, 96), so the bar must too or it overlaps.
            var barRect = new Rect(30f, 134f, barW, 13f);
            GUI.DrawTexture(barRect, dimTexture);
            var frac = Mathf.Clamp01(GameState.Integrity / GameState.MaxIntegrity);
            var barColor = frac > 0.5f ? new Color(0.35f, 0.95f, 0.5f)
                : frac > 0.25f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
            var prev = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * frac, barRect.height),
                Texture2D.whiteTexture);
            GUI.color = prev;

            // Nitro Boost Meter Bar
            if (RoadRageBoostDirector.Instance != null)
            {
                var boostRect = new Rect(30f, 150f, barW, 11f);
                GUI.DrawTexture(boostRect, dimTexture);
                var bFrac = Mathf.Clamp01(RoadRageBoostDirector.Instance.BoostAmount / RoadRageBoostDirector.MaxBoost);
                var isFull = RoadRageBoostDirector.Instance.IsFullBoost;
                var isBurning = RoadRageBoostDirector.Instance.IsBoosting;
                var boostColor = isFull ? new Color(1f, 0.85f, 0.2f) : isBurning ? new Color(1f, 0.45f, 0.1f) : new Color(0.15f, 0.8f, 1f);
                var prevC = GUI.color;
                GUI.color = boostColor;
                GUI.DrawTexture(new Rect(boostRect.x, boostRect.y, boostRect.width * bFrac, boostRect.height), Texture2D.whiteTexture);
                GUI.color = prevC;

                var chain = RoadRageBoostDirector.Instance.BurnoutChain;
                var boostLabel = chain > 0 ? $"🔥 BURNOUT x{chain}" : (isFull ? "★ NITRO READY" : (isBurning ? "🔥 BOOSTING" : "NITRO"));
                GUI.Label(new Rect(35f + barW, 146f, 180f, 20f), boostLabel, readoutStyle);
            }

            GUI.Label(new Rect(30f, 96f, 520f, 32f),
                $"SCORE {GameState.Score:N0}   ${GameState.Cash:N0}   {GameState.Takedowns} TAKEDOWNS", readoutStyle);
            if (GameState.Combo > 0)
                GUI.Label(new Rect(30f, 130f, 520f, 34f), $"x{GameState.ComboMultiplier} COMBO", titleStyle);
            if (c.CountdownTimer > 0f)
            {
                var digit = Mathf.CeilToInt(c.CountdownTimer);
                var countdownColor = digit switch
                {
                    3 => new Color(1f, 0.85f, 0.2f), // Gold
                    2 => new Color(1f, 0.55f, 0.1f), // Orange
                    _ => new Color(1f, 0.25f, 0.2f)  // Coral Red
                };
                var prevC = pickerTitleStyle.normal.textColor;
                pickerTitleStyle.normal.textColor = countdownColor;
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.26f, 300f, 90f),
                    $"{digit}", pickerTitleStyle);
                pickerTitleStyle.normal.textColor = prevC;
            }
            else if (c.CountdownTimer > -0.9f)
            {
                // Countdown reaches 0 and rolls straight into gameplay; no "GO!" banner.
            }
            else if (!string.IsNullOrEmpty(GameState.Message))
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.32f, 400f, 40f),
                    GameState.Message, titleStyle);
            }
            else if (c.SpeedKph < 4f && c.DistanceKm < 0.05f)
            {
                var pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f);
                var prevC = titleStyle.normal.textColor;
                titleStyle.normal.textColor = new Color(0.2f, 0.95f, 0.4f, pulse);
                GUI.Label(new Rect(Screen.width * 0.5f - 280f, Screen.height * 0.32f, 560f, 44f),
                    "🚦 READY! PRESS [GAS] / [W] TO DRIVE", titleStyle);
                titleStyle.normal.textColor = prevC;
            }

            if (RoadRagePolicePursuitDirector.Instance != null && RoadRagePolicePursuitDirector.Instance.IsPursuitActive)
            {
                var heat = RoadRagePolicePursuitDirector.Instance.HeatLevel;
                var stars = new string('★', heat) + new string('☆', 5 - heat);
                var director = RoadRagePolicePursuitDirector.Instance;
                var heatText = $"🚨 WANTED: {stars}   ${Mathf.RoundToInt(director.PursuitBounty)}";
                var flash = Mathf.Sin(Time.unscaledTime * 8f) > 0f;
                var prevColor = titleStyle.normal.textColor;
                titleStyle.normal.textColor = flash ? new Color(1f, 0.25f, 0.2f) : new Color(0.2f, 0.65f, 1f);
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, 76f, 400f, 36f), heatText, titleStyle);

                // Which way the pursuit is going. Both endings used to arrive with no
                // warning, so a chase read as noise rather than as something the player
                // was winning or losing.
                var bust = director.BustProgress;
                var evade = director.EvadeProgress;
                if (bust > 0.01f || evade > 0.01f)
                {
                    var losing = bust > evade;
                    titleStyle.normal.textColor = losing ? new Color(1f, 0.35f, 0.25f) : new Color(0.45f, 1f, 0.55f);
                    var pct = Mathf.RoundToInt((losing ? bust : evade) * 100f);
                    GUI.Label(new Rect(Screen.width * 0.5f - 200f, 110f, 400f, 32f),
                        losing ? $"BUSTED IN... {100 - pct}%" : $"LOSING THEM... {pct}%", titleStyle);
                }
                titleStyle.normal.textColor = prevColor;
            }
            GUI.Label(new Rect(Screen.width - 270f, 24f, 130f, 32f), $"{Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))} FPS", readoutStyle);
            
            var pauseRect = new Rect(Screen.width - 130f, 20f, 110f, 44f);
            if (GUI.Button(pauseRect, w.PickerOpen ? "RESUME" : "PAUSE", buttonStyle))
            {
                if (w.PickerOpen) w.ClosePicker();
                else w.OpenPicker();
                return;
            }

            var biomeRect = new Rect(Screen.width * 0.5f - 92f, 22f, 184f, 48f);
            if (GUI.Button(biomeRect, "BIOMES", buttonStyle))
            {
                w.OpenPicker();
                return;
            }

            if (GameState.IsAftertouchActive)
            {
                GUI.Label(new Rect(0f, 22f, Screen.width, 44f), "💥 IMPACT TIME — AFTERTOUCH 💥", pickerTitleStyle);
                GUI.Label(new Rect(0f, 66f, Screen.width, 32f),
                    $"STEER YOUR WRECK INTO TRAFFIC!   PILEUP: ${GameState.PileupDamage:N0}   TAKEDOWNS: {GameState.AftertouchTakedowns}", readoutStyle);

                if (GameState.CrashbreakerReady)
                {
                    var cbWidth = Mathf.Min(340f, Screen.width * 0.6f);
                    var cbRect = new Rect(Screen.width * 0.5f - cbWidth * 0.5f, Screen.height * 0.72f, cbWidth, 60f);
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.28f, 0.08f, 0.95f);
                    if (GUI.Button(cbRect, "💥 CRASHBREAKER (SPACE / TAP)", buttonStyle))
                    {
                        if (RoadRageAftertouchDirector.Instance != null)
                            RoadRageAftertouchDirector.Instance.DetonateCrashbreaker();
                    }
                    GUI.backgroundColor = prevBg;
                }

                var atSize = Mathf.Clamp(Screen.height * 0.14f, 84f, 136f);
                var atBottom = Screen.height - atSize - 24f;
                var atLeft = GUI.RepeatButton(new Rect(24f, atBottom, atSize, atSize), "◄ STEER", buttonStyle);
                var atRight = GUI.RepeatButton(new Rect(36f + atSize, atBottom, atSize, atSize), "STEER ►", buttonStyle);
                if (RoadRageAftertouchDirector.Instance != null)
                {
                    RoadRageAftertouchDirector.Instance.TouchAftertouchSteer = atLeft ? -1f : atRight ? 1f : 0f;
                }
                return;
            }

            if (GameState.RunOver)
            {
                // The revive offer stands ahead of the crash report; DrawRevivePrompt
                // returns false the frame its clock expires, so the report takes over
                // in that same frame rather than leaving a blank one.
                if (!ShouldOfferRevive() || !DrawRevivePrompt())
                {
                    // Revive is off the table now, so the run is truly finished: bank the
                    // payout once (CommitRun is idempotent) before the results screen reads
                    // LastRunCash / LastRunFury.
                    GameState.CommitRun();
                    DrawRunOverScreen();
                }
                return;
            }

            var size = Mathf.Clamp(Screen.height * 0.13f, 76f, 126f);
            var bottom = Screen.height - size - 24f;
            var left = GUI.RepeatButton(new Rect(24f, bottom, size, size), "LEFT", buttonStyle);
            var right = GUI.RepeatButton(new Rect(36f + size, bottom, size, size), "RIGHT", buttonStyle);
            var nitro = GUI.RepeatButton(new Rect(Screen.width - size * 3.5f - 52f, bottom, size * 1.15f, size), "NITRO", buttonStyle);
            var brake = GUI.RepeatButton(new Rect(Screen.width - size * 2.25f - 38f, bottom, size, size), "BRAKE", buttonStyle);
            var gas = GUI.RepeatButton(new Rect(Screen.width - size - 24f, bottom, size, size), "GAS", buttonStyle);
            c.TouchSteer = left ? -1f : right ? 1f : 0f;
            c.TouchThrottle = gas ? 1f : brake ? -1f : 0f;
            if (RoadRageBoostDirector.Instance != null)
            {
                RoadRageBoostDirector.Instance.TouchNitroPressed = nitro;
            }
        }

        /// Garage: browse the catalogue, buy/select a car, and spend cash on the three
        /// upgrade tracks. Selecting a different car rebuilds the world so the new mesh,
        /// livery and handling stats take effect immediately.
        /// Horizontal 0-1 stat bar. Numbers alone do not communicate that the Juggernaut
        /// has twice the armour of the starter ute.
        private void StatBar(Rect rect, string label, float value, float max, Color fill)
        {
            GUI.Label(new Rect(rect.x, rect.y - 2f, 92f, rect.height), label, readoutStyle);
            var track = new Rect(rect.x + 96f, rect.y + 5f, rect.width - 150f, rect.height - 12f);
            GUI.DrawTexture(track, dimTexture);
            var t = Mathf.Clamp01(value / max);
            var old = GUI.color;
            GUI.color = fill;
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(track.xMax + 8f, rect.y - 2f, 60f, rect.height), $"{value:0.00}", readoutStyle);
        }

        private void DrawGarage()
        {
            if (garageBrowse < 0) garageBrowse = GameState.SelectedCar;
            var cars = GameState.Cars;
            garageBrowse = Mathf.Clamp(garageBrowse, 0, cars.Length - 1);
            var spec = cars[garageBrowse];
            var owned = GameState.OwnedCars.Contains(garageBrowse);
            var selected = GameState.SelectedCar == garageBrowse;

            // Live 3D vehicle behind the panel, slowly turning.
            world.EnsureShowroom(garageBrowse);
            world.SetShowroomActive(true, Time.unscaledTime * 16f);
            if (world.ShowroomCamera != null)
                world.ShowroomCamera.rect = new Rect(0f, 0f, 1f, 1f);

            var w = Screen.width;
            var h = Screen.height;
            var panelW = Mathf.Max(300f, w * 0.30f);
            GUI.DrawTexture(new Rect(0f, 0f, panelW, h), dimTexture);
            GUI.DrawTexture(new Rect(0f, 0f, w, h * 0.13f), dimTexture);
            GUI.DrawTexture(new Rect(0f, h * 0.88f, w, h * 0.12f), dimTexture);

            GUI.Label(new Rect(0f, h * 0.035f, w, 60f), "GARAGE", pickerTitleStyle);
            GUI.Label(new Rect(0f, h * 0.09f, w, 30f),
                $"${GameState.Cash:N0}   💎 {GameState.Gems:N0}   -   {GameState.NextCarGoal()}", readoutStyle);

            // ---- Left panel: identity, price, stats, description ----
            var pad = 22f;
            var y = h * 0.17f;
            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 40f), spec.Name, titleStyle);
            y += 40f;

            var status = selected ? "IN USE" : owned ? "OWNED" : $"${spec.Price:N0}";
            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 30f),
                $"{garageBrowse + 1} / {cars.Length}     {status}", readoutStyle);
            y += 42f;

            var barH = Mathf.Max(26f, 30f * UiScale);
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "SPEED", spec.Speed, 1.5f,
                new Color(0.35f, 0.85f, 1f, 0.95f)); y += barH + 6f;
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "ACCEL", spec.Acceleration, 1.5f,
                new Color(0.55f, 1f, 0.55f, 0.95f)); y += barH + 6f;
            StatBar(new Rect(pad, y, panelW - pad * 2f, barH), "ARMOUR", spec.Armour, 2.5f,
                new Color(1f, 0.68f, 0.32f, 0.95f)); y += barH + 14f;

            GUI.Label(new Rect(pad, y, panelW - pad * 2f, 110f), spec.Description, readoutStyle);

            // ---- Upgrades, applied to whichever vehicle you drive ----
            var upgrades = new[] { ("engine", "ENGINE"), ("armor", "ARMOUR"), ("boost", "BOOST") };
            var upY = h * 0.60f;
            var upH = Mathf.Max(40f, 52f * UiScale);
            for (var i = 0; i < upgrades.Length; i++)
            {
                var (key, title) = upgrades[i];
                var level = GameState.UpgradeLevel(key);
                var maxed = level >= GameState.UpgradeMax;
                var cost = GameState.UpgradeCost(level);
                var rect = new Rect(pad, upY + i * (upH + 8f), panelW - pad * 2f, upH);
                var label = maxed
                    ? $"{title}   MAX ({level})"
                    : $"{title}   {level}/{GameState.UpgradeMax}   ${cost:N0}";
                if (GUI.Button(rect, label, maxed || GameState.Cash < cost ? lockedStyle : buttonStyle) && !maxed)
                    GameState.BuyUpgrade(key);
            }

            // ---- Right side panel: Performance Tuning ----
            var rightPanelW = Mathf.Max(240f, w * 0.24f);
            var rightX = w - rightPanelW;
            GUI.DrawTexture(new Rect(rightX, 0f, rightPanelW, h), dimTexture);
            var tuneY = h * 0.16f;
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 32f), "PERFORMANCE TUNING", titleStyle);
            tuneY += 38f;

            // 1. Forced Induction Tuning
            var inductLabel = GameState.TuningInduction == 0 ? "SUPERCHARGER" : "TURBOCHARGER";
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"INDUCTION: {inductLabel}", buttonStyle))
            {
                GameState.TuningInduction = (GameState.TuningInduction + 1) % 2;
                GameState.Save();
            }
            tuneY += 46f;
            var inductDesc = GameState.TuningInduction == 0 ? "⚡ +22% Launch Acceleration" : "🔥 +22 km/h Top Speed";
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), inductDesc, readoutStyle);
            tuneY += 30f;

            // 2. Tire Compound Tuning
            var tireLabel = GameState.TuningTires == 0 ? "RACING GRIP" : "DRIFT COMPOUND";
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"TIRES: {tireLabel}", buttonStyle))
            {
                GameState.TuningTires = (GameState.TuningTires + 1) % 2;
                GameState.Save();
            }
            tuneY += 46f;
            var tireDesc = GameState.TuningTires == 0 ? "🎯 Laser-Sharp Lane Control" : "💨 Power-Slide Drift Boost";
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), tireDesc, readoutStyle);
            tuneY += 30f;

            // 3. Ramming Bar Tuning
            var ramName = GameState.TuningRamBar switch
            {
                2 => "TITANIUM RAM",
                1 => "STEEL PUSH-BAR",
                _ => "STOCK BUMPER"
            };
            if (GUI.Button(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 42f), $"RAM: {ramName}", buttonStyle))
            {
                GameState.TuningRamBar = (GameState.TuningRamBar + 1) % 3;
                GameState.Save();
            }
            tuneY += 46f;
            var ramDesc = GameState.TuningRamBar switch
            {
                2 => "💥 +65% Ram Power / Heavy Armor",
                1 => "🛡️ +30% Takedown Power",
                _ => "Standard Bumper"
            };
            GUI.Label(new Rect(rightX + 16f, tuneY, rightPanelW - 32f, 24f), ramDesc, readoutStyle);

            // ---- Bottom bar: browse, buy/select, back ----
            var barY = h * 0.90f;
            var btnH = Mathf.Max(42f, 52f * UiScale);
            var navW = Mathf.Max(104f, 124f * UiScale);

            if (GUI.Button(new Rect(pad, barY, navW, btnH), "< PREV", buttonStyle))
                garageBrowse = (garageBrowse - 1 + cars.Length) % cars.Length;
            if (GUI.Button(new Rect(pad + navW + 8f, barY, navW, btnH), "NEXT >", buttonStyle))
                garageBrowse = (garageBrowse + 1) % cars.Length;

            var actionW = Mathf.Min(260f, w * 0.22f);
            var actionRect = new Rect(w * 0.5f - actionW * 0.5f, barY, actionW, btnH);
            if (selected)
            {
                GUI.Button(actionRect, "SELECTED", lockedStyle);
            }
            else if (owned)
            {
                if (GUI.Button(actionRect, "DRIVE THIS", buttonStyle))
                {
                    GameState.SelectedCar = garageBrowse;
                    GameState.Save();
                    world.ReloadBiome(world.BiomeName);
                }
            }
            else
            {
                var afford = GameState.Cash >= spec.Price;
                if (GUI.Button(actionRect, $"BUY  ${spec.Price:N0}", afford ? buttonStyle : lockedStyle)
                    && afford && GameState.BuyCar(garageBrowse))
                {
                    GameState.SelectedCar = garageBrowse;
                    GameState.Save();
                    world.ReloadBiome(world.BiomeName);
                }
            }

            var backW = Mathf.Min(160f, w * 0.14f);
            if (GUI.Button(new Rect(w - backW - pad, barY, backW, btnH), "BACK", buttonStyle))
                CloseGarage();
        }

        private void CloseGarage()
        {
            garageOpen = false;
            garageBrowse = -1;
            world.SetShowroomActive(false);
        }

        /// Daily missions: three rolled per calendar day, with a login streak bonus.
        private void DrawMissions()
        {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTexture);
            GUI.Label(new Rect(0f, Screen.height * 0.08f, Screen.width, 60f), "DAILY MISSIONS", pickerTitleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 32f),
                $"DAY {GameState.LoginStreak} STREAK   •   TODAY'S BONUS +${GameState.LastLoginReward:N0}   •   " +
                $"${GameState.Cash:N0}   •   💎 {GameState.Gems:N0}   •   CLEAR ALL 3 FOR 💎{GameState.GemsAllDailies}",
                readoutStyle);

            var rowY = Screen.height * 0.26f;
            for (var slot = 0; slot < GameState.MissionIds.Count; slot++)
            {
                var spec = GameState.MissionPool[GameState.MissionIds[slot]];
                var progress = GameState.MissionProgress(slot);
                var done = GameState.MissionDone(slot);
                var claimed = slot < GameState.MissionClaimed.Count && GameState.MissionClaimed[slot];
                var goalText = string.Format(spec.Description, spec.Goal % 1f == 0f ? $"{spec.Goal:0}" : $"{spec.Goal:0.#}");

                GUI.Label(new Rect(Screen.width * 0.5f - 380f, rowY + slot * 92f, 520f, 32f),
                    $"{goalText}   —   {Mathf.Min(progress, spec.Goal):0.#}/{spec.Goal:0.#}", readoutStyle);

                var rect = new Rect(Screen.width * 0.5f + 170f, rowY + slot * 92f - 8f, 210f, 54f);
                var label = claimed ? "CLAIMED" : done ? $"CLAIM  ${spec.Reward:N0}" : $"${spec.Reward:N0}";
                if (GUI.Button(rect, label, done && !claimed ? buttonStyle : lockedStyle) && done && !claimed)
                    GameState.ClaimMission(slot);
            }

            if (GUI.Button(new Rect(Screen.width * 0.5f - 300f, rowY + 300f, 200f, 52f),
                    GameState.WheelSpins > 0 ? $"🎡 LUCKY WHEEL ({GameState.WheelSpins})" : "🎡 LUCKY WHEEL", buttonStyle))
            {
                missionsOpen = false;
                wheelOpen = true;
            }

            if (GUI.Button(new Rect(Screen.width * 0.5f + 110f, rowY + 300f, 200f, 52f),
                    GameState.FuryUnclaimedCount() > 0
                        ? $"🔥 FURY PASS ({GameState.FuryUnclaimedCount()})"
                        : $"🔥 FURY PASS  T{GameState.FuryTier}", buttonStyle))
            {
                missionsOpen = false;
                furyOpen = true;
            }

            if (GUI.Button(new Rect(Screen.width * 0.5f - 90f, rowY + 300f, 180f, 52f), "BACK", buttonStyle))
                missionsOpen = false;
        }

        // ---- lucky wheel presentation state ----
        private float wheelAngle;
        private float wheelSpinFrom;
        private float wheelSpinTo;
        private float wheelSpinStart;
        private float wheelSpinDuration;
        private int wheelResult = -1;
        private bool wheelSpinning;
        private string wheelMessage = string.Empty;
        private static Texture2D wheelFaceTex;

        /// Decides the outcome up front and then animates to it. GameState.SpinWheel has
        /// already banked the prize by the time this returns, so a player who closes the
        /// panel while the wheel is still turning keeps what they won.
        private void BeginWheelSpin()
        {
            var index = GameState.SpinWheel();
            if (index < 0) return;

            wheelResult = index;
            wheelMessage = string.Empty;
            wheelSpinning = true;
            wheelSpinStart = Time.unscaledTime;
            wheelSpinDuration = 3.1f;
            wheelSpinFrom = Mathf.Repeat(wheelAngle, 360f);
            wheelAngle = wheelSpinFrom;

            // Five full turns, then stop with this wedge's centre under the pointer.
            var wedgeDeg = 360f / GameState.WheelPrizes.Length;
            var landing = Mathf.Repeat(-(index + 0.5f) * wedgeDeg, 360f);
            wheelSpinTo = wheelSpinFrom + 360f * 5f + Mathf.Repeat(landing - wheelSpinFrom, 360f);
        }

        private void DrawWheelScreen()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            var modalW = Mathf.Clamp(usableW * 0.88f, 480f, 880f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 340f, 580f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;
            GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH),
                cardGlassTex != null ? cardGlassTex : dimTexture);

            var headStyle = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(24 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.82f, 0.2f) }
            };
            GUI.Label(new Rect(modalX, modalY + 6f, modalW, 32f * s), "🎡  L U C K Y   W H E E L  🎡", headStyle);

            var subStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.88f, 1f) }
            };
            GUI.Label(new Rect(modalX, modalY + 34f * s, modalW, 20f * s),
                $"SPINS: {GameState.WheelSpins}   •   x2 CHARGES: {GameState.DoubleCharges}   •   " +
                $"${GameState.Cash:N0}   •   💎 {GameState.Gems:N0}",
                subStyle);

            // Advance the animation. Cubic ease-out, so it decelerates into the wedge.
            if (wheelSpinning)
            {
                var t = wheelSpinDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01((Time.unscaledTime - wheelSpinStart) / wheelSpinDuration);
                wheelAngle = Mathf.Lerp(wheelSpinFrom, wheelSpinTo, 1f - Mathf.Pow(1f - t, 3f));
                if (t >= 1f)
                {
                    wheelSpinning = false;
                    wheelAngle = wheelSpinTo;
                    wheelMessage = GameState.WheelPrizeName(wheelResult);
                }
            }

            var btnH = Mathf.Clamp(modalH * 0.13f, 40f, 54f);
            var btnY = modalY + modalH - btnH - 14f;

            // ---- the wheel itself, left of centre ----
            var faceTop = modalY + 58f * s;
            var faceBox = Mathf.Max(150f, Mathf.Min(modalW * 0.50f, btnY - 34f * s - faceTop));
            var faceCentre = new Vector2(modalX + modalW * 0.29f, faceTop + faceBox * 0.5f);
            var faceRect = new Rect(faceCentre.x - faceBox * 0.5f, faceCentre.y - faceBox * 0.5f, faceBox, faceBox);

            if (wheelFaceTex == null) wheelFaceTex = CreateWheelFace(512, GameState.WheelPrizes.Length);

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(wheelAngle, faceCentre);
            GUI.DrawTexture(faceRect, wheelFaceTex);
            GUI.matrix = matrix;

            // Wedge labels ride the rotation but stay upright, which keeps them legible
            // at the sizes this panel runs at on a phone.
            var wedgeDeg = 360f / GameState.WheelPrizes.Length;
            var wedgeStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(10 * s),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = Color.white }
            };
            for (var i = 0; i < GameState.WheelPrizes.Length; i++)
            {
                var theta = ((i + 0.5f) * wedgeDeg + wheelAngle) * Mathf.Deg2Rad;
                var r = faceBox * 0.33f;
                var px = faceCentre.x + Mathf.Sin(theta) * r;
                var py = faceCentre.y - Mathf.Cos(theta) * r;
                GUI.Label(new Rect(px - faceBox * 0.24f, py - 9f * s, faceBox * 0.48f, 18f * s),
                    GameState.WheelPrizes[i].Label, wedgeStyle);
            }

            var pointerStyle = new GUIStyle(titleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(26 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.35f, 0.2f) }
            };
            GUI.Label(new Rect(faceCentre.x - 24f, faceRect.y - 15f * s, 48f, 28f * s), "▼", pointerStyle);

            // ---- odds table, right of the wheel ----
            var colX = modalX + modalW * 0.60f;
            var colW = modalW * 0.36f;
            var rowH = Mathf.Clamp(faceBox * 0.085f, 14f, 24f);
            var listY = faceCentre.y - (GameState.WheelPrizes.Length * rowH) * 0.5f;
            var oddsStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(11 * s),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.84f, 0.90f, 1f) }
            };
            var wonStyle = new GUIStyle(oddsStyle) { normal = { textColor = new Color(0.4f, 1f, 0.6f) } };
            var oddsHeadStyle = new GUIStyle(oddsStyle) { normal = { textColor = new Color(1f, 0.82f, 0.25f) } };

            // Published odds. A wheel that hides them is the kind players stop trusting.
            GUI.Label(new Rect(colX, listY - rowH - 2f, colW, rowH), "ON THE WHEEL", oddsHeadStyle);
            var weightTotal = Mathf.Max(1, GameState.WheelWeightTotal);
            for (var i = 0; i < GameState.WheelPrizes.Length; i++)
            {
                var prize = GameState.WheelPrizes[i];
                var odds = prize.Weight * 100f / weightTotal;
                GUI.Label(new Rect(colX, listY + i * rowH, colW, rowH),
                    $"{prize.Label}  —  {odds:0.#}%",
                    i == wheelResult && !wheelSpinning ? wonStyle : oddsStyle);
            }

            if (!wheelSpinning && wheelMessage.Length > 0)
            {
                var winStyle = new GUIStyle(titleStyle)
                {
                    font = titleFont,
                    fontSize = Mathf.RoundToInt(18 * s),
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.4f, 1f, 0.6f) }
                };
                GUI.Label(new Rect(modalX, btnY - 30f * s, modalW, 28f * s), $"YOU WON:  {wheelMessage}", winStyle);
            }

            // ---- action row ----
            var btnSpacing = 8f * s;
            var btnW = (modalW - 40f - btnSpacing * 3f) / 4f;
            var actionStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) };

            var canSpin = !wheelSpinning && GameState.WheelSpins > 0;
            var spinLabel = wheelSpinning ? "SPINNING…" : canSpin ? $"🎡 SPIN  ({GameState.WheelSpins})" : "NO SPINS LEFT";
            if (canSpin && greenBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, btnY, btnW, btnH), greenBtnTex);
            if (GUI.Button(new Rect(modalX + 20f, btnY, btnW, btnH), spinLabel, canSpin ? actionStyle : lockedStyle) && canSpin)
                BeginWheelSpin();

            var soldOut = GameState.WheelPaidToday >= GameState.WheelPaidMaxPerDay;
            var canBuy = !wheelSpinning && GameState.CanBuySpin;
            var buyLabel = soldOut ? "SOLD OUT TODAY" : $"BUY SPIN  ${GameState.WheelSpinCost:N0}";
            if (GUI.Button(new Rect(modalX + 20f + btnW + btnSpacing, btnY, btnW, btnH), buyLabel,
                    canBuy ? actionStyle : lockedStyle) && canBuy)
                GameState.BuySpin();

            // Gems buy a spin with no daily cap - the cap belongs to the cash ladder.
            var canGemSpin = !wheelSpinning && GameState.Gems >= GameState.GemSpinPrice;
            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 2f, btnY, btnW, btnH),
                    $"💎 {GameState.GemSpinPrice} SPIN", canGemSpin ? actionStyle : lockedStyle) && canGemSpin)
                GameState.BuySpinWithGems();

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 3f, btnY, btnW, btnH), "BACK [ESC]", actionStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                wheelOpen = false;
                if (Event.current != null) Event.current.Use();
            }
        }

        // ---- fury pass presentation state ----
        private bool furyOpen;
        private Vector2 furyScroll;

        /// One tier's reward cell on one lane.
        private void FuryCell(Rect rect, int tier, bool pro, float s)
        {
            var reward = pro ? GameState.FuryProTrack[tier - 1] : GameState.FuryFreeTrack[tier - 1];
            var claimable = GameState.FuryClaimable(tier, pro);
            var claimedList = pro ? GameState.FuryProClaimed : GameState.FuryFreeClaimed;
            var claimed = tier - 1 < claimedList.Count && claimedList[tier - 1];
            var reached = GameState.FuryTier >= tier;

            var backing = claimable ? rowHighlightTex : pro ? rowOddTex : rowEvenTex;
            GUI.DrawTexture(rect, backing != null ? backing : dimTexture);

            var laneColour = pro ? new Color(1f, 0.62f, 0.22f) : new Color(0.62f, 0.82f, 1f);
            if (!reached) laneColour.a = 0.45f;
            var laneStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(9 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = laneColour }
            };
            GUI.Label(new Rect(rect.x + 3f, rect.y + 2f, rect.width - 6f, rect.height * 0.30f),
                pro ? "PRO" : "FREE", laneStyle);

            var rewardStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(11 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = reached ? Color.white : new Color(0.62f, 0.66f, 0.74f) }
            };
            GUI.Label(new Rect(rect.x + 3f, rect.y + rect.height * 0.26f, rect.width - 6f, rect.height * 0.34f),
                reward.Label, rewardStyle);

            var label = claimed ? "✔ TAKEN"
                : claimable ? "CLAIM"
                : pro && !GameState.FuryPro ? "🔒 PRO"
                : $"{GameState.FuryTierXp * tier - GameState.FuryXp:N0} XP";
            var btnRect = new Rect(rect.x + 4f, rect.y + rect.height * 0.62f, rect.width - 8f, rect.height * 0.34f);
            var btnStyle = new GUIStyle(claimable ? buttonStyle : lockedStyle) { fontSize = Mathf.RoundToInt(10 * s) };
            if (GUI.Button(btnRect, label, btnStyle) && claimable) GameState.ClaimFury(tier, pro);
        }

        /// The Fury Pass: a 28-day track advanced by finished runs. The free lane always
        /// pays; the pro lane is bought with in-game cash and is retroactive, so buying it
        /// at tier 12 hands over all twelve tiers at once.
        private void DrawFuryPass()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            var modalW = Mathf.Clamp(usableW * 0.94f, 480f, 980f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 340f, 600f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;
            GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH),
                cardGlassTex != null ? cardGlassTex : dimTexture);

            var headStyle = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(23 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.55f, 0.18f) }
            };
            GUI.Label(new Rect(modalX, modalY + 6f, modalW, 30f * s), "🔥  F U R Y   P A S S  🔥", headStyle);

            var subStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.75f, 0.88f, 1f) }
            };
            GUI.Label(new Rect(modalX, modalY + 32f * s, modalW, 20f * s),
                $"SEASON {GameState.FurySeason}   •   {GameState.FuryDaysLeft} DAYS LEFT   •   " +
                $"{(GameState.FuryPro ? "PRO ACTIVE" : "FREE LANE")}   •   ${GameState.Cash:N0}   •   💎 {GameState.Gems:N0}", subStyle);

            // Tier progress. Shows XP to the next tier, which is the number that decides
            // whether one more run is worth it.
            var barY = modalY + 54f * s;
            var barH = 16f * s;
            var barX = modalX + 20f;
            var barW = modalW - 40f;
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), dimTexture);
            var frac = GameState.FuryTier >= GameState.FuryTiers
                ? 1f
                : Mathf.Clamp01((float)GameState.FuryTierProgress / GameState.FuryTierXp);
            var prevColour = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.18f);
            GUI.DrawTexture(new Rect(barX, barY, barW * frac, barH), Texture2D.whiteTexture);
            GUI.color = prevColour;

            var barLabelStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(11 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(barX, barY, barW, barH),
                GameState.FuryTier >= GameState.FuryTiers
                    ? $"TIER {GameState.FuryTiers} / {GameState.FuryTiers}  —  TRACK COMPLETE"
                    : $"TIER {GameState.FuryTier} / {GameState.FuryTiers}   —   {GameState.FuryTierProgress:N0} / {GameState.FuryTierXp:N0} XP",
                barLabelStyle);

            // ---- the tier track ----
            var footerH = Mathf.Clamp(modalH * 0.12f, 40f, 52f);
            var footerY = modalY + modalH - footerH - 12f;
            var trackY = barY + barH + 10f * s;
            var trackH = Mathf.Max(110f, footerY - 10f - trackY);
            var trackRect = new Rect(modalX + 14f, trackY, modalW - 28f, trackH);

            var colW = 104f * s;
            var colGap = 8f * s;
            var contentW = GameState.FuryTiers * (colW + colGap) + colGap;

            // Cells are capped rather than stretched to fill: dividing the leftover
            // height in two gave a 195 px cell in a 104 px column on a tall screen.
            // The view is sized to the content, so only the horizontal bar appears.
            var labelH = 17f * s;
            var cellGap = 4f * s;
            var cellH = Mathf.Clamp((Mathf.Max(80f, trackH - 20f) - labelH - cellGap) * 0.5f, 32f, 92f);
            var viewRect = new Rect(0f, 0f, contentW, labelH + cellH * 2f + cellGap);

            furyScroll = GUI.BeginScrollView(trackRect, furyScroll, viewRect);
            var tierOn = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(10 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.82f, 0.25f) }
            };
            var tierOff = new GUIStyle(tierOn) { normal = { textColor = new Color(0.55f, 0.59f, 0.66f) } };

            for (var tier = 1; tier <= GameState.FuryTiers; tier++)
            {
                var cx = colGap + (tier - 1) * (colW + colGap);
                GUI.Label(new Rect(cx, 0f, colW, labelH), $"TIER {tier}",
                    GameState.FuryTier >= tier ? tierOn : tierOff);
                FuryCell(new Rect(cx, labelH, colW, cellH), tier, false, s);
                FuryCell(new Rect(cx, labelH + cellH + cellGap, colW, cellH), tier, true, s);
            }
            GUI.EndScrollView();

            // ---- footer ----
            // Four cells: the pro lane can be unlocked with cash or with gems, and gems
            // can also buy out the current tier. When pro is already active its two
            // cells merge into one status label.
            var footStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(12 * s) };
            var footGap = 8f * s;
            var footW = (modalW - 40f - footGap * 3f) / 4f;
            var footX0 = modalX + 20f;
            var footX1 = footX0 + footW + footGap;
            var footX2 = footX1 + footW + footGap;
            var footX3 = footX2 + footW + footGap;

            if (GameState.FuryPro)
            {
                var proRect = new Rect(footX0, footerY, footW * 2f + footGap, footerH);
                if (statCardGlassTex != null) GUI.DrawTexture(proRect, statCardGlassTex);
                var activeStyle = new GUIStyle(readoutStyle)
                {
                    font = arcadeFont,
                    fontSize = Mathf.RoundToInt(12 * s),
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.62f, 0.22f) }
                };
                GUI.Label(proRect, "🔥 PRO LANE ACTIVE THIS SEASON", activeStyle);
            }
            else
            {
                var affordCash = GameState.Cash >= GameState.FuryProPrice;
                if (affordCash && orangeBtnTex != null)
                    GUI.DrawTexture(new Rect(footX0, footerY, footW, footerH), orangeBtnTex);
                if (GUI.Button(new Rect(footX0, footerY, footW, footerH),
                        $"🔥 PRO  ${GameState.FuryProPrice:N0}", affordCash ? footStyle : lockedStyle) && affordCash)
                {
                    GameState.BuyFuryPro();
                }

                var affordGems = GameState.Gems >= GameState.GemProPrice;
                if (GUI.Button(new Rect(footX1, footerY, footW, footerH),
                        $"🔥 PRO  💎{GameState.GemProPrice}", affordGems ? footStyle : lockedStyle) && affordGems)
                {
                    GameState.BuyFuryProWithGems();
                }
            }

            var canSkip = GameState.FuryTier < GameState.FuryTiers && GameState.Gems >= GameState.GemTierSkipPrice;
            var skipLabel = GameState.FuryTier >= GameState.FuryTiers
                ? "TRACK DONE"
                : $"⏩ SKIP TIER  💎{GameState.GemTierSkipPrice}";
            if (GUI.Button(new Rect(footX2, footerY, footW, footerH), skipLabel,
                    canSkip ? footStyle : lockedStyle) && canSkip)
            {
                GameState.SkipFuryTier();
            }

            if (GUI.Button(new Rect(footX3, footerY, footW, footerH), "BACK [ESC]", footStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                furyOpen = false;
                if (Event.current != null) Event.current.Use();
            }
        }

        // ---- adrenaline revive ----
        private const float ReviveOfferSeconds = 6f;
        private float reviveOfferStart = -1f;
        private bool reviveDeclined;

        private bool ShouldOfferRevive() => GameState.CanRevive && !reviveDeclined;

        private void ResetReviveOffer()
        {
            reviveOfferStart = -1f;
            reviveDeclined = false;
        }

        /// The offer stands on its own, ahead of the crash report, with a clock running.
        /// Folded into the crash report it would be a fifth button on a screen the player
        /// has already read as "run over" - the point is to catch them before that.
        /// Returns false once the clock has run out, so the caller falls through to the
        /// crash report in the same frame rather than drawing nothing.
        private bool DrawRevivePrompt()
        {
            if (reviveOfferStart < 0f) reviveOfferStart = Time.unscaledTime;
            var left = ReviveOfferSeconds - (Time.unscaledTime - reviveOfferStart);
            if (left <= 0f)
            {
                reviveDeclined = true;
                return false;
            }

            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;
            var usableH = h - Mathf.Max(h - (safe.y + safe.height), 12f) - Mathf.Max(safe.y, 12f);
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            var modalW = Mathf.Clamp(w * 0.62f, 380f, 620f);
            var modalH = Mathf.Clamp(usableH * 0.62f, 240f, 380f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;
            GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH),
                cardGlassTex != null ? cardGlassTex : dimTexture);

            var pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 9f);
            var titleS = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(27 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.32f * pulse + 0.1f, 0.18f) }
            };
            GUI.Label(new Rect(modalX, modalY + 10f * s, modalW, 34f * s), "⚡ ADRENALINE ⚡", titleS);

            var subS = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(13 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(modalX, modalY + 44f * s, modalW, 22f * s),
                $"GET BACK ON THE ROAD AT {GameState.ReviveIntegrityFraction * 100f:0}% INTEGRITY", subS);
            GUI.Label(new Rect(modalX, modalY + 64f * s, modalW, 22f * s),
                $"RUN SO FAR:  {GameState.Score:N0} PTS  •  {GameState.Takedowns} TAKEDOWNS  •  {GameState.RunDistanceKm:0.00} KM", subS);

            // The clock, as a draining bar. A number alone does not create the pressure.
            var barY = modalY + modalH * 0.50f;
            var barH = 15f * s;
            var barX = modalX + 24f;
            var barW = modalW - 48f;
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), dimTexture);
            var timeFrac = Mathf.Clamp01(left / ReviveOfferSeconds);
            var prevColour = GUI.color;
            GUI.color = timeFrac > 0.4f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.22f);
            GUI.DrawTexture(new Rect(barX, barY, barW * timeFrac, barH), Texture2D.whiteTexture);
            GUI.color = prevColour;
            GUI.Label(new Rect(barX, barY, barW, barH), $"{Mathf.CeilToInt(left)}",
                new GUIStyle(subS) { fontSize = Mathf.RoundToInt(11 * s) });

            var btnH = Mathf.Clamp(modalH * 0.20f, 44f, 62f);
            var btnY = modalY + modalH - btnH - 14f;
            var btnGap = 8f * s;
            var btnW = (modalW - 40f - btnGap * 2f) / 3f;
            var actionStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) };

            // The default payment - a pro token if one is held, otherwise cash.
            var defaultPay = GameState.ReviveDefaultPayment;
            var canDefault = defaultPay != GameState.RevivePayment.Gems && GameState.CanPayRevive(defaultPay);
            var priceLabel = defaultPay == GameState.RevivePayment.Token
                ? $"⚡ REVIVE  (TOKEN x{GameState.ReviveTokens})"
                : $"⚡ REVIVE  ${GameState.ReviveCost:N0}";

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f * pulse, 1f * pulse, 0.5f * pulse, 1f);
            if (canDefault && greenBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, btnY, btnW, btnH), greenBtnTex);
            var tookDefault = GUI.Button(new Rect(modalX + 20f, btnY, btnW, btnH), priceLabel,
                                  canDefault ? actionStyle : lockedStyle)
                              || (Event.current != null && Event.current.type == EventType.KeyDown
                                  && Event.current.keyCode == KeyCode.Space);
            GUI.backgroundColor = prevBg;

            if (tookDefault && canDefault)
            {
                if (GameState.Revive(defaultPay))
                {
                    // The run continues, so this is not the final score - let the crash
                    // report submit it when the run really ends.
                    hasSubmittedRunScore = false;
                    ResetReviveOffer();
                }
                if (Event.current != null) Event.current.Use();
                return true;
            }

            var canGems = GameState.CanPayRevive(GameState.RevivePayment.Gems);
            if (GUI.Button(new Rect(modalX + 20f + btnW + btnGap, btnY, btnW, btnH),
                    $"💎 REVIVE  {GameState.GemRevivePrice}", canGems ? actionStyle : lockedStyle) && canGems)
            {
                if (GameState.Revive(GameState.RevivePayment.Gems))
                {
                    hasSubmittedRunScore = false;
                    ResetReviveOffer();
                }
                return true;
            }

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnGap) * 2f, btnY, btnW, btnH), "NO — END RUN", actionStyle))
                reviveDeclined = true;

            return true;
        }

        private bool settingsOpen = false;
        private bool sfxEnabled = true;
        private bool showFps = true;

        private void DrawLandingScreen()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            // Safe Area margins (protecting from mobile notches, camera cutouts, and rounded corners)
            var leftPad = Mathf.Max(safe.x, 36f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 36f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 14f);
            var botPad = Mathf.Max(safe.y, 14f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 650f, 0.48f, 1.15f);

            // 1. Top Status Bar
            var topBarH = Mathf.Clamp(usableH * 0.13f, 40f, 58f);
            var topBarY = topPad;
            if (topBarTex != null)
                GUI.DrawTexture(new Rect(0f, 0f, w, topBarY + topBarH), topBarTex);
            else
                GUI.DrawTexture(new Rect(0f, 0f, w, topBarY + topBarH), dimTexture);

            // Top Bar - Left: Player Profile Pill
            var badgeW = Mathf.Clamp(usableW * 0.25f, 150f, 250f);
            var rank = Mathf.Max(1, GameState.Takedowns / 5 + 1);
            if (goldBadgeTex != null)
                GUI.DrawTexture(new Rect(leftPad, topBarY + 3f, badgeW, topBarH - 6f), goldBadgeTex);

            var profileStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(leftPad + 8f, topBarY + 2f, badgeW - 16f, (topBarH - 6f) * 0.52f), $"👑 VIP PILOT • LVL {rank}", profileStyle);
            var streakStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            GUI.Label(new Rect(leftPad + 8f, topBarY + (topBarH - 6f) * 0.48f, badgeW - 16f, (topBarH - 6f) * 0.48f), $"🔥 {GameState.LoginStreak}-DAY STREAK", streakStyle);

            // Top Bar - Center: Cash & High Score
            var centerW = Mathf.Clamp(usableW * 0.45f, 220f, 440f);
            var centerX = w * 0.5f - centerW * 0.5f;
            if (pillBadgeTex != null)
                GUI.DrawTexture(new Rect(centerX, topBarY + 3f, centerW, topBarH - 6f), pillBadgeTex);

            var centerStatStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(centerX, topBarY + 2f, centerW, (topBarH - 6f) * 0.52f),
                $"💰 ${GameState.Cash:N0}   💎 {GameState.Gems:N0}   🏆 {GameState.HighScore:N0}", centerStatStyle);

            var activeBiome = World != null ? World.BiomeName : "Tire District";
            var trackInfoStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.45f, 0.95f, 0.65f) } };
            GUI.Label(new Rect(centerX, topBarY + (topBarH - 6f) * 0.48f, centerW, (topBarH - 6f) * 0.48f),
                $"TRACK: {activeBiome.ToUpper()}  •  {WeatherSystem.Label(World != null ? World.Weather : WeatherKind.Clear).ToUpper()}", trackInfoStyle);

            // Top Bar - Right: Settings Button
            var setBtnW = Mathf.Clamp(usableW * 0.15f, 85f, 125f);
            var setBtnH = topBarH - 6f;
            var setBtnX = w - rightPad - setBtnW;
            var setBtnStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(13 * s) };
            if (GUI.Button(new Rect(setBtnX, topBarY + 3f, setBtnW, setBtnH), "⚙️ SETTINGS", setBtnStyle))
            {
                settingsOpen = !settingsOpen;
            }

            // 2. Left Side: Brand Logo & Vehicle Specs Card
            var contentTop = topBarY + topBarH + Mathf.Max(6f, 8f * s);
            var cardW = Mathf.Clamp(usableW * 0.28f, 190f, 300f);
            var cardH = Mathf.Clamp(usableH * 0.44f, 95f, 150f);

            // Stylized Game Logo
            var logoH = Mathf.Clamp(usableH * 0.11f, 30f, 48f);
            var logoStyle = new GUIStyle(pickerTitleStyle) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(leftPad + 2f, contentTop + 1f, cardW, logoH * 0.65f), "ROAD RAGE", new GUIStyle(logoStyle) { normal = { textColor = Color.black } });
            GUI.Label(new Rect(leftPad, contentTop, cardW, logoH * 0.65f), "ROAD RAGE", logoStyle);

            var subLogoStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(11 * s), normal = { textColor = new Color(1f, 0.82f, 0.2f) } };
            GUI.Label(new Rect(leftPad, contentTop + logoH * 0.55f, cardW, logoH * 0.45f), "⚡ BURNOUT ARCADE RACING ⚡", subLogoStyle);

            // Vehicle Specs Glass Card
            var cardY = contentTop + logoH + 4f;
            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(leftPad, cardY, cardW, cardH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(leftPad, cardY, cardW, cardH), dimTexture);

            var curCar = GameState.CurrentCar;
            var ramName = GameState.TuningRamBar switch { 2 => "TITANIUM RAM", 1 => "STEEL PUSHBAR", _ => "STOCK BUMPER" };
            var inductName = GameState.TuningInduction == 1 ? "TURBOCHARGER (+22 KM/H)" : "SUPERCHARGER (+ACCEL)";

            var lineH = cardH / 4.2f;
            var padIn = 8f;
            var carNameStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(13 * s), normal = { textColor = Color.white } };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f, cardW - padIn * 2, lineH), $"🏎️ {curCar.Name.ToUpper()}", carNameStyle);

            var specStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s) };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH, cardW - padIn * 2, lineH), $"⚡ TOP: {curCar.Speed * 220f:0} KM/H  •  ACC: {curCar.Acceleration:0.0}", specStyle);
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH * 2, cardW - padIn * 2, lineH), $"🛡️ ARMOR: {curCar.Armour:0.0}  •  {ramName}", specStyle);
            var orangeStyle = new GUIStyle(readoutStyle) { fontSize = Mathf.RoundToInt(10 * s), normal = { textColor = new Color(1f, 0.72f, 0.22f) } };
            GUI.Label(new Rect(leftPad + padIn, cardY + 3f + lineH * 3, cardW - padIn * 2, lineH), $"🔥 {inductName}", orangeStyle);

            // 3. Center-Bottom Hero CTA: "START RUN"
            var pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5.0f);
            var ctaW = Mathf.Clamp(usableW * 0.34f, 200f, 340f);
            var ctaH = Mathf.Clamp(usableH * 0.14f, 42f, 60f);
            var ctaX = w * 0.5f - ctaW * 0.5f;

            var dockBtnH = Mathf.Clamp(usableH * 0.11f, 34f, 46f);
            var dockY = h - botPad - dockBtnH;
            var ctaY = dockY - ctaH - Mathf.Max(6f, 10f * s);

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f * pulse, 1f * pulse, 0.5f * pulse, 1f);
            var ctaStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(20 * s) };
            if (GUI.Button(new Rect(ctaX, ctaY, ctaW, ctaH), "🏁  S T A R T   R U N", ctaStyle))
            {
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.LaunchRun();
            }
            GUI.backgroundColor = oldBg;

            var hintStyle = new GUIStyle(readoutStyle) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(10 * s), normal = { textColor = new Color(0.8f, 0.95f, 1f, 0.85f) } };
            GUI.Label(new Rect(0f, ctaY + ctaH + 1f, w, 16f), "PRESS [SPACE] / [ENTER] OR TAP TO RACE", hintStyle);

            // 4. Bottom Dock Nav (Garage, Tracks, Missions, Wheel, Fury Pass, Board)
            var dockSpacing = Mathf.Clamp(usableW * 0.010f, 4f, 10f);
            // Six across now, so the width is also capped by what actually fits.
            var dockBtnW = Mathf.Min(Mathf.Clamp(usableW * 0.15f, 64f, 128f), (usableW - dockSpacing * 5f) / 6f);
            var totalDockW = dockBtnW * 6 + dockSpacing * 5;
            var dockStartX = w * 0.5f - totalDockW * 0.5f;

            var dockBtnStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(12 * s) };

            // Dock Button 1: GARAGE
            if (GUI.Button(new Rect(dockStartX, dockY, dockBtnW, dockBtnH), "🏎️ GARAGE [G]", dockBtnStyle))
            {
                garageOpen = true;
            }

            // Dock Button 2: TRACKS / BIOMES
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing), dockY, dockBtnW, dockBtnH), "🌐 TRACKS [B]", dockBtnStyle))
            {
                if (World != null) World.OpenPicker();
            }

            // Dock Button 3: DAILY MISSIONS
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 2, dockY, dockBtnW, dockBtnH), "🎯 MISSIONS [M]", dockBtnStyle))
            {
                missionsOpen = true;
            }

            // Dock Button 4: LUCKY WHEEL - badged with the spin count, because an
            // unspent free spin is the single best reason to come back tomorrow.
            var wheelDockLabel = GameState.WheelSpins > 0 ? $"🎡 WHEEL ({GameState.WheelSpins})" : "🎡 WHEEL [W]";
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 3, dockY, dockBtnW, dockBtnH), wheelDockLabel, dockBtnStyle))
            {
                wheelOpen = true;
            }

            // Dock Button 5: FURY PASS - badged with tiers waiting to be claimed.
            var furyUnclaimed = GameState.FuryUnclaimedCount();
            var furyDockLabel = furyUnclaimed > 0 ? $"🔥 PASS ({furyUnclaimed})" : $"🔥 PASS T{GameState.FuryTier}";
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 4, dockY, dockBtnW, dockBtnH), furyDockLabel, dockBtnStyle))
            {
                furyOpen = true;
            }

            // Dock Button 6: LEADERBOARD
            if (GUI.Button(new Rect(dockStartX + (dockBtnW + dockSpacing) * 5, dockY, dockBtnW, dockBtnH), "🏆 BOARD [L]", dockBtnStyle))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.OpenLeaderboard();
            }

            // Hotkeys
            if (Event.current != null && Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.G) { garageOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.B && World != null) { World.OpenPicker(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.M) { missionsOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.W) { wheelOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.F) { furyOpen = true; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.L)
                {
                    if (RoadRageLeaderboardDirector.Instance != null)
                        RoadRageLeaderboardDirector.Instance.ToggleLeaderboard();
                    Event.current.Use();
                }
            }

            // 5. Settings Modal
            if (settingsOpen)
            {
                DrawSettingsModal();
            }
        }

        private bool hasSubmittedRunScore = false;
        private string tempPlayerName = "";

        private void DrawRunOverScreen()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            // Auto-submit score to Leaderboard once per run
            if (!hasSubmittedRunScore)
            {
                hasSubmittedRunScore = true;
                if (RoadRageLeaderboardDirector.Instance != null)
                {
                    RoadRageLeaderboardDirector.Instance.SubmitScore(
                        GameState.Score, GameState.Takedowns, GameState.CurrentCar.Name);
                }
            }

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            // Main AAA Modal Frame
            var modalW = Mathf.Clamp(usableW * 0.88f, 480f, 880f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 320f, 580f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            // Top Header: CRASH REPORT
            var headerH = Mathf.Clamp(modalH * 0.13f, 36f, 54f);
            var bannerStyle = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(26 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.35f, 0.25f) }
            };
            GUI.Label(new Rect(modalX, modalY + 6f, modalW, headerH * 0.65f), "💥 CRASH REPORT  •  RUN CONCLUDED 💥", bannerStyle);

            var subBannerStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.85f, 1f, 0.8f) }
            };
            GUI.Label(new Rect(modalX, modalY + headerH * 0.65f + 4f, modalW, headerH * 0.35f),
                $"🔥 +{GameState.LastRunFury:N0} FURY  •  PASS TIER {GameState.FuryTier}/{GameState.FuryTiers}  •  ALL CASH BANKED TO THE GARAGE VAULT",
                subBannerStyle);

            var contentY = modalY + headerH + 8f;
            var colSpacing = 16f * s;
            var colW = (modalW - 40f - colSpacing) * 0.5f;

            // --- LEFT CARD: HERO SCORE & COMBAT STATS ---
            var leftCardX = modalX + 20f;
            var cardH = Mathf.Clamp(modalH * 0.44f, 110f, 190f);

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(leftCardX, contentY, colW, cardH), statCardGlassTex);

            var isNewRecord = GameState.Score >= GameState.HighScore && GameState.Score > 0;
            var scoreHeadStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(1f, 0.8f, 0.3f) } };
            GUI.Label(new Rect(leftCardX + 16f, contentY + 8f, colW - 32f, 20f), "FINAL RUN SCORE", scoreHeadStyle);

            var bigScoreStyle = new GUIStyle(titleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(28 * s),
                normal = { textColor = isNewRecord ? new Color(1f, 0.90f, 0.2f) : Color.white }
            };
            var scoreText = $"{GameState.Score:N0}";
            if (isNewRecord) scoreText += "  ⭐ RECORD!";
            GUI.Label(new Rect(leftCardX + 16f, contentY + 26f * s + 4f, colW - 32f, 38f * s), scoreText, bigScoreStyle);

            // Combat mini metrics
            var combatRowY = contentY + 68f * s;
            var metricStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(13 * s), normal = { textColor = Color.white } };
            GUI.Label(new Rect(leftCardX + 16f, combatRowY, colW - 32f, 22f * s), $"💥 TAKEDOWNS: {GameState.Takedowns}  (+{GameState.AftertouchTakedowns} AFTERTOUCH)", metricStyle);
            GUI.Label(new Rect(leftCardX + 16f, combatRowY + 22f * s, colW - 32f, 22f * s), $"🚨 PILEUP WRECKAGE: ${GameState.PileupDamage:N0}", metricStyle);

            // --- RIGHT CARD: CASH EARNED & VEHICLE STATS ---
            var rightCardX = leftCardX + colW + colSpacing;
            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(rightCardX, contentY, colW, cardH), statCardGlassTex);

            var cashHeadStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(0.4f, 1f, 0.6f) } };
            GUI.Label(new Rect(rightCardX + 16f, contentY + 8f, colW - 32f, 20f), "TOTAL REWARDS BANKED", cashHeadStyle);

            var bigCashStyle = new GUIStyle(titleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(28 * s),
                normal = { textColor = new Color(0.35f, 1f, 0.55f) }
            };
            GUI.Label(new Rect(rightCardX + 16f, contentY + 26f * s + 4f, colW - 32f, 38f * s), $"+${GameState.LastRunCash:N0}", bigCashStyle);

            var carSpec = GameState.CurrentCar;
            GUI.Label(new Rect(rightCardX + 16f, combatRowY, colW - 32f, 22f * s), $"🏎️ PILOT CAR: {carSpec.Name.ToUpper()}", metricStyle);
            GUI.Label(new Rect(rightCardX + 16f, combatRowY + 22f * s, colW - 32f, 22f * s), $"🛣️ HIGHWAY DISTANCE: {GameState.RunDistanceKm:0.00} KM", metricStyle);

            // --- LOWER BLOCK: rank bar, the x2 offer, the action dock ---
            // The rank bar and the button dock keep the sizes and positions they had
            // before the x2 row existed. Only when the extra row would run past the
            // bottom of a short modal is the overflow taken out of all three, so the
            // buttons can never end up off-screen on a phone.
            var rankY = contentY + cardH + 10f;
            var rankH = Mathf.Clamp(modalH * 0.12f, 32f, 48f);
            var doubleH = Mathf.Clamp(modalH * 0.11f, 30f, 44f);
            var btnH = Mathf.Clamp(modalH * 0.15f, 42f, 56f);

            var overflow = (rankY + rankH + 10f + doubleH + 12f + btnH) - (modalY + modalH - 12f);
            if (overflow > 0f)
            {
                var shrink = overflow / 3f;
                rankH = Mathf.Max(22f, rankH - shrink);
                doubleH = Mathf.Max(24f, doubleH - shrink);
                btnH = Mathf.Max(34f, btnH - shrink);
            }

            var doubleY = rankY + rankH + 10f;
            var rankCardW = modalW - 40f;

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, rankY, rankCardW, rankH), statCardGlassTex);

            var rank = Mathf.Max(1, GameState.Takedowns / 5 + 1);
            var xpFrac = Mathf.Clamp01((GameState.Takedowns % 5) / 5f);

            var rankTitleStyle = new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.82f, 0.25f) } };
            GUI.Label(new Rect(modalX + 34f, rankY, rankCardW * 0.35f, rankH), $"👑 VIP PILOT • RANK {rank}", rankTitleStyle);

            var barX = modalX + 34f + rankCardW * 0.35f;
            var barW = rankCardW * 0.58f;
            var barH = rankH * 0.44f;
            var barY = rankY + (rankH - barH) * 0.5f;

            GUI.DrawTexture(new Rect(barX, barY, barW, barH), dimTexture);
            var prevGUIColor = GUI.color;
            GUI.color = new Color(1f, 0.78f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, barW * xpFrac, barH), Texture2D.whiteTexture);
            GUI.color = prevGUIColor;

            var xpLabelStyle = new GUIStyle(readoutStyle) { font = arcadeFont, fontSize = Mathf.RoundToInt(11 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(barX, barY, barW, barH), $"{Mathf.RoundToInt(xpFrac * 100)}% TO RANK {rank + 1}", xpLabelStyle);

            // --- DOUBLE EARNINGS ---
            // The run payout, paid a second time, for one charge. Charges come off the
            // Lucky Wheel, so this row is also what makes the wheel worth opening.
            var doubleRect = new Rect(modalX + 20f, doubleY, rankCardW, doubleH);
            var doubleStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(15f * s) };
            var canDouble = GameState.CanDoubleEarnings;

            if (canDouble)
            {
                var doublePulse = 0.86f + 0.14f * Mathf.Sin(Time.unscaledTime * 5f);
                if (greenBtnTex != null)
                {
                    var prevDoubleCol = GUI.color;
                    GUI.color = new Color(doublePulse, doublePulse, doublePulse, 1f);
                    GUI.DrawTexture(doubleRect, greenBtnTex);
                    GUI.color = prevDoubleCol;
                }
                if (GUI.Button(doubleRect,
                        $"💰 DOUBLE EARNINGS  —  +${GameState.LastRunCash:N0}  (x2 CHARGES: {GameState.DoubleCharges})",
                        doubleStyle))
                {
                    GameState.DoubleEarnings();
                }
            }
            else
            {
                if (statCardGlassTex != null) GUI.DrawTexture(doubleRect, statCardGlassTex);
                var spentStyle = new GUIStyle(readoutStyle)
                {
                    font = arcadeFont,
                    fontSize = Mathf.RoundToInt(12f * s),
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = GameState.DoubleUsedThisRun ? new Color(0.4f, 1f, 0.6f) : new Color(0.72f, 0.78f, 0.88f) }
                };
                GUI.Label(doubleRect, GameState.DoubleUsedThisRun
                    ? "✅ EARNINGS DOUBLED — BANKED AT x2"
                    : "💰 DOUBLE EARNINGS — WIN AN x2 CHARGE ON THE LUCKY WHEEL [W]", spentStyle);
            }

            // --- ACTION BUTTONS DOCK ---
            var btnRowY = doubleY + doubleH + 12f;
            var btnSpacing = 10f * s;
            var btnW = (modalW - 40f - btnSpacing * 3) / 4f;

            var actionBtnStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(14 * s) };

            // Button 1: PLAY AGAIN
            var pulse = 0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f * pulse, 0.95f * pulse, 0.45f * pulse);
            if (greenBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f, btnRowY, btnW, btnH), greenBtnTex);

            if (GUI.Button(new Rect(modalX + 20f, btnRowY, btnW, btnH), "🏁 PLAY AGAIN [SPACE]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space))
            {
                hasSubmittedRunScore = false;
                ResetReviveOffer();
                GameState.BeginRun();
                if (World != null) World.ReloadBiome(World.BiomeName);
                if (Event.current != null) Event.current.Use();
                return;
            }
            GUI.backgroundColor = oldBg;

            // Button 2: LEADERBOARD
            if (blueBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f + (btnW + btnSpacing), btnRowY, btnW, btnH), blueBtnTex);

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing), btnRowY, btnW, btnH), "🏆 BOARD [L]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.L))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.OpenLeaderboard();
                if (Event.current != null) Event.current.Use();
            }

            // Button 3: GARAGE
            if (orangeBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + 20f + (btnW + btnSpacing) * 2, btnRowY, btnW, btnH), orangeBtnTex);

            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 2, btnRowY, btnW, btnH), "🏎️ GARAGE [G]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.G))
            {
                hasSubmittedRunScore = false;
                ResetReviveOffer();
                garageOpen = true;
                if (Event.current != null) Event.current.Use();
            }

            // [W] opens the wheel over the crash screen. Closing it comes straight back
            // here, so a charge won on the wheel can be spent on this run's payout.
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.W)
            {
                wheelOpen = true;
                Event.current.Use();
            }

            // Button 4: MAIN MENU
            if (GUI.Button(new Rect(modalX + 20f + (btnW + btnSpacing) * 3, btnRowY, btnW, btnH), "🏠 MENU [ESC]", actionBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                hasSubmittedRunScore = false;
                ResetReviveOffer();
                GameState.BeginRun();
                if (RoadRageLandingDirector.Instance != null)
                    RoadRageLandingDirector.Instance.ReturnToLanding();
                if (World != null) World.ReloadBiome(World.BiomeName);
                if (Event.current != null) Event.current.Use();
            }
        }

        private void DrawLeaderboardModal()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;

            var leftPad = Mathf.Max(safe.x, 24f);
            var rightPad = Mathf.Max(w - (safe.x + safe.width), 24f);
            var topPad = Mathf.Max(h - (safe.y + safe.height), 12f);
            var botPad = Mathf.Max(safe.y, 12f);

            var usableW = w - leftPad - rightPad;
            var usableH = h - topPad - botPad;
            var s = Mathf.Clamp(usableH / 600f, 0.55f, 1.35f);

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);

            var modalW = Mathf.Clamp(usableW * 0.88f, 480f, 880f);
            var modalH = Mathf.Clamp(usableH * 0.92f, 340f, 580f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            // Title & Status
            var titleStyleL = new GUIStyle(pickerTitleStyle)
            {
                font = titleFont,
                fontSize = Mathf.RoundToInt(24 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.82f, 0.2f) }
            };
            GUI.Label(new Rect(modalX, modalY + 8f, modalW, 30f * s), "🏆 GLOBAL ARCADE LEADERBOARD 🏆", titleStyleL);

            var statusMsg = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.StatusMessage : "Ready";
            var statusStyle = new GUIStyle(readoutStyle)
            {
                font = arcadeFont,
                fontSize = Mathf.RoundToInt(12 * s),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.65f, 0.85f, 1f) }
            };
            GUI.Label(new Rect(modalX, modalY + 36f * s, modalW, 20f), statusMsg, statusStyle);

            // Pilot Tag Selector Bar
            var currentName = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.PlayerName : "RoadWarrior";
            if (string.IsNullOrEmpty(tempPlayerName)) tempPlayerName = currentName;

            var nameRowY = modalY + 56f * s;
            var nameTagW = 140f * s;
            var nameFieldW = 180f * s;
            var nameTotalW = nameTagW + nameFieldW;
            var nameStartX = modalX + modalW * 0.5f - nameTotalW * 0.5f;

            GUI.Label(new Rect(nameStartX, nameRowY, nameTagW, 26f), "✏️ YOUR PILOT TAG:", new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) });
            tempPlayerName = GUI.TextField(new Rect(nameStartX + nameTagW, nameRowY, nameFieldW, 26f), tempPlayerName, 14);
            if (tempPlayerName != currentName && RoadRageLeaderboardDirector.Instance != null)
            {
                RoadRageLeaderboardDirector.Instance.PlayerName = tempPlayerName;
            }

            // Table Header
            var tableHeaderY = nameRowY + 34f;
            var tableX = modalX + 24f;
            var tableW = modalW - 48f;
            var rowH = Mathf.Clamp((modalH - 180f) / 7.2f, 26f, 38f);

            var colRankW = 70f * s;
            var colNameW = 180f * s;
            var colCarW = 140f * s;
            var colTdW = 90f * s;
            var colScoreW = tableW - colRankW - colNameW - colCarW - colTdW;

            if (statCardGlassTex != null)
                GUI.DrawTexture(new Rect(tableX, tableHeaderY, tableW, 26f), statCardGlassTex);

            var thStyle = new GUIStyle(titleStyle) { font = titleFont, fontSize = Mathf.RoundToInt(12 * s), normal = { textColor = new Color(0.6f, 0.8f, 1f) } };
            GUI.Label(new Rect(tableX + 8f, tableHeaderY + 2f, colRankW, 22f), "RANK", thStyle);
            GUI.Label(new Rect(tableX + colRankW, tableHeaderY + 2f, colNameW, 22f), "PILOT", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW, tableHeaderY + 2f, colCarW, 22f), "VEHICLE", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW, tableHeaderY + 2f, colTdW, 22f), "WRECKS", thStyle);
            GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW + colTdW, tableHeaderY + 2f, colScoreW - 8f, 22f), "HIGH SCORE", new GUIStyle(thStyle) { alignment = TextAnchor.MiddleRight });

            // Table Rows
            var entries = RoadRageLeaderboardDirector.Instance != null ? RoadRageLeaderboardDirector.Instance.CachedEntries : new List<LeaderboardEntryData>();
            var rowStartY = tableHeaderY + 28f;

            var maxRows = Mathf.Min(7, entries.Count);
            for (int i = 0; i < maxRows; i++)
            {
                var entry = entries[i];
                var currentY = rowStartY + i * (rowH + 3f);
                var isMe = entry.Username == currentName;

                var bgTex = isMe ? rowHighlightTex : (i % 2 == 0 ? rowEvenTex : rowOddTex);
                if (bgTex != null)
                {
                    GUI.DrawTexture(new Rect(tableX, currentY, tableW, rowH), bgTex);
                }

                var rankBadge = entry.Rank switch
                {
                    1 => "🥇  #1",
                    2 => "🥈  #2",
                    3 => "🥉  #3",
                    _ => $"    #{entry.Rank}"
                };

                var rankColor = entry.Rank switch
                {
                    1 => new Color(1f, 0.85f, 0.25f),
                    2 => new Color(0.85f, 0.90f, 1f),
                    3 => new Color(0.95f, 0.65f, 0.40f),
                    _ => Color.white
                };

                var rowTextStyle = new GUIStyle(readoutStyle)
                {
                    font = arcadeFont,
                    fontSize = Mathf.RoundToInt(13 * s),
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = isMe ? new Color(1f, 0.90f, 0.25f) : rankColor }
                };

                var displayName = isMe ? $"{entry.Username}  (YOU)" : entry.Username;

                GUI.Label(new Rect(tableX + 8f, currentY, colRankW, rowH), rankBadge, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW, currentY, colNameW, rowH), displayName, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW, currentY, colCarW, rowH), entry.CarName, rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW, currentY, colTdW, rowH), $"{entry.Takedowns} ⭐", rowTextStyle);
                GUI.Label(new Rect(tableX + colRankW + colNameW + colCarW + colTdW, currentY, colScoreW - 8f, rowH), $"{entry.Score:N0} PTS", new GUIStyle(rowTextStyle) { alignment = TextAnchor.MiddleRight, font = titleFont });
            }

            // Bottom Buttons
            var botBtnY = modalY + modalH - 48f;
            var botBtnW = 140f * s;
            var botBtnStyle = new GUIStyle(buttonStyle) { font = titleFont, fontSize = Mathf.RoundToInt(13 * s) };

            if (blueBtnTex != null)
                GUI.DrawTexture(new Rect(modalX + modalW * 0.5f - botBtnW - 10f, botBtnY, botBtnW, 40f), blueBtnTex);

            if (GUI.Button(new Rect(modalX + modalW * 0.5f - botBtnW - 10f, botBtnY, botBtnW, 40f), "🔄 REFRESH", botBtnStyle))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.FetchOnlineScores();
            }

            if (GUI.Button(new Rect(modalX + modalW * 0.5f + 10f, botBtnY, botBtnW, 40f), "✖ CLOSE [ESC]", botBtnStyle) ||
                (Event.current != null && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.L)))
            {
                if (RoadRageLeaderboardDirector.Instance != null)
                    RoadRageLeaderboardDirector.Instance.CloseLeaderboard();
                if (Event.current != null) Event.current.Use();
            }
        }

        private void DrawSettingsModal()
        {
            var w = Screen.width;
            var h = Screen.height;
            var modalW = Mathf.Min(w * 0.7f, 400f);
            var modalH = Mathf.Min(h * 0.75f, 350f);
            var modalX = w * 0.5f - modalW * 0.5f;
            var modalY = h * 0.5f - modalH * 0.5f;

            GUI.DrawTexture(new Rect(0f, 0f, w, h), dimTexture);
            if (cardGlassTex != null)
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), cardGlassTex);
            else
                GUI.DrawTexture(new Rect(modalX, modalY, modalW, modalH), dimTexture);

            var s = UiScale;
            var titleS = new GUIStyle(pickerTitleStyle) { fontSize = Mathf.RoundToInt(24 * s) };
            GUI.Label(new Rect(modalX, modalY + 14f, modalW, 30f), "SETTINGS", titleS);

            var rowY = modalY + 54f;
            var rowH = Mathf.Clamp(modalH * 0.14f, 32f, 42f);

            var btnS = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(14 * s) };

            // Audio SFX Toggle
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"SOUND EFFECTS: {(sfxEnabled ? "ON 🔊" : "OFF 🔇")}", btnS))
            {
                sfxEnabled = !sfxEnabled;
                AudioListener.volume = sfxEnabled ? 1f : 0f;
            }
            rowY += rowH + 8f;

            // Haptics Toggle
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH),
                    $"HAPTICS: {(RoadRageHaptics.Enabled ? "ON" : "OFF")}", btnS))
            {
                RoadRageHaptics.Enabled = !RoadRageHaptics.Enabled;
                if (RoadRageHaptics.Enabled) RoadRageHaptics.Medium();
            }
            rowY += rowH + 8f;

            // FPS Display Toggle
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"FPS COUNTER: {(showFps ? "VISIBLE" : "HIDDEN")}", btnS))
            {
                showFps = !showFps;
            }
            rowY += rowH + 8f;

            // Target Framerate
            var currentFps = Application.targetFrameRate;
            if (GUI.Button(new Rect(modalX + 20f, rowY, modalW - 40f, rowH), $"TARGET FPS: {currentFps} FPS", btnS))
            {
                Application.targetFrameRate = currentFps == 120 ? 60 : 120;
            }
            rowY += rowH + 14f;

            // Close Button
            if (GUI.Button(new Rect(modalX + modalW * 0.5f - 70f, rowY, 140f, rowH), "CLOSE", btnS))
            {
                settingsOpen = false;
            }
        }

        private static int pickerCursorIndex = 0;
        private static float lastNavTime = 0f;

        private void HandlePickerGamepadInput(IReadOnlyList<string> playable, int columns)
        {
            if (Event.current != null && Event.current.type != EventType.Layout) return;
            if (Time.unscaledTime - lastNavTime < 0.18f) return;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;

            var left = (pad != null && (pad.dpad.left.isPressed || pad.leftStick.left.isPressed)) ||
                       (kb != null && (kb.leftArrowKey.isPressed || kb.aKey.isPressed));
            var right = (pad != null && (pad.dpad.right.isPressed || pad.leftStick.right.isPressed)) ||
                        (kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed));
            var up = (pad != null && (pad.dpad.up.isPressed || pad.leftStick.up.isPressed)) ||
                     (kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed));
            var down = (pad != null && (pad.dpad.down.isPressed || pad.leftStick.down.isPressed)) ||
                       (kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed));
            var confirm = (pad != null && pad.buttonSouth.wasPressedThisFrame) ||
                          (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame));
            var cancel = (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) ||
                         (kb != null && kb.escapeKey.wasPressedThisFrame);

            if (left) { pickerCursorIndex = Mathf.Max(0, pickerCursorIndex - 1); lastNavTime = Time.unscaledTime; }
            if (right) { pickerCursorIndex = Mathf.Min(playable.Count - 1, pickerCursorIndex + 1); lastNavTime = Time.unscaledTime; }
            if (up) { pickerCursorIndex = Mathf.Max(0, pickerCursorIndex - columns); lastNavTime = Time.unscaledTime; }
            if (down) { pickerCursorIndex = Mathf.Min(playable.Count - 1, pickerCursorIndex + columns); lastNavTime = Time.unscaledTime; }

            if (confirm && pickerCursorIndex >= 0 && pickerCursorIndex < playable.Count)
            {
                Debug.Log($"[BIOME] Selected with gamepad/keyboard: {playable[pickerCursorIndex]}");
                var w = World;
                if (w != null) w.SelectBiome(playable[pickerCursorIndex]);
            }
        }

        private void Update()
        {
            var w = World;
            if (w == null || !w.PickerOpen) return;

            Vector2? pressPos = null;
            try
            {
                var touch = UnityEngine.InputSystem.Touchscreen.current;
                if (touch != null && (touch.primaryTouch.press.wasPressedThisFrame || touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
                {
                    var p = touch.primaryTouch.position.ReadValue();
                    pressPos = new Vector2(p.x, Screen.height - p.y);
                }
                else
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame))
                    {
                        var p = mouse.position.ReadValue();
                        pressPos = new Vector2(p.x, Screen.height - p.y);
                    }
                    else
                    {
                        var ptr = UnityEngine.InputSystem.Pointer.current;
                        if (ptr != null && (ptr.press.wasPressedThisFrame || ptr.press.isPressed || ptr.press.wasReleasedThisFrame))
                        {
                            var p = ptr.position.ReadValue();
                            pressPos = new Vector2(p.x, Screen.height - p.y);
                        }
                    }
                }
            }
            catch {}

            if (pressPos.HasValue)
            {
                var pos = pressPos.Value;
                var playable = RoadRageBootstrap.PlayableBiomes;
                var locked = RoadRageBootstrap.LockedBiomes;
                var columns = Screen.width < 720 ? 2 : 3;
                var panelWidth = Mathf.Min(Screen.width * 0.92f, 1040f);
                var left = (Screen.width - panelWidth) * 0.5f;
                var gap = 12f;
                var cardWidth = (panelWidth - gap * (columns - 1)) / columns;
                var rows = Mathf.CeilToInt((playable.Count + locked.Count) / (float)columns);
                var cardHeight = Mathf.Clamp((Screen.height * 0.62f - gap * (rows - 1)) / rows, 44f, 82f);
                var gridTop = Screen.height * 0.5f - (cardHeight * rows + gap * (rows - 1)) * 0.5f + 24f;

                for (var i = 0; i < playable.Count; i++)
                {
                    var rect = CardRect(left, gridTop, i, columns, cardWidth, cardHeight, gap);
                    if (rect.Contains(pos))
                    {
                        Debug.Log($"[BIOME] Card tapped in Update: {playable[i]}");
                        pickerCursorIndex = i;
                        w.SelectBiome(playable[i]);
                        return;
                    }
                }

                var closeWidth = Mathf.Min(panelWidth * 0.4f, 260f);
                var closeTop = gridTop + rows * (cardHeight + gap) + 14f;
                var driveRect = new Rect(Screen.width * 0.5f - closeWidth * 0.5f, closeTop, closeWidth, 52f);
                if (driveRect.Contains(pos))
                {
                    w.ClosePicker();
                    return;
                }
            }
        }

        private void DrawPicker()
        {
            var w = World;
            if (w == null) return;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTexture);

            var playable = RoadRageBootstrap.PlayableBiomes;
            var locked = RoadRageBootstrap.LockedBiomes;
            var columns = Screen.width < 720 ? 2 : 3;
            var panelWidth = Mathf.Min(Screen.width * 0.92f, 1040f);
            var left = (Screen.width - panelWidth) * 0.5f;
            var gap = 12f;
            var cardWidth = (panelWidth - gap * (columns - 1)) / columns;
            var rows = Mathf.CeilToInt((playable.Count + locked.Count) / (float)columns);
            var cardHeight = Mathf.Clamp((Screen.height * 0.62f - gap * (rows - 1)) / rows, 44f, 82f);
            var gridTop = Screen.height * 0.5f - (cardHeight * rows + gap * (rows - 1)) * 0.5f + 24f;

            HandlePickerGamepadInput(playable, columns);

            GUI.Label(new Rect(left, gridTop - 92f, panelWidth, 44f), "SELECT BIOME", pickerTitleStyle);
            GUI.Label(new Rect(left, gridTop - 50f, panelWidth, 28f),
                $"{playable.Count} PLAYABLE  •  {locked.Count} COMING SOON  (Click, D-Pad/A, or Keys 1-0)", readoutStyle);

            for (var i = 0; i < playable.Count; i++)
            {
                var rect = CardRect(left, gridTop, i, columns, cardWidth, cardHeight, gap);
                var isCurrent = playable[i] == w.BiomeName;
                var isSelected = pickerCursorIndex == i;
                var previousColor = GUI.backgroundColor;
                if (isCurrent) GUI.backgroundColor = new Color(0.28f, 0.92f, 0.55f);
                else if (isSelected) GUI.backgroundColor = new Color(0.35f, 0.70f, 1f);

                var digit = (i + 1) % 10;
                var label = isCurrent ? $"▶ [{digit}] {playable[i]}" : (isSelected ? $"★ [{digit}] {playable[i]}" : $"[{digit}] {playable[i]}");
                
                if (GUI.Button(rect, label, buttonStyle))
                {
                    pickerCursorIndex = i;
                    Debug.Log($"[BIOME] Card clicked in GUI.Button: {playable[i]}");
                    w.SelectBiome(playable[i]);
                    GUI.backgroundColor = previousColor;
                    return;
                }
                GUI.backgroundColor = previousColor;
            }

            for (var i = 0; i < locked.Count; i++)
            {
                var rect = CardRect(left, gridTop, playable.Count + i, columns, cardWidth, cardHeight, gap);
                var previousEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, $"{locked[i]}\nSOON", lockedStyle);
                GUI.enabled = previousEnabled;
            }

            var closeWidth = Mathf.Min(panelWidth * 0.4f, 260f);
            var closeTop = gridTop + rows * (cardHeight + gap) + 14f;
            var driveRect = new Rect(Screen.width * 0.5f - closeWidth * 0.5f, closeTop, closeWidth, 52f);
            if (GUI.Button(driveRect, "DRIVE (ESC / B)", buttonStyle))
            {
                w.ClosePicker();
                return;
            }
        }

        private static Rect CardRect(float left, float top, int index, int columns,
            float cardWidth, float cardHeight, float gap)
        {
            var column = index % columns;
            var row = index / columns;
            return new Rect(left + column * (cardWidth + gap), top + row * (cardHeight + gap), cardWidth, cardHeight);
        }
    }
}
