using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.UI
{
	public class DeathHUD : MonoBehaviour
	{
		public static DeathHUD Instance { get; private set; }

		private VisualElement deathPopup;
		private Label respawnLabel;

		private void Awake()
		{
			Instance = this;

			MainPlayer.Died += OnDied;
			MainPlayer.Respawned += OnRespawned;
		}

		private void OnDestroy()
		{
			MainPlayer.Died -= OnDied;
			MainPlayer.Respawned -= OnRespawned;
		}

		private void OnEnable()
		{
			deathPopup = HUDElement.Require<VisualElement>(this, "death-hud");
			respawnLabel = HUDElement.Require<Label>(this, "respawn-label");

			bool isDead = MainPlayer.Instance != null && !MainPlayer.Instance.IsAlive;
			HUDElement.SetDisplayed(deathPopup, isDead);
		}

		private void OnDisable()
		{
			if (deathPopup != null)
				HUDElement.SetDisplayed(deathPopup, false);
		}

		private void OnDied()
		{
			gameObject.SetActive(true);
		}

		private void OnRespawned()
		{
			gameObject.SetActive(false);
		}

		private void Update()
		{
			HUDElement.SetDisplayed(deathPopup, !MainPlayer.Instance.IsAlive);

			MatchSettings settings = MatchReferee.Settings;

			if (settings.respawnCondition == RespawnCondition.NextRound
			    && MatchReferee.State == MatchState.Playing)
			{
				respawnLabel.text = "WAIT FOR NEXT ROUND";
			}
			else if (settings.respawnCondition == RespawnCondition.InBases
			         && !MainPlayer.Instance.IsInFriendlyBase)
			{
				respawnLabel.text = $"GO TO:   BASE";
			}
			else
			{
				var timeSinceDeath = Time.time - MainPlayer.Instance.LastDeathTime;
				var timeToRespawn = settings.respawnSeconds - timeSinceDeath;
				respawnLabel.text = $"RESPAWN: {timeToRespawn:F1}s";
			}
		}
	}
}
