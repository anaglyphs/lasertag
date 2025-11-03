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

		[SerializeField] private RectTransform menuMaskRectTransform = null;

		private float maxMenuMaskHeight = 0;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			maxMenuMaskHeight = menuMaskRectTransform.sizeDelta.y;
			Player.Instance.Died += OnDied;
			Player.Instance.Respawned += OnRespawned;
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
			menuMaskRectTransform.sizeDelta = new Vector2(menuMaskRectTransform.sizeDelta.x, Mathf.Lerp(0, maxMenuMaskHeight, EaseInOutCirc(Mathf.Clamp01(Player.Instance.RespawnTimerSeconds))));

			respawnPopup.SetActive(!Player.Instance.IsAlive);

			if (MatchReferee.Settings.respawnInBases && !Player.Instance.IsInFriendlyBase)
			{
				respawnText.text = $"GO TO:   BASE";
			}
			else
			{
				respawnText.text = $"RESPAWN: {(Player.Instance.RespawnTimerSeconds).ToString("F1")}s";
			}
		}
	}
}