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

        private const float RampSpacing = 650f;
        private const float RampHalfLength = 4.8f;
        private const float RampHalfWidth = 2.4f;

        private Material rampDeckMat;
        private Material rampFrameMat;
        private Material hazardMat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            InitializeRampMaterials();
        }

        private void InitializeRampMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            rampDeckMat = new Material(shader)
            {
                color = new Color(0.20f, 0.21f, 0.24f, 1f) // Dark asphalt ramp deck
            };
            rampDeckMat.SetFloat("_Metallic", 0.15f);
            rampDeckMat.SetFloat("_Smoothness", 0.35f);

            rampFrameMat = new Material(shader)
            {
                color = new Color(0.85f, 0.65f, 0.08f, 1f) // Yellow industrial steel frame
            };
            rampFrameMat.SetFloat("_Metallic", 0.75f);
            rampFrameMat.SetFloat("_Smoothness", 0.65f);

            hazardMat = new Material(shader)
            {
                color = new Color(1.0f, 0.35f, 0.05f, 1f) // Safety Orange Cones
            };
        }

        public void BindPlayer(Transform player)
        {
            playerCar = player;
            if (player != null)
            {
                playerController = player.GetComponent<ArcadeCarController>();
            }
        }

        public void ClearRamps()
        {
            for (var i = activeRamps.Count - 1; i >= 0; i--)
            {
                if (activeRamps[i].RampObject != null)
                {
                    Destroy(activeRamps[i].RampObject);
                }
            }
            activeRamps.Clear();
        }

        private void OnDestroy()
        {
            ClearRamps();
        }

        private void Update()
        {
            if (playerController == null || GameState.RunOver || 
                (RoadRageLandingDirector.Instance != null && RoadRageLandingDirector.Instance.IsLandingActive))
            {
                return;
            }

            var playerDist = playerController.RoadDistance;

            // Only spawn ramps on highway straightaways starting at 450m ahead
            var firstRampDist = 450f;
            var nextRampDist = Mathf.Floor((playerDist - firstRampDist) / RampSpacing) * RampSpacing + firstRampDist + RampSpacing;

            for (var d = nextRampDist - RampSpacing; d <= playerDist + 750f; d += RampSpacing)
            {
                if (d < playerDist + 80f) continue;
                if (!HasRampNear(d))
                {
                    SpawnRampAt(d);
                }
            }

            // Cleanup distant ramps
            for (var i = activeRamps.Count - 1; i >= 0; i--)
            {
                var r = activeRamps[i];
                if (r.RoadDistance < playerDist - 120f || r.RoadDistance > playerDist + 1200f)
                {
                    if (r.RampObject != null) Destroy(r.RampObject);
                    activeRamps.RemoveAt(i);
                }
            }

            // Check if player drives onto a ramp
            CheckPlayerRampCollision();
            // ...and traffic, which used to drive straight through the deck. A ramp that
            // only the player can use is scenery for everyone else, and it reads as the
            // world being fake: the interesting shot is the car ahead of you taking the
            // jump you are about to take.
            CheckTrafficRampCollisions();
        }

        private bool HasRampNear(float dist)
        {
            for (var i = 0; i < activeRamps.Count; i++)
            {
                if (Mathf.Abs(activeRamps[i].RoadDistance - dist) < 60f) return true;
            }
            return false;
        }

        private void SpawnRampAt(float dist)
        {
            var halfW = RoadPath.HalfWidthAt(dist);
            // Alternate left and right outer lanes
            var laneSign = ((int)(dist / RampSpacing) % 2 == 0) ? -1f : 1f;
            var latOffset = (halfW - 3.4f) * laneSign;

            var rampObj = new GameObject($"StuntRamp_{dist:0}");
            // CRITICAL: Must be root world object, NEVER parented to camera!
            rampObj.transform.SetParent(null, false);

            var mf = rampObj.AddComponent<MeshFilter>();
            var mr = rampObj.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new Material[] { rampDeckMat, rampFrameMat };

            mf.sharedMesh = CreateWedgeRampMesh();

            var worldPos = RoadPath.Point(dist, latOffset, 0.05f);
            var worldRot = RoadPath.Rotation(dist);
            rampObj.transform.position = worldPos;
            rampObj.transform.rotation = worldRot;

            // Spawn warning hazard cones leading up to the ramp
            for (var c = 0; c < 3; c++)
            {
                var coneDist = dist - 18f + c * 5.5f;
                var conePos = RoadPath.Point(coneDist, latOffset + (c % 2 == 0 ? -1.1f : 1.1f), 0.35f);
                var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cone.name = "Ramp Warning Cone";
                cone.transform.SetParent(rampObj.transform, false);
                cone.transform.position = conePos;
                cone.transform.localScale = new Vector3(0.25f, 0.35f, 0.25f);
                var cr = cone.GetComponent<Renderer>();
                if (cr != null) cr.sharedMaterial = hazardMat;
                var cc = cone.GetComponent<Collider>();
                if (cc != null) Destroy(cc);
            }

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
                // Ramp wedge top asphalt deck (Submesh 0)
                new Vector3(-hw, 0.05f, -hl),
                new Vector3(hw, 0.05f, -hl),
                new Vector3(-hw, height, hl),
                new Vector3(hw, height, hl),

                // Back drop face (Submesh 1 - Yellow Frame)
                new Vector3(-hw, height, hl),
                new Vector3(hw, height, hl),
                new Vector3(-hw, 0.05f, hl),
                new Vector3(hw, 0.05f, hl),

                // Left side (Submesh 1 - Yellow Frame)
                new Vector3(-hw, 0.05f, -hl),
                new Vector3(-hw, height, hl),
                new Vector3(-hw, 0.05f, hl),

                // Right side (Submesh 1 - Yellow Frame)
                new Vector3(hw, 0.05f, -hl),
                new Vector3(hw, 0.05f, hl),
                new Vector3(hw, height, hl)
            };

            var deckTris = new int[]
            {
                0, 2, 1,
                1, 2, 3
            };

            var frameTris = new int[]
            {
                4, 5, 6,
                5, 7, 6,
                8, 9, 10,
                11, 12, 13
            };

            m.subMeshCount = 2;
            m.vertices = verts;
            m.SetTriangles(deckTris, 0);
            m.SetTriangles(frameTris, 1);
            m.RecalculateNormals();
            return m;
        }

        /// Same geometry test as the player's, against every live traffic car. Traffic
        /// needs more speed than the player to commit, so slow cars still filter past a
        /// ramp rather than the whole highway launching at once.
        private void CheckTrafficRampCollisions()
        {
            var cars = TrafficCarController.All;
            for (var c = 0; c < cars.Count; c++)
            {
                var car = cars[c];
                if (car == null || car.IsAirborne || car.IsWreck) continue;

                for (var i = 0; i < activeRamps.Count; i++)
                {
                    var r = activeRamps[i];
                    var dDist = car.RoadDistance - r.RoadDistance;
                    var dLat = Mathf.Abs(car.LaneOffset - r.LateralOffset);

                    if (dDist < -RampHalfLength || dDist > RampHalfLength + 1.8f) continue;
                    if (dLat > RampHalfWidth + 0.4f) continue;
                    if (car.SpeedKph <= 55f) continue;

                    // Scaled down from the player's launch: traffic should clear the
                    // ramp convincingly, not sail over the skyline and steal the shot.
                    car.LaunchAirtime(9.5f + car.SpeedKph / 200f * 4.5f);
                    break;
                }
            }
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
                if (dDist >= -RampHalfLength && dDist <= RampHalfLength + 1.8f && dLat < RampHalfWidth + 0.4f)
                {
                    if (pSpeed > 35f)
                    {
                        var launchPower = 13.5f + (pSpeed / 200f) * 7.5f;
                        playerController.LaunchAirtime(launchPower);

                        if (RoadRageAudioBridge.Instance != null)
                        {
                            RoadRageAudioBridge.Instance.PlayTurboFlutter();
                        }
                        if (RoadRageImpactShakeDirector.Instance != null)
                        {
                            RoadRageImpactShakeDirector.Instance.TriggerMediumShake(0.45f);
                        }
                        GameState.Show("🚀 STUNT JUMP TAKEOFF!");
                        break;
                    }
                }
            }
        }
    }
}
