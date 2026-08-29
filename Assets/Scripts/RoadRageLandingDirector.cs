using UnityEngine;

namespace RoadRage.UnityRemake
{
    public sealed class RoadRageLandingDirector : MonoBehaviour
    {
        public static RoadRageLandingDirector Instance { get; private set; }

        public bool IsLandingActive { get; set; } = true;
        public bool IsTransitioningToRace { get; private set; }

        private float showcaseOrbitAngle = 40f;
        private float transitionProgress = 0f;
        private const float TransitionDuration = 0.85f;

        private Vector3 transitionStartPos;
        private Quaternion transitionStartRot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                var prevState = Instance.IsLandingActive;
                Destroy(Instance.gameObject);
                Instance = this;
                IsLandingActive = prevState;
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (IsLandingActive)
            {
                showcaseOrbitAngle += Time.unscaledDeltaTime * 14.0f;
                if (showcaseOrbitAngle >= 360f) showcaseOrbitAngle -= 360f;

                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    LaunchRun();
                }
            }
            else if (IsTransitioningToRace)
            {
                transitionProgress += Time.unscaledDeltaTime / TransitionDuration;
                if (transitionProgress >= 1f)
                {
                    transitionProgress = 1f;
                    IsTransitioningToRace = false;
                }
            }
        }

        public void LaunchRun()
        {
            if (!IsLandingActive) return;

            IsLandingActive = false;
            IsTransitioningToRace = true;
            transitionProgress = 0f;

            if (Camera.main != null)
            {
                transitionStartPos = Camera.main.transform.position;
                transitionStartRot = Camera.main.transform.rotation;
            }

            if (RoadRageAudioBridge.Instance != null)
            {
                RoadRageAudioBridge.Instance.PlayTurboFlutter();
            }

            GameState.ResetRun();
            GameState.BeginRun();

            var car = GameObject.FindWithTag("Player");
            if (car != null)
            {
                var controller = car.GetComponent<ArcadeCarController>();
                if (controller != null)
                {
                    controller.CountdownTimer = 3.0f;
                    controller.SpeedKph = 0f;
                }
            }
        }

        public void ReturnToLanding()
        {
            IsLandingActive = true;
            IsTransitioningToRace = false;
            transitionProgress = 0f;

            var car = GameObject.FindWithTag("Player");
            if (car != null)
            {
                var controller = car.GetComponent<ArcadeCarController>();
                if (controller != null)
                {
                    controller.SpeedKph = 0f;
                    controller.TouchThrottle = 0f;
                    controller.TouchSteer = 0f;
                }
            }
        }

        public bool TryGetShowcaseCameraPose(Transform car, out Vector3 cameraPos, out Quaternion cameraRot)
        {
            if (car == null)
            {
                cameraPos = Vector3.zero;
                cameraRot = Quaternion.identity;
                return false;
            }

            var carPos = car.position;
            var carRot = car.rotation;

            var focusPoint = carPos + Vector3.up * 0.95f;

            var rad = showcaseOrbitAngle * Mathf.Deg2Rad;
            var radius = 6.2f + 0.4f * Mathf.Sin(Time.unscaledTime * 0.4f);
            var height = 1.6f + 0.35f * Mathf.Sin(Time.unscaledTime * 0.6f);

            var localOffset = new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius);
            var targetShowcasePos = focusPoint + (carRot * localOffset);
            var targetShowcaseRot = Quaternion.LookRotation(focusPoint - targetShowcasePos);

            if (IsLandingActive)
            {
                cameraPos = targetShowcasePos;
                cameraRot = targetShowcaseRot;
                return true;
            }

            if (IsTransitioningToRace)
            {
                var t = Mathf.SmoothStep(0f, 1f, transitionProgress);
                cameraPos = Vector3.Lerp(transitionStartPos, targetShowcasePos, 1f - t);
                cameraRot = Quaternion.Slerp(transitionStartRot, targetShowcaseRot, 1f - t);
                return true;
            }

            cameraPos = Vector3.zero;
            cameraRot = Quaternion.identity;
            return false;
        }
    }
}
