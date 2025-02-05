using StrikerLink.Unity.Runtime.Core;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class BasicProjectileShooter : MonoBehaviour
    {
        public StrikerDevice strikerDevice;
        public List<HapticEngine.HapticEffectAsset> fireEffects;
        public List<HapticEngine.HapticEffectAsset> reloadEffects;
        public List<HapticEngine.HapticEffectAsset> dryFireEffects;
        public Animator blasterAnimator;
        public AudioSource audioSource;

        [Header("Blaster Config")]
        public int ammo;

        [Header("Projectile")]
        public GameObject bulletPrefab;
        public Transform muzzleTransform;
        public ParticleSystem muzzleFlash;

        [Header("Debugging")]
        public bool mouseDebugInput;

        [Header("Audio")]
        public AudioClip fireClip;
        public AudioClip reloadClip;
        public AudioClip dryFireClip;

        int currentAmmo;

        // Start is called before the first frame update
        void Start()
        {
            currentAmmo = ammo;
        }

        // Update is called once per frame
        void Update()
        {
            CheckInputs();
            UpdateLights();
        }

        void UpdateLights()
        {
            if(blasterAnimator != null)
                blasterAnimator.SetBool("OutOfAmmo", currentAmmo <= 0);
        }

        void CheckInputs()
        {
            if (strikerDevice.GetTriggerDown() || (Application.isEditor && mouseDebugInput && Input.GetMouseButtonDown(0)))
            {
                if (currentAmmo > 0)
                    Fire();
                else
                    DryFire();
            }
            else if (strikerDevice.GetSensorDown(Shared.Devices.DeviceFeatures.DeviceSensor.ReloadTouched) || (Application.isEditor && mouseDebugInput && Input.GetMouseButtonDown(1)))
            {
                if(currentAmmo < ammo)
                    Reload();
            }
        }

        void DryFire()
        {
            PlayAudio(dryFireClip);
            FireRandomEffectFromList(dryFireEffects);
        }

        void PlayAudio(AudioClip clip)
        {
            if (clip != null && audioSource != null)
                audioSource.PlayOneShot(clip);
        }

        void Fire()
        {
            if(bulletPrefab != null && muzzleTransform != null)
                Instantiate(bulletPrefab, muzzleTransform.position, Quaternion.LookRotation(muzzleTransform.forward));

            if (muzzleFlash != null)
                muzzleFlash.Play();

            FireRandomEffectFromList(fireEffects);

            if (blasterAnimator != null)
                blasterAnimator.SetTrigger("OnFire");

            PlayAudio(fireClip);

            currentAmmo--;
        }

        void Reload()
        {
            currentAmmo = ammo;

            FireRandomEffectFromList(reloadEffects);

            PlayAudio(reloadClip);
        }

        void FireRandomEffectFromList(List<HapticEngine.HapticEffectAsset> effects)
        {
            if (effects != null && effects.Count > 0)
                strikerDevice.FireHaptic(effects[Random.Range(0, effects.Count)]);
        }
    }
}