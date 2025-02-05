using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
using System.Collections.Generic;
using Anaglyph.Lasertag.Weapons;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class MavrikController : MonoBehaviour
    {
        public StrikerDevice strikerDevice;
        public Blaster netBlaster;
        public Animator blasterAnimator;

        [Header("Blaster Config")]
        public int ammoSize;
        public Vector2 minMaxPitch;
        public Vector2 minMaxIntensity;
        public Vector2 minMaxSpeed;
        public List<HapticEffectAsset> shotEffects;
        public HapticEffectAsset reloadEffect;
        public HapticEffectAsset dryEffect;

        [Header("Projectile")]
        public GameObject bulletPrefab;
        public GameObject rapidBulletPrefab;
        public Transform muzzleTransform;
        public ParticleSystem muzzleFlash;
        public ParticleSystem autoMuzzleFlash;

        [Header("Debugging")]
        public bool mouseDebugInput;

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioSource shotSource;
        public AudioClip fireClip;
        public AudioClip reloadClip;
        public AudioClip dryFireClip;

        int currentAmmo;
        bool autoMode = false;

        int intensityIndex;
        float rapidSpeed;
        float lastFire;

        // Start is called before the first frame update
        void Start()
        {
            currentAmmo = ammoSize;
            rapidSpeed = minMaxSpeed.x;
            intensityIndex = 0;
        }

        // Update is called once per frame
        void Update()
        {
            CheckInputs();
            UpdateLights();
        }

        void UpdateLights()
        {
            if (blasterAnimator != null)
            {
                blasterAnimator.SetBool("OutOfAmmo", currentAmmo <= 0);
                blasterAnimator.SetBool("Automode", autoMode);
            }
        }

        void CheckInputs()
        {
            if (!autoMode && strikerDevice.GetTriggerDown() || (Application.isEditor && mouseDebugInput && Input.GetMouseButtonDown(0)))
            {
                if (currentAmmo > 0)
                    Fire();
                else
                    DryFire();
            }
            
            if (autoMode && strikerDevice.GetTrigger() || (Application.isEditor && mouseDebugInput && Input.GetMouseButton(0)))
            {
                if (currentAmmo > 0)
                    Fire();
            }
            
            if (strikerDevice.GetSensorDown(Shared.Devices.DeviceFeatures.DeviceSensor.ReloadTouched) || (Application.isEditor && mouseDebugInput && Input.GetMouseButtonDown(1)))
            {
                if (currentAmmo < ammoSize)
                    Reload();
            }
                
            if (strikerDevice.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideLeft) || strikerDevice.GetButtonDown(Shared.Devices.DeviceFeatures.DeviceButton.SideRight) || (Application.isEditor && mouseDebugInput && Input.GetMouseButtonDown(2)))
            {
                autoMode = !autoMode;
            }

            if (strikerDevice.GetSensor(Shared.Devices.DeviceFeatures.DeviceSensor.SlideTouched))
            {
                intensityIndex = Mathf.FloorToInt(Mathf.Lerp(0, shotEffects.Count, strikerDevice.GetAxis(Shared.Devices.DeviceFeatures.DeviceAxis.SlidePosition)));

                if (intensityIndex == shotEffects.Count)
                    intensityIndex = shotEffects.Count - 1;
            }
            
            if(strikerDevice.GetSensor(Shared.Devices.DeviceFeatures.DeviceSensor.ForwardBarGripTouched))
            {
                rapidSpeed = Mathf.Lerp(minMaxSpeed.x, minMaxSpeed.y, strikerDevice.GetAxis(Shared.Devices.DeviceFeatures.DeviceAxis.ForwardBarGripPosition));
            }
        }

        void DryFire()
        {
            PlayAudio(dryFireClip);
            strikerDevice.FireHaptic(dryEffect);
        }

        void PlayAudio(AudioClip clip)
        {
            if (clip != null && audioSource != null)
                audioSource.PlayOneShot(clip);
        }

        void Fire()
        {
            if (autoMode)
            {
                if (lastFire > Time.time - (1f / rapidSpeed))
                    return;

                lastFire = Time.time;
            }

            if (currentAmmo <= 0)
            {
                DryFire();
                return;
            }

            //GameObject bullet = autoMode ? rapidBulletPrefab : bulletPrefab;

            /*if(bullet != null && muzzleTransform != null)
                Instantiate(bullet, muzzleTransform.position, Quaternion.LookRotation(muzzleTransform.forward));*/

            

            

            if (!autoMode && muzzleFlash != null)
                muzzleFlash.Play();
            else if (autoMode && autoMuzzleFlash != null)
                autoMuzzleFlash.Play();

            strikerDevice.FireHaptic(shotEffects[intensityIndex]);

            if (blasterAnimator != null)
                blasterAnimator.SetTrigger("OnFire");

            shotSource.pitch = Mathf.Lerp(minMaxPitch.x, minMaxPitch.y, intensityIndex / 5);
            shotSource.PlayOneShot(fireClip);

            // Delegate actual netcode spawn to Blaster
            if (netBlaster != null)
            {
                netBlaster.Fire();
            }
            else
            {
                Debug.LogWarning("No netBlaster assigned on MavrikController!");
            }

            currentAmmo--;
        }

        void Reload()
        {
            currentAmmo = ammoSize;

            strikerDevice.FireHaptic(reloadEffect);

            PlayAudio(reloadClip);
        }
    }
}