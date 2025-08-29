using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class InteractiveDuringMatch : MonoBehaviour
    {
		private Selectable selectable;
		public bool invert;

		private void OnEnable()
		{
			if(didStart)
				HandleChange();
		}

		private void Start()
		{
			selectable = GetComponent<Selectable>();
			MatchReferee.Instance.StateChanged += OnMatchStateChanged;

			HandleChange();
		}

		private void OnDestroy()
		{
			MatchReferee.Instance.StateChanged -= OnMatchStateChanged;
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
