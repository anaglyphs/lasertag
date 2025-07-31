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
			MatchManager.MatchStateChanged += HandleStateChange;
		}

		private void OnDestroy()
		{
			MatchManager.MatchStateChanged -= HandleStateChange;
		}

		private void HandleStateChange(MatchState old, MatchState state)
		{
			switch(state)
			{
				case MatchState.NotPlaying:
					if (old == MatchState.Playing)
						audioSource.PlayOneShot(finish);
					break;

				case MatchState.Queued:
					audioSource.PlayOneShot(queue);
					break;

				case MatchState.Countdown:
					audioSource.PlayOneShot(countdown);
					break;

				case MatchState.Playing:
					audioSource.PlayOneShot(siren);
					break;
			}
		}
	}
}
