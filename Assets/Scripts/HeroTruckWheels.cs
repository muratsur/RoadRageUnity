using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Rolls the hero truck's four wheels with its speed and turns the front two with the
    /// steering. Each wheel is its own object with its origin on its axle
    /// (Tools/Rodin/build_hero_truck_asset.py).
    public sealed class HeroTruckWheels : MonoBehaviour
    {
        private const float WheelRadius = 0.45f;
        private const float MaxSteerDegrees = 24f;
        private const float SteerSpeed = 140f;

        private struct Wheel
        {
            public Transform Transform;
            public Quaternion Rest;
            public bool Steers;
        }

        private Wheel[] wheels = System.Array.Empty<Wheel>();
        private ArcadeCarController car;
        private float roll;
        private float steer;

        public int Count => wheels.Length;

        public void Bind(Transform visual, ArcadeCarController controller)
        {
            car = controller;
            var found = new System.Collections.Generic.List<Wheel>();
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Wheel_"))
                    found.Add(new Wheel
                    {
                        Transform = t,
                        Rest = Quaternion.Inverse(transform.rotation) * t.rotation,
                        Steers = t.name.StartsWith("Wheel_F"),
                    });
            wheels = found.ToArray();
            Debug.Log($"RR_HERO wheels bound={wheels.Length}");
        }

        private void LateUpdate()
        {
            if (car == null) return;
            var metres = car.SpeedKph / 3.6f * Time.deltaTime;
            roll = Mathf.Repeat(roll + metres / WheelRadius * Mathf.Rad2Deg, 360f);
            steer = Mathf.MoveTowards(steer, car.SteerInput * MaxSteerDegrees, SteerSpeed * Time.deltaTime);
            var spin = Quaternion.Euler(roll, 0f, 0f);
            var turned = Quaternion.Euler(0f, steer, 0f) * spin;
            foreach (var wheel in wheels)
                wheel.Transform.rotation = transform.rotation * (wheel.Steers ? turned : spin) * wheel.Rest;
        }
    }
}
