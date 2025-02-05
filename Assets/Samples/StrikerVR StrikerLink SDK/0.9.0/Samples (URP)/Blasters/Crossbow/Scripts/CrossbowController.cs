using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class CrossbowController : MonoBehaviour
    {
        [Header("References")]
        public StrikerDevice device;
        public Animator crossbowAnimator;
        public Animator unicornAnimator;
        public Transform projectileMountPoint;

        [Header("Crossbow Settings")]
        public float swipeLoadTolerance = 0.1f;
        public float sliderMargin = 0.15f;
        public float loadingInterpolationSpeed = 2f;
        public float loadedDecayRate = 0.25f;
        public float projectileForce = 20f;

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioClip clipFire;
        public AudioClip clipReload;
        public List<AudioClip> clipNotches;

        [Header("Haptics")]
        public HapticEffectAsset hapticShot;
        public HapticEffectAsset hapticReload;
        public HapticEffectAsset hapticExplode;
        public List<HapticEffectAsset> hapticNotches;

        [Header("Projectiles")]
        public GameObject normalProjectile;
        public GameObject explosiveProjectile;

        [Header("Runtime Properties")]
        public float loadedAmount = 0f;
        public float targetLoadedAmount = 0f;
        public bool explosiveMode = false;
        public CrossbowProjectile activeProjectile;

        private void Update()
        {
            UpdateLoadingAmount();
            UpdateAnimator();
            HandleInputs();
        }

        void HandleInputs()
        {
            if(device.GetTriggerDown())
            {
                Fire();
            }

            if(device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideLeft) || device.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideRight))
            {
                if (activeProjectile != null)
                {
                    Destroy(activeProjectile.gameObject);
                    activeProjectile = null;
                }

                explosiveMode = !explosiveMode;

                if (loadedAmount >= 1f)
                {
                    SpawnProjectile();
                }
            }
        }

        void UpdateLoadingAmount()
        {
            if (loadedAmount >= 1f)
                return;

            // We use sliderMargin to provide some leniancy on reaching the end of the slide
            float slidePos = (1f - device.GetAxis(Shared.Devices.DeviceFeatures.DeviceAxis.SlidePosition)) * (1f + sliderMargin);

            // Only allow a touch if it's within a certain distance of the current target value, so you actually have to slide your hand across it
            if(device.GetSensor(Shared.Devices.DeviceFeatures.DeviceSensor.SlideTouched) && slidePos > targetLoadedAmount && slidePos <= (targetLoadedAmount + swipeLoadTolerance))
            {
                targetLoadedAmount = Mathf.Clamp01(slidePos);
            } else if(!device.GetSensor(Shared.Devices.DeviceFeatures.DeviceSensor.SlideTouched) && targetLoadedAmount < 1f)
            {
                targetLoadedAmount = Mathf.Clamp01(Mathf.MoveTowards(targetLoadedAmount, 0f, loadedDecayRate * Time.deltaTime));
            }

            // Interpolate it for visual niceness
            loadedAmount = Mathf.MoveTowards(loadedAmount, targetLoadedAmount, loadingInterpolationSpeed * Time.deltaTime);
            loadedAmount = Mathf.Clamp01(loadedAmount);

            if (loadedAmount >= 1f && activeProjectile == null)
                SpawnProjectile();
        }

        void UpdateAnimator()
        {
            crossbowAnimator.SetFloat("LoadedAmount", loadedAmount);
        }

        void SpawnProjectile()
        {
            if (explosiveMode)
                activeProjectile = Instantiate(explosiveProjectile).GetComponent<CrossbowProjectile>();
            else
                activeProjectile = Instantiate(normalProjectile).GetComponent<CrossbowProjectile>();

            audioSource.PlayOneShot(clipReload);

            activeProjectile.firingController = this;

            activeProjectile.transform.SetParent(projectileMountPoint);
            activeProjectile.transform.localPosition = Vector3.zero;
            activeProjectile.transform.localRotation = Quaternion.identity;

            device.FireHaptic(hapticReload);
        }

        public void OnReachedNotch(int index)
        {
            audioSource.PlayOneShot(clipNotches[index]);
            device.FireHaptic(hapticNotches[index]);
        }

        void Fire()
        {
            if (loadedAmount < 1f)
                return;

            if (activeProjectile == null)
            {
                SpawnProjectile();
                return;
            }

            if (activeProjectile.spawnProgress < 1f)
                return;

            audioSource.PlayOneShot(clipFire);

            crossbowAnimator.SetTrigger("Fire");
            unicornAnimator.SetTrigger("Shot");

            loadedAmount = 0f;
            targetLoadedAmount = 0f;

            activeProjectile.Fire(projectileForce);
            activeProjectile = null;

            device.FireHaptic(hapticShot);
        }

        public void OnDistantExplosion()
        {
            device.FireHaptic(hapticExplode);
        }
    }
}