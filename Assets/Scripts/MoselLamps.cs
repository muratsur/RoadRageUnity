using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Street lamps. Every lamp head glows when it is dark; only the few nearest the player
    /// cast real light, which keeps a long road of them cheap.
    public sealed class MoselLamps : MonoBehaviour
    {
        private const int RealLights = 4;
        private const float PickEverySeconds = 0.2f;
        private const float DropBehindMetres = 60f;

        /// Set with the headlights, by the conditions of the run.
        public static bool Dark;
        private static readonly List<Vector3> Lamps = new();
        private static Material head;
        private readonly List<Light> lights = new();
        private float nextPick;

        public static void Add(Vector3 lampHead) => Lamps.Add(lampHead);

        public static void Bind(Material material)
        {
            head = material;
            head.EnableKeyword("_EMISSION");
        }

        private void Update()
        {
            if (head != null)
                head.SetColor("_EmissionColor", Dark ? new Color(3.2f, 2.5f, 1.5f) : Color.black);
            if (Time.time < nextPick) return;
            nextPick = Time.time + PickEverySeconds;
            while (lights.Count < RealLights)
            {
                var light = new GameObject("Street Light").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.82f, 0.58f);
                light.range = 22f;
                light.intensity = 9f;
                light.shadows = LightShadows.None;
                lights.Add(light);
            }
            var here = TrafficCarController.PlayerDistance;
            Lamps.RemoveAll(l => Mathf.Abs(l.z - here) > 2000f || (l.z < here - DropBehindMetres && l.z > here - 2000f));
            Lamps.Sort((a, b) => Mathf.Abs(a.z - here).CompareTo(Mathf.Abs(b.z - here)));
            for (var i = 0; i < lights.Count; i++)
            {
                var on = Dark && i < Lamps.Count && Mathf.Abs(Lamps[i].z - here) < 150f;
                lights[i].enabled = on;
                if (on) lights[i].transform.position = Lamps[i] + Vector3.down * 0.5f;
            }
        }
    }
}
