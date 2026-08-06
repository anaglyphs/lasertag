using System;
using Anaglyph.Lasertag.Weapons;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// The local player's own state - health, life and respawn rules. It knows nothing
	/// about the networked avatar; LocalAvatarMirror owns that seam and feeds this.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MainPlayer : MonoBehaviour
	{
		private const float MaxHealth = 100;

		// todo move this into another component. this really doesn't belong here
		// private OVRPassthroughLayer passthroughLayer;
		public bool redDamagedVision = true;

		public static MainPlayer Instance { get; private set; }

		public float Health { get; private set; } = MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; }
		public byte Team { get; private set; }

		/// <summary>Whether the player currently has an avatar in the match.</summary>
		public bool IsInPlay { get; private set; }

		public float LastDeathTime { get; private set; }

		public static event Action<ulong> Died = delegate { };
		public static event Action Respawned = delegate { };
		public static event Action Damaged = delegate { };
		public static event Action<byte> TeamChanged = delegate { };

		private void Awake()
		{
			Instance = this;

			// passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();

			MatchReferee.StateChanged += OnMatchStateChange;
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;

			if (Instance == this)
				Instance = null;
		}

		private void Update()
		{
			if (!IsInPlay)
				return;

			// health
			if (redDamagedVision)
			{
				// passthroughLayer.edgeRenderingEnabled = true;
				// Color color = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(Health / MaxHealth));
				// passthroughLayer.edgeColor = color;
			}
			else
			{
				ClearPassthroughEffects();
			}

			if (IsAlive) Health += MatchReferee.Settings.healthRegenPerSecond * Time.deltaTime;

			WeaponsManagement.CanFire = IsAlive;

			Health = Mathf.Clamp(Health, 0, MaxHealth);

			// respawn timer
			if (!IsAlive)
			{
				MatchSettings settings = MatchReferee.Settings;
				float timeSinceDeath = Time.time - LastDeathTime;
				bool timeCheck = timeSinceDeath > settings.respawnSeconds;

				// per-round deaths last until the round ends; every match state
				// change already respawns everyone (OnMatchStateChange)
				bool conditionCheck = settings.respawnCondition switch
				{
					RespawnCondition.InBases => IsInFriendlyBase,
					RespawnCondition.NextRound => MatchReferee.State != MatchState.Playing,
					_ => true,
				};

				if (timeCheck && conditionCheck)
					Respawn();
			}
		}

		private void OnMatchStateChange(MatchState state)
		{
			Respawn();
		}

		public void SetInPlay(bool inPlay)
		{
			IsInPlay = inPlay;

			if (inPlay)
				return;

			IsInFriendlyBase = false;
			WeaponsManagement.CanFire = false;
		}

		public void SetInFriendlyBase(bool inFriendlyBase)
		{
			IsInFriendlyBase = inFriendlyBase;
		}

		public void SetTeam(byte team)
		{
			if (Team == team)
				return;

			Team = team;
			TeamChanged.Invoke(team);
		}

		public void Damage(float damage, ulong damagedBy)
		{
			Damaged.Invoke();
			float mult = MatchReferee.Settings.damageMultiplier;
			if (mult == 0) mult = 1;
			Health -= damage * mult;

			if (Health <= 0)
				Kill(damagedBy);
		}

		public void Kill(ulong killerId)
		{
			if (!IsAlive) return;

			WeaponsManagement.CanFire = false;

			IsAlive = false;
			Health = 0;
			LastDeathTime = Time.time;

			Died.Invoke(killerId);
		}

		public void Respawn()
		{
			if (IsAlive) return;

			ClearPassthroughEffects();

			WeaponsManagement.CanFire = true;

			IsAlive = true;
			Health = MaxHealth;

			Respawned.Invoke();
		}

		private void ClearPassthroughEffects()
		{
			// passthroughLayer.edgeRenderingEnabled = false;
			// passthroughLayer.edgeColor = Color.clear;
		}
	}
}
