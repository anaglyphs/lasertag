using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag.UI
{
	public class DeathHUD : MonoBehaviour
	{
		public static DeathHUD Instance { get; private set; }

		[Header("Death Popup")]
		[SerializeField] private Text respawnText = null;

		[SerializeField] private GameObject respawnPopup = null;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			MainPlayer.Instance.Died += OnDied;
			MainPlayer.Instance.Respawned += OnRespawned;
		}

		private void OnDied()
		{
			gameObject.SetActive(true);
		}

		private void OnRespawned()
		{
			gameObject.SetActive(false);
		}

		// https://easings.net/#easeInOutCirc
		private float EaseInOutCirc(float x)
		{
			return x < 0.5
			  ? (1 - Mathf.Sqrt(1 - Mathf.Pow(2 * x, 2))) / 2
			  : (Mathf.Sqrt(1 - Mathf.Pow(-2 * x + 2, 2)) + 1) / 2;
		}

		private void Update()
		{
			respawnPopup.SetActive(!MainPlayer.Instance.IsAlive);

			if (MatchReferee.Settings.respawnInBases && !MainPlayer.Instance.IsInFriendlyBase)
			{
				respawnText.text = $"GO TO:   BASE";
			}
			else
			{
				float timeSinceDeath = Time.time - MainPlayer.Instance.LastDeathTime;
				float timeToRespawn = MatchReferee.Settings.respawnSeconds - timeSinceDeath;
				respawnText.text = $"RESPAWN: {timeToRespawn:F1}s";
			}
		}
	}
}