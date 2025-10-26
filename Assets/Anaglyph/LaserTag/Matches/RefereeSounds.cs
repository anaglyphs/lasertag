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
			MatchReferee.StateChanged += HandleStateChange;
			MatchReferee.MatchFinished += OnMatchFinished;
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= HandleStateChange;
			MatchReferee.MatchFinished -= OnMatchFinished;
		}

		private void HandleStateChange(MatchState state)
		{
			switch (state)
			{
				case MatchState.Mustering:
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

		private void OnMatchFinished()
		{
			audioSource.PlayOneShot(finish);
		}
	}
}