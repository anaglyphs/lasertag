using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Hides spatial features without disabling the NetworkObject or any NetworkBehaviours.
	/// Renderer.enabled remains owned by components such as DontRenderIfOwner.
	///
	/// An avatar stands in for a person in the room, so another player's is drawn only when
	/// it lands on them: both headsets in the same frame. That is this peer's view alone,
	/// so colliders follow the shared spatial presence instead.
	/// </summary>
	[RequireComponent(typeof(PlayerAvatar))]
	public class AvatarPresentation : MonoBehaviour
	{
		private PlayerAvatar avatar;
		private Renderer[] renderers;
		private Collider[] colliders;
		private bool[] colliderEnabled;
		private ParticleSystem[] particles;
		private AudioSource[] audioSources;
		private bool[] audioMuted;

		public bool IsShown { get; private set; }

		private void Awake()
		{
			avatar = GetComponent<PlayerAvatar>();
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

		private void Refresh() => RefreshFor(PlayerHeadsetStatus.LocalViewAligned);

		private void RefreshFor(bool viewerAligned)
		{
			bool present = avatar.HasSpatialPresence;
			bool wasShown = IsShown;
			IsShown = present && (avatar.IsOwner || viewerAligned);

			foreach (Renderer renderer in renderers)
				renderer.forceRenderingOff = !IsShown || (!avatar.IsAlive && renderer is not ParticleSystemRenderer);
			for (int i = 0; i < colliders.Length; i++)
				colliders[i].enabled = colliderEnabled[i] && present && avatar.IsAlive;
			for (int i = 0; i < audioSources.Length; i++)
				audioSources[i].mute = audioMuted[i] || !IsShown;
			if (wasShown && !IsShown)
			{
				foreach (ParticleSystem particle in particles)
					particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
				foreach (AudioSource source in audioSources) source.Stop();
			}
		}
	}
}
