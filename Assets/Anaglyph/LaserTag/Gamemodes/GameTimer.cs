using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-1000)]
	public class GameTimer : NetworkBehaviour
	{
		public NetworkVariable<float> endTimeSeconds = new(0);

		public float SecondsLeft { get; private set; }
		public bool GameIsOn { get; private set; }

		public void StartTimer(float lengthSeconds)
		{
			if (!IsOwner)
				return;

			endTimeSeconds.Value = Time.time + lengthSeconds;
		}

		private void Update()
		{
			SecondsLeft = endTimeSeconds.Value - Time.time;
			GameIsOn = SecondsLeft > 0;
		}
	}
}
