using Anaglyph.Lasertag;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag.UI
{
	public class HUDDriver : SingletonBehavior<HUDDriver>
	{

		[Header("Death Popup")]

		[SerializeField]
		private Text respawnText;

		[SerializeField]
		private GameObject rootHudObject;

		[SerializeField]
		private RectTransform menuMaskRectTransform;

		[Header("Round countdown")]

		[SerializeField] private GameObject countdownText;

		[Header("Scoreboard")]

		[SerializeField] private GameObject scoreboard;

		private float maxMenuMaskHeight = 0;

		void Start()
		{
			maxMenuMaskHeight = menuMaskRectTransform.sizeDelta.y;

			RoundManager.OnGameCountdownEveryone += OnRoundCountdown;
			RoundManager.OnGameEndEveryone += OnRoundEnd;

			countdownText.SetActive(false);
			scoreboard.SetActive(false);
		}

		private void OnRoundCountdown()
		{
			countdownText.SetActive(true);

			Task.Factory.StartNew(() => Thread.Sleep(5000))
			.ContinueWith((t) =>
			{
				countdownText.SetActive(false);
			}, TaskScheduler.FromCurrentSynchronizationContext());
		}

		private void OnRoundEnd()
		{
			scoreboard.SetActive(true);

			Task.Factory.StartNew(() => Thread.Sleep(3000))
			.ContinueWith((t) =>
			{
				scoreboard.SetActive(false);
			}, TaskScheduler.FromCurrentSynchronizationContext());
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

			rootHudObject.SetActive(!MainPlayer.Instance.IsAlive);

			if (MainPlayer.Instance.currentRole.ReturnToBaseOnDie && !MainPlayer.Instance.IsInFriendlyBase)
			{
				respawnText.text = $"GO TO:   BASE";
			}
			else
			{
				respawnText.text = $"RESPAWN: {(MainPlayer.Instance.RespawnTimerSeconds).ToString("F1")}s";
			}
		}

		protected override void SingletonAwake()
		{
			
		}

		protected override void OnSingletonDestroy()
		{
			RoundManager.OnGameCountdownEveryone -= OnRoundCountdown;
			RoundManager.OnGameEndEveryone -= OnRoundEnd;
		}
	}
}