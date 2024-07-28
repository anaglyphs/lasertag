using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag.UI
{
	public class HUDDriver : SingletonBehavior<HUDDriver>
	{

		[Header("Death Popup")]

		[SerializeField]
		private Text respawnText;

		[SerializeField]
		private GameObject respawnPopup;

		[SerializeField]
		private RectTransform menuMaskRectTransform;

		[Header("Game queued")]
		[SerializeField] private GameObject queuePopup;

		[Header("Round countdown")]

		[SerializeField] private GameObject countdownPopup;

		[Header("Scoreboard")]

		[SerializeField] private GameObject scoreboardPopup;

		private float maxMenuMaskHeight = 0;

		void Start()
		{
			maxMenuMaskHeight = menuMaskRectTransform.sizeDelta.y;

			countdownPopup.SetActive(false);
			scoreboardPopup.SetActive(false);
			queuePopup.SetActive(false);

			RoundManager.Instance.roundStateSync.OnValueChanged += OnRoundStateChange;
		}

		private void OnRoundStateChange(RoundState prev, RoundState state)
		{
			if(state == RoundState.Countdown)
				countdownPopup.SetActive(true);	

			if (prev == RoundState.Playing && state == RoundState.NotPlaying)
				scoreboardPopup.SetActive(true);

			queuePopup.SetActive(state == RoundState.Queued);

		}

		// https://easings.net/#easeInOutCirc
		private float EaseInOutCirc(float x)
		{
			return x < 0.5
			  ? (1 - Mathf.Sqrt(1 - Mathf.Pow(2 * x, 2))) / 2
			  : (Mathf.Sqrt(1 - Mathf.Pow(-2 * x + 2, 2)) + 1) / 2;
		}

		void Update()
		{
			//transform.position = Vector3.Lerp(transform.position, headObject.transform.position, Time.deltaTime * 5);
			//transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0, headObject.transform.eulerAngles.y, 0), Time.deltaTime * 5);

			menuMaskRectTransform.sizeDelta = new Vector2(menuMaskRectTransform.sizeDelta.x, Mathf.Lerp(0, maxMenuMaskHeight, EaseInOutCirc(Mathf.Clamp01(MainPlayer.Instance.RespawnTimerSeconds))));

			respawnPopup.SetActive(!MainPlayer.Instance.IsAlive);

			if (MainPlayer.Instance.currentRole.ReturnToBaseOnDie && !MainPlayer.Instance.IsInFriendlyBase)
			{
				respawnText.text = $"GO TO:   BASE";
			}
			else
			{
				respawnText.text = $"RESPAWN: {(MainPlayer.Instance.RespawnTimerSeconds).ToString("F1")}s";
			}

			queuePopup.SetActive(RoundManager.Instance.RoundState == RoundState.Queued);
		}

		protected override void SingletonAwake()
		{
			
		}

		protected override void OnSingletonDestroy()
		{
			RoundManager.Instance.roundStateSync.OnValueChanged -= OnRoundStateChange;
		}
	}
}