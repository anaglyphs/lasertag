using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class EnableDuringMatch : MonoBehaviour
    {
		public Behaviour[] behaviours;
		public bool invert;

        private void Awake()
        {
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

		private void OnMatchStateChanged(MatchState prev, MatchState current)
		{
			HandleChange();
		}

		private void HandleChange()
		{
			bool isPlaying = MatchManager.MatchState == MatchState.Playing;
			foreach (Behaviour behaviour in behaviours)
			{
				behaviour.enabled = isPlaying ^ invert;
			}
		}
	}
}
