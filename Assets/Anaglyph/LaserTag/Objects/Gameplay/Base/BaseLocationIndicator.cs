using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;

namespace Anaglyph.LaserTag.Objects.Gameplay.Base
{
	[DefaultExecutionOrder(99999)]
	public class BaseLocationIndicator : MonoBehaviour
	{
		private const float flashesPerSecond = 0.8f;
		private const float minFlashAlpha = 0.5f;

		private Camera mainCamera;
		private Base homeBase;
		private SpriteRenderer[] sprites;

		private void Awake()
		{
			homeBase = GetComponentInParent<Base>();
			sprites = GetComponentsInChildren<SpriteRenderer>(true);
		}

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		// Polled rather than event-driven because the local player can spawn, change
		// team, and die at any time, and the match state changes independently.
		private bool ShouldShow(out Color color)
		{
			color = Color.white;

			PlayerAvatar local = PlayerAvatar.Local;
			if (local == null)
				return false;

			// teamless players need to find a base to join one
			if (local.Team == 0)
			{
				color = Teams.Colors[homeBase.Team];
				return MatchReferee.State is MatchState.Mustering or MatchState.Playing;
			}

			// white stays visible through the red filter over a dead player's vision
			return local.Team == homeBase.Team && !local.IsAlive;
		}

		private void LateUpdate()
		{
			bool show = ShouldShow(out Color color);

			if (show)
			{
				float flash = Mathf.PingPong(Time.time * 2 * flashesPerSecond, 1);
				color.a = Mathf.Lerp(minFlashAlpha, 1, flash);
			}

			foreach (SpriteRenderer sprite in sprites)
			{
				sprite.enabled = show;
				sprite.color = color;
			}

			if (show && mainCamera != null)
				transform.LookAt(mainCamera.transform);
		}
	}
}
