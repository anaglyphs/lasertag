using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class InteractiveDuringMatch : MonoBehaviour
	{
		private Selectable selectable;
		public bool invert;

		private MatchReferee referee => MatchReferee.Instance;

		private void OnEnable()
		{
			if (didStart)
				HandleChange();
		}

		private void Start()
		{
			selectable = GetComponent<Selectable>();
			referee.StateChanged += OnMatchStateChanged;

			HandleChange();
		}

		private void OnDestroy()
		{
			if (referee != null)
				referee.StateChanged -= OnMatchStateChanged;
		}

		private void OnMatchStateChanged(MatchState current)
		{
			HandleChange();
		}

		private void HandleChange()
		{
			bool isPlaying = MatchReferee.Instance.State == MatchState.Playing;
			selectable.interactable = isPlaying ^ invert;
		}
	}
}