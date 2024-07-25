using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class RoundTimer : MonoBehaviour
    {
        [SerializeField] private Text text;

		private void OnValidate()
		{
			this.SetComponent(ref text);
		}

		private void Update()
		{
			if (RoundManager.Instance.RoundState == RoundState.Playing && RoundManager.Instance.ActiveGameSettings.CheckWinByTimer())
			{
				TimeSpan time = TimeSpan.FromSeconds(RoundManager.Instance.TimeGameEnds - NetworkManager.Singleton.LocalTime.Time);
				text.text = time.ToString(@"mm\:ss");
			} else {
				text.text = "";
			}
		}
	}
}
