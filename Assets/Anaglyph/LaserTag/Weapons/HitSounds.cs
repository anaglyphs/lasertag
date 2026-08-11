using Anaglyph.Lasertag.Networking;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	// Plays a hit confirmation for the local player's own shots, pitched by how much health
	// the target has left. Nobody else hears it.
	[RequireComponent(typeof(AudioSource))]
	public class HitSounds : MonoBehaviour
	{
		[SerializeField] private AudioClip[] hitClips;

		[Tooltip("Pitch when the target still has full health.")]
		[SerializeField] private float pitchAtFullHealth = 0.8f;

		[Tooltip("Pitch when the shot leaves the target with nothing left.")]
		[SerializeField] private float pitchAtNoHealth = 1.6f;

		[SerializeField] private AudioSource audioSource;

		private void OnEnable() => IDamageable.DamageDealt += OnDamageDealt;

		private void OnDisable() => IDamageable.DamageDealt -= OnDamageDealt;

		private void OnDamageDealt(Vector3 position, IDamageable target, IDamageable.Data data)
		{
			if (target is not PlayerAvatar avatar)
				return;

			// already reflects this hit - the avatar drops its health locally before this fires
			// pitch lives on the source, so this retunes shots that are still ringing out
			audioSource.pitch = Mathf.Lerp(pitchAtNoHealth, pitchAtFullHealth, avatar.NormalizedHealth);
			
			int i = Random.Range(0, hitClips.Length);
			
			AudioClip clip = hitClips[i];

			audioSource.PlayOneShot(clip);
		}
	}
}
