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
			if (local == null || !local.HasSpatialPresence)
				return false;

			// teamless players need to find a base to join one
			if (local.Team == 0)
			{
				color = Teams.Colors[homeBase.Team];
				return MatchReferee.State is MatchState.Mustering or MatchState.Playing;
			}

			// Guide assigned players home until they reach a friendly base for mustering.
			bool needsToMuster = MatchReferee.State == MatchState.Mustering && !local.IsInFriendlyBase;

			// White also stays visible through the red filter over a dead player's vision.
			return local.Team == homeBase.Team && (needsToMuster || !local.IsAlive);
		}

		private void LateUpdate()
		{
			bool show = ShouldShow(out Color color);

			if (show)
			{
				bool phase = Mathf.Repeat(Time.time * flashesPerSecond, 1f) > 0.5f;
				color.a = phase ? minFlashAlpha : 1;
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
