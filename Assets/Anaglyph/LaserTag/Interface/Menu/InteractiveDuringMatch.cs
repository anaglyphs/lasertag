using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class InteractiveDuringMatch : MonoBehaviour
    {
		private Selectable selectable;
		public bool invert;

		private void Awake()
		{
			selectable = GetComponent<Selectable>();
			MatchManager.MatchStateChanged += OnMatchStateChanged;
		}

		private void OnEnable()
		{
			if(didStart)
				HandleChange();
		}

		private void Start()
		{
			HandleChange();
		}

		private void OnDestroy()
		{
			MatchManager.MatchStateChanged -= OnMatchStateChanged;
		}

		private void OnMatchStateChanged(MatchState prev, MatchState current)
		{
			HandleChange();
		}

		private void HandleChange()
		{
			bool isPlaying = MatchManager.MatchState == MatchState.Playing;
			selectable.interactable = isPlaying ^ invert;
		}
	}
}
