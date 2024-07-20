using Anaglyph.LaserTag.Networking;
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public enum WinCondition : byte
	{
		MostPointsAtTimerEnd,
		ReachPointsTarget,
		AllPointsHeld,
	}

	[Serializable]
	public struct GameSettings
	{
		public bool teams;
		public bool respawnInBases;
		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;
		public WinCondition windCondition;
		public uint timerSeconds;
		public uint pointsTarget;
	}

	public class RoundManager : NetworkManager
	{
		public static RoundManager Instance { get; private set; }

		[SerializeField] private GameTimer gameTimer;
		[SerializeField] private TeamScores teamScores;
		

		public static bool GameIsOn { get; private set; }

		private void OnValidate()
		{
			this.SetComponent(ref gameTimer);
			this.SetComponent(ref teamScores);
		}

		private void Awake()
		{
			Instance = this;
		}

		public NetworkVariable<GameSettings> gameSettingsSync;
		public GameSettings GameSettings => gameSettingsSync.Value;

		public void QueueStartGame()
		{
			StartCoroutine(QueueStartGameCoroutine());
		}

		public void CancelQueuedGame()
		{

		}

		private IEnumerator QueueStartGameCoroutine()
		{
			gameQueued = true;

			if (GameSettings.respawnInBases)
			{
				GlobalMessage.Instance.Set("All players must be in a base for the round to start!");

				int numPlayersInbase = 0;
				do
				{
					numPlayersInbase = 0;

					foreach (Player player in Player.AllPlayers)
					{
						if (player.IsInBase)
							numPlayersInbase++;
					}

					yield return null;

				} while (numPlayersInbase < Player.AllPlayers.Count && gameQueued);

				GlobalMessage.Instance.Set("");
			}

			StartGame();
		}

		private void StartGame()
		{

		}
	}
}
