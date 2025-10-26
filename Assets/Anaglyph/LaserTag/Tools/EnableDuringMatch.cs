using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class EnableDuringMatch : MonoBehaviour
	{
		public Behaviour[] behaviours;
		public bool invert;

		private MatchReferee referee => MatchReferee.Instance;

		private void OnEnable()
		{
			if (didStart)
				HandleChange();
		}

		private void Start()
		{
			MatchReferee.StateChanged += OnMatchStateChanged;

			HandleChange();
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
		}

		private void OnMatchStateChanged(MatchState current)
		{
			HandleChange();
		}

		private void HandleChange()
		{
			bool isPlaying = MatchReferee.State == MatchState.Playing;
			foreach (Behaviour behaviour in behaviours)
			{
				behaviour.enabled = isPlaying ^ invert;
			}
		}
	}
}