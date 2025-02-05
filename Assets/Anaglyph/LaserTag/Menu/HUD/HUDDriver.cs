using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag.UI
{
	public class HUDDriver : SingletonBehavior<HUDDriver>
	{
		[Header("Death Popup")]
		[SerializeField] private Text respawnText = null;

		[SerializeField] private GameObject respawnPopup = null;

		[SerializeField] private RectTransform menuMaskRectTransform = null;

		private float maxMenuMaskHeight = 0;

		void Start()
		{
			maxMenuMaskHeight = menuMaskRectTransform.sizeDelta.y;
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
		}

		protected override void OnSingletonDestroy()
		{
			
		}

		protected override void SingletonAwake()
		{
			
		}
	}
}