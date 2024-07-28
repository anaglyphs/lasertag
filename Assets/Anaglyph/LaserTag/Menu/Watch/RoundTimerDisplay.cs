using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class RoundTimerDisplay : MonoBehaviour
    {
        [SerializeField] private Text text;

		private void OnValidate()
		{
			this.SetComponent(ref text);
		}

		private void Update()
		{
			float time = 0;

			if (RoundManager.Instance.RoundState == RoundState.Playing && RoundManager.Instance.ActiveSettings.CheckWinByTimer())
			{
				time = RoundManager.Instance.TimeRoundEnds - (float)NetworkManager.Singleton.LocalTime.Time;
			}

			text.text = TimeSpan.FromSeconds(time).ToString(@"mm\:ss");
		}
	}
}
