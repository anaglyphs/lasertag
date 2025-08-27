using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class RefereeSounds : MonoBehaviour
    {
		private AudioSource audioSource;

		[SerializeField] private MatchReferee matchManager;

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
			matchManager.StateChanged += HandleStateChange;
		}

		private void OnDestroy()
		{
			matchManager.StateChanged -= HandleStateChange;
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
