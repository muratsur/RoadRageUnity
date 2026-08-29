using System.Collections.Generic;
using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Highway Stunt Jump Ramp Director:
    /// Procedurally places highway launch ramps and detects player ramp takeoffs,
    /// triggering high-flying aerial leaps over traffic with stunt bonuses and VFX.
    /// </summary>
    public sealed class RoadRageRampDirector : MonoBehaviour
    {
        public static RoadRageRampDirector Instance { get; private set; }

        private struct RampData
        {
            public float RoadDistance;
            public float LateralOffset;
            public GameObject RampObject;
        }

        private readonly List<RampData> activeRamps = new();
        private Transform playerCar;
        private ArcadeCarController playerController;

        private const float RampSpacing = 420f;
        private const float RampHalfLength = 4.2f;
        private const float RampHalfWidth = 2.4f;

        private Material rampMat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeRampMaterial();
        }

        private void InitializeRampMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            rampMat = new Material(shader) { color = new Color(0.95f, 0.75f, 0.12f, 1f) };
        }

        public void BindPlayer(Transform player)
        {
            playerCar = player;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
            }
        }

        private void Update()
        {
            if (playerController == null || GameState.RunOver) return;

            var playerDist = playerController.RoadDistance;

            // Maintain procedural ramps ahead of the player
            var nextRampDist = Mathf.Floor(playerDist / RampSpacing) * RampSpacing + RampSpacing;
            for (var d = nextRampDist - RampSpacing; d <= playerDist + 800f; d += RampSpacing)
            {
                if (d < playerDist - 150f) continue;
                if (!HasRampNear(d))
                {
                    SpawnRampAt(d);
                }
            }

            // Cleanup distant ramps
            for (var i = activeRamps.Count - 1; i >= 0; i--)
            {
                var r = activeRamps[i];
                if (r.RoadDistance < playerDist - 200f || r.RoadDistance > playerDist + 1200f)
                {
                    if (r.RampObject != null) Destroy(r.RampObject);
                    activeRamps.RemoveAt(i);
                }
            }

            // Check if player drives onto a ramp
            CheckPlayerRampCollision();
        }

        private bool HasRampNear(float dist)
        {
            for (var i = 0; i < activeRamps.Count; i++)
            {
                if (Mathf.Abs(activeRamps[i].RoadDistance - dist) < 50f) return true;
            }
            return false;
        }

        private void SpawnRampAt(float dist)
        {
            var halfW = RoadPath.HalfWidthAt(dist);
            // Alternate left and right outer lanes
            var laneSign = ((int)(dist / RampSpacing) % 2 == 0) ? -1f : 1f;
            var latOffset = (halfW - 3.8f) * laneSign;

            var rampObj = new GameObject($"StuntRamp_{dist:0}");
            rampObj.transform.SetParent(transform, false);

            var mf = rampObj.AddComponent<MeshFilter>();
            var mr = rampObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rampMat;

            mf.sharedMesh = CreateWedgeRampMesh();

            var worldPos = RoadPath.Point(dist, latOffset, 0.1f);
            var worldRot = RoadPath.Rotation(dist);
            rampObj.transform.position = worldPos;
            rampObj.transform.rotation = worldRot;

            activeRamps.Add(new RampData
            {
                RoadDistance = dist,
                LateralOffset = latOffset,
                RampObject = rampObj
            });
        }

        private Mesh CreateWedgeRampMesh()
        {
            var m = new Mesh { name = "WedgeRampMesh" };
            var hl = RampHalfLength;
            var hw = RampHalfWidth;
            var height = 1.65f;

            var verts = new Vector3[]
            {
                // Ramp wedge top face
                new Vector3(-hw, 0.05f, -hl),
                new Vector3(hw, 0.05f, -hl),
                new Vector3(-hw, height, hl),
                new Vector3(hw, height, hl),

                // Back drop face
                new Vector3(-hw, height, hl),
                new Vector3(hw, height, hl),
                new Vector3(-hw, 0.05f, hl),
                new Vector3(hw, 0.05f, hl),

                // Left side
                new Vector3(-hw, 0.05f, -hl),
                new Vector3(-hw, height, hl),
                new Vector3(-hw, 0.05f, hl),

                // Right side
                new Vector3(hw, 0.05f, -hl),
                new Vector3(hw, 0.05f, hl),
                new Vector3(hw, height, hl)
            };

            var tris = new int[]
            {
                // Top
                0, 2, 1,
                1, 2, 3,

                // Back
                4, 5, 6,
                5, 7, 6,

                // Left
                8, 9, 10,

                // Right
                11, 12, 13
            };

            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            return m;
        }

        private void CheckPlayerRampCollision()
        {
            if (playerController == null || playerController.IsAirborne) return;

            var pDist = playerController.RoadDistance;
            var pLat = playerController.LateralOffset;
            var pSpeed = playerController.SpeedKph;

            for (var i = 0; i < activeRamps.Count; i++)
            {
                var r = activeRamps[i];
                var dDist = pDist - r.RoadDistance;
                var dLat = Mathf.Abs(pLat - r.LateralOffset);

                // Check if car is climbing ramp slope and reaching launch lip
                if (dDist >= -RampHalfLength && dDist <= RampHalfLength + 1.5f && dLat < RampHalfWidth + 0.5f)
                {
                    if (pSpeed > 35f)
                    {
                        var launchPower = 12f + (pSpeed / 200f) * 8.5f;
                        playerController.LaunchAirtime(launchPower);

                        if (RoadRageAudioBridge.Instance != null)
                        {
                            RoadRageAudioBridge.Instance.PlayTurboFlutter();
                        }
                        if (RoadRageHapticsDirector.Instance != null)
                        {
                            RoadRageHapticsDirector.Instance.TriggerMediumHaptic(0.45f);
                        }
                        GameState.Show("🚀 STUNT JUMP TAKEOFF!");
                        break;
                    }
                }
            }
        }
    }
}
