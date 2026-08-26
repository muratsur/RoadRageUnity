using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Smooth, non-intrusive Audio and VFX Controller for Road Rage vehicles.
    /// 100% silent at idle / before game start. Only revs when actually driving.
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public sealed class RoadRageAudioAndVFX : MonoBehaviour
    {
        private ArcadeCarController controller;

        // Audio Sources
        private AudioSource engineSource;
        private AudioSource turboSource;
        private AudioSource nosSource;
        private AudioSource crashSource;
        private AudioSource tireSource;

        // Audio Clips
        private AudioClip engineClip;
        private AudioClip turboBlowOffClip;
        private AudioClip nosWhooshClip;
        private AudioClip crashHeavyClip;
        private AudioClip crashMediumClip;
        private AudioClip tireSkidClip;

        // VFX Instances
        private ParticleSystem leftNosFlame;
        private ParticleSystem rightNosFlame;
        private ParticleSystem leftTireSmoke;
        private ParticleSystem rightTireSmoke;
        private ParticleSystem sparkSystem;

        private float previousSpeedKmh;
        private float previousThrottle;
        private float nextTirePlayTime;

        private void Awake()
        {
            controller = GetComponent<ArcadeCarController>();
            SetupAudioSources();
            LoadAudioClips();
            SetupVFX();
        }

        private void SetupAudioSources()
        {
            engineSource = CreateAudioSource("Engine Audio", loop: true, spatialBlend: 0.2f);
            turboSource = CreateAudioSource("Turbo SFX", loop: false, spatialBlend: 0.3f);
            nosSource = CreateAudioSource("NOS SFX", loop: true, spatialBlend: 0.3f);
            crashSource = CreateAudioSource("Crash SFX", loop: false, spatialBlend: 0.2f);
            tireSource = CreateAudioSource("Tire SFX", loop: false, spatialBlend: 0.3f);
        }

        private AudioSource CreateAudioSource(string name, bool loop, float spatialBlend)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.volume = 0f;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = 60f;
            return src;
        }

        private void LoadAudioClips()
        {
            // Smooth engine rumble loop from VPP
            engineClip = Resources.Load<AudioClip>("Audio/VPP/car rumble")
                      ?? Resources.Load<AudioClip>("Audio/VPP/Car Engine Run 01");

            turboBlowOffClip = Resources.Load<AudioClip>("Audio/VPP/turbo")
                            ?? Resources.Load<AudioClip>("Audio/SFX/Chargers/Turbochargers/Medium/Common/TURBO_MED_MB_01");

            nosWhooshClip = Resources.Load<AudioClip>("Audio/SFX/NOS/NOSWhoosh2")
                         ?? Resources.Load<AudioClip>("Audio/SFX/NOS/NOS");

            crashHeavyClip = Resources.Load<AudioClip>("Audio/VPP/hood impact 7")
                          ?? Resources.Load<AudioClip>("Audio/SFX/Impacts/Cars/CarImpactHigh02");
            crashMediumClip = Resources.Load<AudioClip>("Audio/SFX/Impacts/Cars/CarImpactMedium01");

            tireSkidClip = Resources.Load<AudioClip>("Audio/SFX/Tires/AsphaltSkid_Sideways")
                        ?? Resources.Load<AudioClip>("Audio/SFX/Tires/AsphaltFlatSkid");

            if (engineSource != null && engineClip != null)
            {
                engineSource.clip = engineClip;
                engineSource.volume = 0f; // Start silent!
                engineSource.pitch = 0.85f;
            }
        }

        private void SetupVFX()
        {
            var nosPrefab = Resources.Load<GameObject>("VFX/VehicleNOSFlame")
                         ?? Resources.Load<GameObject>("VFX/VehicleNOSBigFlame");
            var smokePrefab = Resources.Load<GameObject>("VFX/VehicleTireAsphaltSmoke");
            var sparksPrefab = Resources.Load<GameObject>("VFX/VehicleSparks");

            // Attach Left & Right Exhaust NOS Flames
            if (nosPrefab != null)
            {
                leftNosFlame = InstantiateVFX(nosPrefab, new Vector3(-0.48f, 0.28f, -2.1f), Quaternion.Euler(0f, 180f, 0f));
                rightNosFlame = InstantiateVFX(nosPrefab, new Vector3(0.48f, 0.28f, -2.1f), Quaternion.Euler(0f, 180f, 0f));
            }

            // Attach Rear Tire Smoke
            if (smokePrefab != null)
            {
                leftTireSmoke = InstantiateVFX(smokePrefab, new Vector3(-0.85f, 0.15f, -1.2f), Quaternion.Euler(-90f, 0f, 0f));
                rightTireSmoke = InstantiateVFX(smokePrefab, new Vector3(0.85f, 0.15f, -1.2f), Quaternion.Euler(-90f, 0f, 0f));
            }

            // Attach Collision Sparks
            if (sparksPrefab != null)
            {
                sparkSystem = InstantiateVFX(sparksPrefab, Vector3.zero, Quaternion.identity);
            }
        }

        private ParticleSystem InstantiateVFX(GameObject prefab, Vector3 localPos, Quaternion localRot)
        {
            var obj = Instantiate(prefab, transform);
            obj.transform.localPosition = localPos;
            obj.transform.localRotation = localRot;
            var ps = obj.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.playOnAwake = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            return ps;
        }

        private void Update()
        {
            if (controller == null) return;

            var speedKmh = controller.Speed * 3.6f;
            var isAccelerating = controller.IsAccelerating;
            var isBraking = controller.IsBraking;
            var isSteeringHard = Mathf.Abs(controller.SteerInput) > 0.5f && speedKmh > 35f;

            // Completely silent when paused or at standstill before start
            var isDriving = Time.timeScale > 0.01f && (speedKmh > 2f || isAccelerating);

            UpdateEngineSound(speedKmh, isAccelerating, isDriving);
            UpdateTurboBlowOff(speedKmh, isAccelerating, isDriving);
            UpdateNitroVFXAndAudio(isAccelerating, speedKmh, isDriving);
            UpdateTireEffects(isBraking, isSteeringHard, speedKmh, isDriving);

            previousSpeedKmh = speedKmh;
            previousThrottle = isAccelerating ? 1f : 0f;
        }

        private void UpdateEngineSound(float speedKmh, bool isAccelerating, bool isDriving)
        {
            if (engineSource == null) return;

            if (!isDriving)
            {
                // Completely silent at idle / before game start
                engineSource.volume = Mathf.MoveTowards(engineSource.volume, 0f, Time.deltaTime * 3f);
                if (engineSource.volume <= 0.01f && engineSource.isPlaying)
                    engineSource.Stop();
                return;
            }

            if (!engineSource.isPlaying)
                engineSource.Play();

            // Smooth RPM progression (0.80 idle -> 1.35 full revs)
            var speedRatio = Mathf.Clamp01(speedKmh / 200f);
            var targetPitch = Mathf.Lerp(0.80f, 1.35f, speedRatio * speedRatio) + (isAccelerating ? 0.05f : -0.05f);
            engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, Time.deltaTime * 3f);

            var targetVolume = Mathf.Lerp(0.12f, 0.45f, speedRatio) * (isAccelerating ? 1.0f : 0.55f);
            engineSource.volume = Mathf.Lerp(engineSource.volume, targetVolume, Time.deltaTime * 4f);
        }

        private void UpdateTurboBlowOff(float speedKmh, bool isAccelerating, bool isDriving)
        {
            if (!isDriving) return;

            // Play turbo whoosh cleanly on gas release
            if (previousThrottle > 0.5f && !isAccelerating && speedKmh > 80f)
            {
                if (turboSource != null && turboBlowOffClip != null && !turboSource.isPlaying)
                    turboSource.PlayOneShot(turboBlowOffClip, 0.40f);
            }
        }

        private void UpdateNitroVFXAndAudio(bool isAccelerating, float speedKmh, bool isDriving)
        {
            var boosting = isDriving && isAccelerating && speedKmh > 60f;

            // Exhaust NOS Flames
            SetParticleEmission(leftNosFlame, boosting);
            SetParticleEmission(rightNosFlame, boosting);

            // NOS Audio
            if (nosSource != null)
            {
                if (boosting && !nosSource.isPlaying && nosWhooshClip != null)
                {
                    nosSource.clip = nosWhooshClip;
                    nosSource.volume = 0.30f;
                    nosSource.Play();
                }
                else if (!boosting && nosSource.isPlaying)
                {
                    nosSource.volume = Mathf.Lerp(nosSource.volume, 0f, Time.deltaTime * 8f);
                    if (nosSource.volume < 0.02f) nosSource.Stop();
                }
            }
        }

        private void UpdateTireEffects(bool isBraking, bool isSteeringHard, float speedKmh, bool isDriving)
        {
            var drifting = isDriving && (isBraking || isSteeringHard) && speedKmh > 30f;

            // Tire Burnout & Drift Smoke
            SetParticleEmission(leftTireSmoke, drifting);
            SetParticleEmission(rightTireSmoke, drifting);

            // Play tire skid only during actual drifts
            if (drifting && Time.time > nextTirePlayTime && tireSource != null && tireSkidClip != null)
            {
                nextTirePlayTime = Time.time + 0.5f;
                tireSource.PlayOneShot(tireSkidClip, 0.30f);
            }
        }

        private void SetParticleEmission(ParticleSystem ps, bool active)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.enabled = active;
            if (active && !ps.isPlaying) ps.Play();
            else if (!active && ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Call this when the car collides or scores a takedown.
        /// </summary>
        public void PlayCrashImpact(Vector3 contactPoint, bool heavyTakedown = false)
        {
            if (crashSource != null)
            {
                var clip = heavyTakedown ? crashHeavyClip : crashMediumClip;
                if (clip != null) crashSource.PlayOneShot(clip, heavyTakedown ? 0.80f : 0.50f);
            }

            if (sparkSystem != null)
            {
                sparkSystem.transform.position = contactPoint;
                sparkSystem.Emit(heavyTakedown ? 60 : 25);
            }
        }
    }
}
