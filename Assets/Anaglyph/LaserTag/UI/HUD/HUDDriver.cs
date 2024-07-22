using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag.UI
{
	public class HUDDriver : SingletonBehavior<HUDDriver>
	{
		[SerializeField]
		private Text timerTex;

		[SerializeField]
		private Text respawnText;

		[SerializeField]
		private GameObject rootHudObject;

		[SerializeField]
		private RectTransform menuMaskRectTransform;

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
			throw new System.NotImplementedException();
		}

		protected override void OnSingletonDestroy()
		{
			throw new System.NotImplementedException();
		}
	}
}