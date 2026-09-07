using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.XR;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// The local player's own state - health, life and respawn rules. It knows nothing
	/// about the networked avatar; LocalAvatarMirror owns that seam and feeds this.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MainPlayer : MonoBehaviour
	{
		private const float MaxHealth = MatchSettings.MaxHealth;

		// todo move this into another component. this really doesn't belong here
		// private OVRPassthroughLayer passthroughLayer;
		public bool redDamagedVision = true;
		[SerializeField, Min(0.01f)] private float damageFlashDuration = 0.25f;
		[SerializeField, Range(0f, 1f)] private float damageFlashStrength = 0.75f;
		private float damageFlash;

		public static MainPlayer Instance { get; private set; }

		public float Health { get; private set; } = MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; }
		public byte Team { get; private set; }

		/// <summary>Whether the player currently has an avatar in the match.</summary>
		public bool IsInPlay => hasAvatar && !IsTeamlessDuringMatch;

		/// <summary>A player who has not joined a team sits out until they do.</summary>
		public bool IsTeamlessDuringMatch =>
			Team == 0 && MatchReferee.State != MatchState.NotPlaying;

		private bool hasAvatar;

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
			{
				WeaponsManagement.CanFire = false;
				ClearPassthroughEffects();
				return;
			}

			// health
			if (redDamagedVision)
			{
				float healthNormalized = 1f - Mathf.Clamp01(Health / MaxHealth);
				float tintAmount = Mathf.Lerp(healthNormalized, 1f, damageFlash * damageFlashStrength);

				Color col = Color.red;
				col.a = tintAmount;
				
				PassthroughStylingFeature.SetStyle(
					tint: col,
					tintAmount: tintAmount,
					newEdgeTint: col
					);

				damageFlash = Mathf.MoveTowards(damageFlash, 0f,
					Time.deltaTime / Mathf.Max(0.01f, damageFlashDuration));
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
			hasAvatar = inPlay;

			if (inPlay)
				return;

			IsInFriendlyBase = false;
			WeaponsManagement.CanFire = false;
			ClearPassthroughEffects();
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
			float appliedDamage = MatchReferee.Settings.ApplyDamageMultiplier(damage);
			if (IsAlive && appliedDamage > 0f)
				damageFlash = 1f;
			Health -= appliedDamage;

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
			damageFlash = 0f;
			PassthroughStylingFeature.ClearStyle();
		}
	}
}
