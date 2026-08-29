using UnityEngine;

namespace RoadRage.UnityRemake
{
    public sealed class RoadRageLandingDirector : MonoBehaviour
    {
        public static RoadRageLandingDirector Instance { get; private set; }

        public bool IsLandingActive { get; set; } = true;
        public bool IsTransitioningToRace { get; private set; }

        private float showcaseOrbitAngle = 35f;
        private float transitionStartAngle = 35f;
        private float transitionProgress = 0f;
        private const float TransitionDuration = 0.75f;

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
                // Slow, dynamic showcase sweep around vehicle
                showcaseOrbitAngle += Time.unscaledDeltaTime * 11.0f;
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
            if (!IsLandingActive && !IsTransitioningToRace) return;

            IsLandingActive = false;
            IsTransitioningToRace = true;
            transitionProgress = 0f;
            transitionStartAngle = showcaseOrbitAngle;

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

            if (IsLandingActive)
            {
                var focusPoint = carPos + Vector3.up * 0.65f;
                var rad = showcaseOrbitAngle * Mathf.Deg2Rad;
                var radius = 4.7f + 0.35f * Mathf.Sin(Time.unscaledTime * 0.35f);
                var height = 0.95f + 0.18f * Mathf.Sin(Time.unscaledTime * 0.5f);

                var localOffset = new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius);
                var targetShowcasePos = focusPoint + (carRot * localOffset);

                var floor = carPos.y + 0.35f;
                if (targetShowcasePos.y < floor) targetShowcasePos.y = floor;

                cameraPos = targetShowcasePos;
                cameraRot = Quaternion.LookRotation(focusPoint - targetShowcasePos, car.up);
                return true;
            }

            if (IsTransitioningToRace)
            {
                var t = Mathf.SmoothStep(0f, 1f, transitionProgress);
                var delta = Mathf.DeltaAngle(transitionStartAngle, 180f);
                var currentAngle = transitionStartAngle + delta * t;
                var rad = currentAngle * Mathf.Deg2Rad;

                // Expand radius and lift height smoothly from showcase pose to chase camera pose
                var radius = Mathf.Lerp(4.7f, 8.2f, t);
                var height = Mathf.Lerp(0.95f, 4.7f, t);

                var localOffset = new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius);
                var targetPos = carPos + (carRot * localOffset);

                var floor = carPos.y + 0.65f;
                if (targetPos.y < floor) targetPos.y = floor;

                var lookTarget = carPos + car.up * Mathf.Lerp(0.65f, 1.2f, t) + car.forward * Mathf.Lerp(0f, 9f, t);

                cameraPos = targetPos;
                cameraRot = Quaternion.LookRotation(lookTarget - targetPos, car.up);
                return true;
            }

            cameraPos = Vector3.zero;
            cameraRot = Quaternion.identity;
            return false;
        }
    }
}
