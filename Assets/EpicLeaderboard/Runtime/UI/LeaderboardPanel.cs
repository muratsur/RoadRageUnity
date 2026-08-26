//#define PREVIEW_SPOTLIGHT 

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EpicLeaderboard
{
    public static class EasingFunctions
    {
        public static float EaseOutElastic(float start, float end, float value)
        {
            end -= start;

            float d = 1f;
            float p = d * .3f;
            float s;
            float a = 0;

            if (value == 0) return start;

            if ((value /= d) == 1) return start + end;

            if (a == 0f || a < Mathf.Abs(end))
            {
                a = end;
                s = p * 0.25f;
            }
            else
            {
                s = p / (2 * Mathf.PI) * Mathf.Asin(end / a);
            }

            return (a * Mathf.Pow(2, -10 * value) * Mathf.Sin((value * d - s) * (2 * Mathf.PI) / p) + end + start);
        }
    }


    public static class ScrollViewFocusFunctions
    {
        public static Vector2 CalculateFocusedScrollPosition(this ScrollRect scrollView, Vector2 focusPoint)
        {
            Vector2 contentSize = scrollView.content.rect.size;
            Vector2 viewportSize = ((RectTransform)scrollView.content.parent).rect.size;
            Vector2 contentScale = scrollView.content.localScale;

            contentSize.Scale(contentScale);
            focusPoint.Scale(contentScale);

            Vector2 scrollPosition = scrollView.normalizedPosition;
            if (scrollView.horizontal && contentSize.x > viewportSize.x)
                scrollPosition.x =
                    Mathf.Clamp01((focusPoint.x - viewportSize.x * 0.5f) / (contentSize.x - viewportSize.x));
            if (scrollView.vertical && contentSize.y > viewportSize.y)
                scrollPosition.y =
                    Mathf.Clamp01((focusPoint.y - viewportSize.y * 0.5f) / (contentSize.y - viewportSize.y));

            return scrollPosition;
        }

        public static Vector2 CalculateFocusedScrollPosition(this ScrollRect scrollView, RectTransform item)
        {
            Vector2 itemCenterPoint =
                scrollView.content.InverseTransformPoint(item.transform.TransformPoint(item.rect.center));

            Vector2 contentSizeOffset = scrollView.content.rect.size;
            contentSizeOffset.Scale(scrollView.content.pivot);

            return scrollView.CalculateFocusedScrollPosition(itemCenterPoint + contentSizeOffset);
        }

        public static void FocusAtPoint(this ScrollRect scrollView, Vector2 focusPoint)
        {
            scrollView.normalizedPosition = scrollView.CalculateFocusedScrollPosition(focusPoint);
        }

        public static void FocusOnItem(this ScrollRect scrollView, RectTransform item)
        {
            scrollView.normalizedPosition = scrollView.CalculateFocusedScrollPosition(item);
        }

        private static IEnumerator LerpToScrollPositionCoroutine(this ScrollRect scrollView,
            Vector2 targetNormalizedPos,
            float speed, Func<float, float> easingFunction = null)
        {
            Vector2 initialNormalizedPos = scrollView.normalizedPosition;

            float t = 0f;
            while (t < 1f)
            {
                float easeT;
                if (easingFunction != null)
                    easeT = easingFunction(t);
                else
                    easeT = 1f - (1f - t) * (1f - t); // default ease-out quad

                scrollView.normalizedPosition =
                    Vector2.LerpUnclamped(initialNormalizedPos, targetNormalizedPos, easeT);

                yield return null;
                t += speed * Time.unscaledDeltaTime;
            }

            scrollView.normalizedPosition = targetNormalizedPos;
        }

        public static IEnumerator FocusAtPointCoroutine(this ScrollRect scrollView, Vector2 focusPoint, float speed,
            Func<float, float> easingFunction = null)
        {
            yield return scrollView.LerpToScrollPositionCoroutine(scrollView.CalculateFocusedScrollPosition(focusPoint),
                speed, easingFunction);
        }

        public static IEnumerator FocusOnItemCoroutine(this ScrollRect scrollView, RectTransform item, float speed,
            Func<float, float> easingFunction = null)
        {
            // Wait one frame so the layout system (VerticalLayoutGroup / ContentSizeFitter)
            // fully resolves item positions and content size after bulk instantiation.
            yield return null;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);

            yield return scrollView.LerpToScrollPositionCoroutine(scrollView.CalculateFocusedScrollPosition(item),
                speed,
                easingFunction);
        }
    }

    public class LeaderboardPanel : MonoBehaviour
    {
        [Header("References (drag from hierarchy)")] [SerializeField]
        GameObject contentContainer;

        // reference to the spotlight entry slot
        [Header("References (drag from hierarchy)")] [SerializeField]
        LeaderboardRow spotlightEntry;

        [SerializeField] ScrollRect scrollRect;

        // row prefab
        [Header("Prefabs (drag from project)")] [SerializeField]
        LeaderboardRow rowPrefab;

        void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += RebuildPreview;
#endif
        }

        private Coroutine _scrollCoroutine;

        private static readonly string[] DemoNames =
        {
            "Astra",
            "Nova",
            "Orion",
            "Vortex",
            "Pulse",
            "Echo",
            "Raptor",
            "Zenith",
            "Blaze",
            "Seraph"
        };

        private static readonly string[] DemoCountries =
        {
            "US",
            "DE",
            "JP",
            "GB",
            "BR",
            "SE",
            "FR",
            "CA",
            "KR",
            "AU"
        };

        private void OnEnable()
        {
            UpdateSafeArea();
        }

        private void Update()
        {
#if UNITY_IOS || UNITY_ANDROID
    UpdateSafeArea();
#endif
        }

        void UpdateSafeArea()
        {
            if (!Application.isPlaying) return;

            RectTransform rt = GetComponent<RectTransform>();
            Rect safeArea = Screen.safeArea;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
        }

        private static ScoreEntry[] GenerateDemoEntries(int count)
        {
            if (count <= 0)
                return Array.Empty<ScoreEntry>();

            var random = new System.Random(79321);
            var entries = new ScoreEntry[count];

            var scores = new List<double>();

            for (var i = 0; i < count; i++)
            {
                var score = random.NextDouble() * 2_000_000 + 500;
                scores.Add(score);
            }

            scores.Sort();
            scores.Reverse();

            for (var i = 0; i < count; i++)
            {
                entries[i] = new ScoreEntry
                {
                    rank = i + 1,
                    username = $"{DemoNames[random.Next(DemoNames.Length)]}{random.Next(1, 9999):D4}",
                    score = scores[i].ToString("N3"),
                    country = DemoCountries[random.Next(DemoCountries.Length)]
                };
            }

            return entries;
        }

        /// <summary>
        /// Smoothly scrolls the leaderboard so the given row RectTransform is centered in the viewport.
        /// </summary>
        public void ScrollToPlayerSmooth(RectTransform rowRect, float speed = 2f)
        {
            if (rowRect == null || scrollRect == null) return;

            Canvas.ForceUpdateCanvases();

            if (_scrollCoroutine != null) StopCoroutine(_scrollCoroutine);
            _scrollCoroutine = StartCoroutine(scrollRect.FocusOnItemCoroutine(rowRect, speed,
                value => EasingFunctions.EaseOutElastic(0, 1, value)));
        }

        IEnumerator SmoothScroll(int playerIndex, int totalRows, float duration)
        {
            Canvas.ForceUpdateCanvases();

            float target = 1f - ((playerIndex + 0.5f) / Mathf.Max(totalRows, 1));
            target = Mathf.Clamp01(target);

            float start = scrollRect.verticalNormalizedPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Smooth ease-out quad
                float eased = 1f - (1f - t) * (1f - t);

                scrollRect.verticalNormalizedPosition = Mathf.Lerp(start, target, eased);
                yield return null;
            }

            scrollRect.verticalNormalizedPosition = target;
        }

        void RebuildPreview()
        {
            if (this == null || contentContainer == null) return;

            if (gameObject.IsPrefabMode())
                return;

            // Clear – im Editor DestroyImmediate verwenden
            contentContainer.transform.DestroyAllChildren();

            bool previewSpotlight = true;

            int numDemoEntries = 50;
            int focusedEntry = previewSpotlight ? -1 : 30;

            var demoEntries = GenerateDemoEntries(numDemoEntries);

            int index = 0;
            LeaderboardRow focusedRow = null;
            foreach (var entry in demoEntries)
            {
                var row = Instantiate(rowPrefab, contentContainer.transform);

                bool shouldHighlight = index == focusedEntry;

                if (shouldHighlight)
                    focusedRow = row;

                row.Populate(entry, shouldHighlight);
                index++;
            }

            // populate spotlight entry 
#if PREVIEW_SPOTLIGHT
            spotlightEntry.Populate(demoEntries.Last(), true);
            spotlightEntry.gameObject.SetActive(true);
#else // hide spotlight entry    
            spotlightEntry.gameObject.SetActive(false);
#endif

            if (Application.isPlaying && focusedRow != null)
            {
                ScrollToPlayerSmooth(focusedRow.GetComponent<RectTransform>(), 0.7f);
            }
        }

        // TODO: spotlight display
        public void DisplayResult(GetScoresResponse entries, string highlightUsername = null)
        {
            if (this == null || contentContainer == null) return;

            // Clear existing rows
            contentContainer.transform.DestroyAllChildren();

            LeaderboardRow firstRow = null;
            LeaderboardRow focusedRow = null;
            int index = 0;
            foreach (var entry in entries.scores)
            {
                var row = Instantiate(rowPrefab, contentContainer.transform);

                bool shouldHighlight = !string.IsNullOrEmpty(highlightUsername) &&
                                       string.Equals(entry.username, highlightUsername,
                                           StringComparison.OrdinalIgnoreCase);

                if (index == 0)
                {
                    firstRow = row;
                }

                if (shouldHighlight)
                {
                    focusedRow = row;
                }

                row.Populate(entry, shouldHighlight);
                index++;
            }

            // populate spotlight entry 
            if (entries.playerScore != null)
            {
                spotlightEntry.Populate(entries.playerScore, true);
                spotlightEntry.gameObject.SetActive(true);

                // set focused row to first place 
                focusedRow = firstRow;
            }
            else // hide spotlight entry    
            {
                spotlightEntry.gameObject.SetActive(false);
            }

            if (Application.isPlaying && focusedRow != null)
            {
                ScrollToPlayerSmooth(focusedRow.GetComponent<RectTransform>(), 0.7f);
            }
        }
    }
}