using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class BallzookaController : MonoBehaviour
    {
        [Header("References")]
        public StrikerDevice device;
        public Transform shotSpawnPoint;
        public Transform reloadBigPoint;
        public Transform reloadSmallPoint;
        public GameObject projectileBig;
        public GameObject projectileSmall;

        [Header("Physics")]
        public List<GameObject> ballsSmall;
        public List<GameObject> ballsBig;

        [Header("Animation")]
        public Animator ballzookaAnimator;
        public float bigShotSpeedMultiplier = 0.25f;
        public float smallShotSpeedMultiplier = 1f;

        [Header("Blaster Settings")]
        public float rotationAcceleration;
        public float rotationDeceleration;
        public GameObject bigProjectilePrefab;
        public GameObject smallProjectilePrefab;
        public float bigShotVelocity;
        public float smallShotVelocity;

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioSource fireSpawnSource;
        public AudioSource spinAudioSource;
        public AudioClip clipShot;
        public AudioClip clipMiniMode;
        public AudioClip clipBigMode;
        public AudioClip clipSpinUp;
        public AudioClip clipSpinDown;
        public AudioClip clipSpinLoop;
        public AudioClip clipReload;
        public AudioClip clipSparks;

        [Header("Haptics")]
        public HapticEffectAsset hapticFireSmall;
        public HapticEffectAsset hapticFireBig;
        public HapticEffectAsset hapticReload;
        public HapticEffectAsset hapticRotary;
        public HapticEffectAsset hapticSmallCollision;
        public HapticEffectAsset hapticBigCollision;
        public float ballCollisionCooldown = 0.05f;

        [Header("Particle Systems")]
        public ParticleSystem vfxDoReload;
        public ParticleSystem vfxNeedsReload;
        public ParticleSystem vfxFireShared;
        public ParticleSystem vfxFireBig;
        public ParticleSystem vfxFireSmall;
        public ParticleSystem vfxSparks;
        public ParticleSystem vfxScope;

        [Header("Debug")]
        public bool mouseInputInEditor = false;

        int ballsRemaining;
        bool smallMode = false;

        float currentSpinRate = 0f;
        bool isSpinningUp = false;
        float lastBallCollisionHaptic = 0;

        // Start is called before the first frame update
        void Start()
        {
            Reload();
        }

        // Update is called once per frame
        void Update()
        {
            CheckInputs();
            HandleSpin();
            UpdateAnimator();
            CheckBalls();
        }

        void CheckBalls()
        {
            if(ballsRemaining == 0)
            {
                if (!vfxNeedsReload.isPlaying)
                    vfxNeedsReload.Play();
            }
        }
        
        void UpdateAnimator()
        {
            ballzookaAnimator.SetFloat("SpinSpeed", currentSpinRate * (smallMode ? smallShotSpeedMultiplier : bigShotSpeedMultiplier));
        }

        void CheckInputs()
        {
            // Mode Switch
            if (device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideLeft) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadLeft) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideRight) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.TouchpadRight) || (Application.isEditor && mouseInputInEditor && Input.GetMouseButtonDown(2))) {
                SwitchMode();
            }
            
            if(device.GetSensorDown(Shared.Devices.DeviceFeatures.DeviceSensor.UnderTouchpadGripTouched) || (Application.isEditor && mouseInputInEditor && Input.GetMouseButtonDown(1))) {
                Reload();
            }

            if(device.GetTrigger() || (Application.isEditor && mouseInputInEditor && Input.GetMouseButton(0)))
            {
                if (currentSpinRate < 1f)
                    currentSpinRate += rotationAcceleration * Time.deltaTime;

                if (!isSpinningUp)
                    PlaySpinUp();

                isSpinningUp = true;

                if (!vfxScope.isPlaying)
                    vfxScope.Play();
            } else
            {
                if (isSpinningUp)
                    PlaySpinDown();

                isSpinningUp = false;

                if(currentSpinRate > 0f)
                    currentSpinRate -= rotationDeceleration * Time.deltaTime;

                if (vfxScope.isPlaying)
                    vfxScope.Stop();
            }

            currentSpinRate = Mathf.Clamp01(currentSpinRate);
        }
        
        void PlaySpinUp()
        {
            spinAudioSource.Stop();
            spinAudioSource.PlayOneShot(clipSpinUp);
        }
        
        void PlaySpinDown()
        {
            spinAudioSource.Stop();
            spinAudioSource.PlayOneShot(clipSpinDown);
        }

        void HandleSpin()
        {
            if(isSpinningUp && currentSpinRate >= 1f)
            {
                if (!spinAudioSource.isPlaying || spinAudioSource.clip != clipSpinLoop)
                {
                    spinAudioSource.Stop();
                    spinAudioSource.clip = clipSpinLoop;
                    spinAudioSource.loop = true;
                    spinAudioSource.Play();
                }
            }
        }

        void Reload()
        {
            if(smallMode)
            {
                ballsRemaining = ballsSmall.Count;

                foreach(GameObject ball in ballsSmall)
                {
                    ball.SetActive(true);
                    ball.transform.position = reloadSmallPoint.position;
                }

                foreach(GameObject ball in ballsBig)
                {
                    ball.SetActive(false);
                }
            } else
            {
                ballsRemaining = ballsBig.Count;

                foreach (GameObject ball in ballsBig)
                {
                    ball.SetActive(true);
                    ball.transform.position = reloadBigPoint.position;
                }

                foreach (GameObject ball in ballsSmall)
                {
                    ball.SetActive(false);
                }
            }

            audioSource.PlayOneShot(clipReload);
            vfxDoReload.Play();
            vfxNeedsReload.Stop();
            device.FireHaptic(hapticReload);
        }

        void SwitchMode()
        {
            smallMode = !smallMode;
            Reload();
        }

        public void OnBarrelAligned()
        {
            if (currentSpinRate < 1f)
                return;

            if(ballsRemaining > 0)
            {
                Fire();
            } else
            {
                device.FireHaptic(hapticRotary);
            }
        }

        public void Fire()
        {
            if (ballsRemaining == 0)
                return;

            ballsRemaining--;

            if (smallMode)
            {
                device.FireHaptic(hapticFireSmall);
                vfxFireSmall.Play();
                ballsSmall[ballsRemaining].SetActive(false);

                BallzookaProjectile newProj = Instantiate(smallProjectilePrefab, shotSpawnPoint.position, Quaternion.identity).GetComponent<BallzookaProjectile>();

                newProj.ApplyForce(shotSpawnPoint.forward, smallShotVelocity);
            } else
            {
                device.FireHaptic(hapticFireBig);
                vfxFireBig.Play();
                ballsBig[ballsRemaining].SetActive(false);

                BallzookaProjectile newProj = Instantiate(bigProjectilePrefab, shotSpawnPoint.position, Quaternion.identity).GetComponent<BallzookaProjectile>();

                newProj.ApplyForce(shotSpawnPoint.forward, bigShotVelocity);
            }

            fireSpawnSource.PlayOneShot(clipShot);
            //fireSpawnSource.PlayOneShot(clipSparks);
            vfxSparks.Play();
            vfxFireShared.Play();
        }

        public void OnSmallBallCollision()
        {
            if (lastBallCollisionHaptic > Time.time - ballCollisionCooldown)
                return;

            lastBallCollisionHaptic = Time.time;

            device.FireHaptic(hapticSmallCollision);
        }

        public void OnLargeBallCollision()
        {
            if (lastBallCollisionHaptic > Time.time - ballCollisionCooldown)
                return;

            lastBallCollisionHaptic = Time.time;

            device.FireHaptic(hapticBigCollision);
        }
    }
}