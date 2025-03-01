using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class RoundManagerSFX : MonoBehaviour
    {
		private AudioSource audioSource;

		[SerializeField] AudioClip queue;
		[SerializeField] AudioClip countdown;
		[SerializeField] AudioClip siren;
		[SerializeField] AudioClip finish;

		private void Awake()
		{
			TryGetComponent(out audioSource);
		}

		private void Start()
		{
			RoundManager.OnRoundStateChange += HandleStateChange;
		}

		private void OnDestroy()
		{
			RoundManager.OnRoundStateChange -= HandleStateChange;
		}

		private void HandleStateChange(RoundState old, RoundState state)
		{
			switch(state)
			{
				case RoundState.NotPlaying:
					if (old == RoundState.Playing)
						audioSource.PlayOneShot(finish);
					break;

				case RoundState.Queued:
					audioSource.PlayOneShot(queue);
					break;

				case RoundState.Countdown:
					audioSource.PlayOneShot(countdown);
					break;

				case RoundState.Playing:
					audioSource.PlayOneShot(siren);
					break;
			}
		}
	}
}
