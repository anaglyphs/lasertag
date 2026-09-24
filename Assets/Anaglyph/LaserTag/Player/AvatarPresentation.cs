using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Hides spatial features without disabling the NetworkObject or any NetworkBehaviours.
	/// Renderer.enabled remains owned by components such as DontRenderIfOwner.
	/// </summary>
	[RequireComponent(typeof(PlayerAvatar))]
	public class AvatarPresentation : MonoBehaviour
	{
		[SerializeField] private GameObject wandPrefab;
		[SerializeField] private Renderer helmetRenderer;
		[SerializeField] private Renderer[] wizardHatRenderers = { };

		private PlayerAvatar avatar;
		private AvatarWeaponVisual[] weapons;
		private Renderer[] renderers;
		private Collider[] colliders;
		private bool[] colliderEnabled;
		private ParticleSystem[] particles;
		private AudioSource[] audioSources;
		private bool[] audioMuted;
		private bool wasPresent;

		private void Awake()
		{
			avatar = GetComponent<PlayerAvatar>();
			weapons = GetComponentsInChildren<AvatarWeaponVisual>(true);
			renderers = GetComponentsInChildren<Renderer>(true);
			colliders = GetComponentsInChildren<Collider>(true);
			colliderEnabled = new bool[colliders.Length];
			for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
			particles = GetComponentsInChildren<ParticleSystem>(true);
			audioSources = GetComponentsInChildren<AudioSource>(true);
			audioMuted = new bool[audioSources.Length];
			for (int i = 0; i < audioSources.Length; i++) audioMuted[i] = audioSources[i].mute;
			Refresh();
		}

		private void LateUpdate() => Refresh();

		private void Refresh()
		{
			bool present = avatar.HasSpatialPresence;
			foreach (Renderer renderer in renderers)
				renderer.forceRenderingOff = !present || (!avatar.IsAlive && renderer is not ParticleSystemRenderer);
			RefreshHeadwear();
			for (int i = 0; i < colliders.Length; i++)
				colliders[i].enabled = colliderEnabled[i] && present && avatar.IsAlive;
			for (int i = 0; i < audioSources.Length; i++)
				audioSources[i].mute = audioMuted[i] || !present;
			if (wasPresent && !present)
			{
				foreach (ParticleSystem particle in particles)
					particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
				foreach (AudioSource source in audioSources) source.Stop();
			}
			wasPresent = present;
		}

		private void RefreshHeadwear()
		{
			bool hasWand = false;
			if (wandPrefab != null)
				foreach (AvatarWeaponVisual weapon in weapons)
					hasWand |= weapon.EquippedWeapon == wandPrefab;

			if (helmetRenderer != null)
				helmetRenderer.forceRenderingOff |= hasWand;
			foreach (Renderer renderer in wizardHatRenderers)
				renderer.forceRenderingOff |= !hasWand || avatar.IsOwner;
		}
	}
}
