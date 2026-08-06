using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	/// <summary>
	/// The one place that knows both the local player and their networked avatar exist.
	/// Local state goes out onto the avatar; what the avatar learns from the network
	/// comes back in. Nothing else should reach across.
	/// </summary>
	public class LocalAvatarMirror : NetworkBehaviour
	{
		[SerializeField] private PlayerAvatar avatar;

		private MainPlayer player;

		private void OnValidate()
		{
			TryGetComponent(out avatar);
		}

		public override void OnNetworkSpawn()
		{
			if (!IsOwner)
				return;

			player = MainPlayer.Instance;

			MainPlayer.Died += OnDied;
			MainPlayer.Respawned += OnRespawned;

			avatar.Damaged += OnDamaged;
			avatar.InFriendlyBaseChanged += OnInFriendlyBaseChanged;
			avatar.TeamOwner.TeamChanged += OnTeamChanged;

			avatar.SetAlive(player.IsAlive);
			player.SetInFriendlyBase(avatar.IsInFriendlyBase);
			player.SetTeam(avatar.Team);
			player.SetInPlay(true);
		}

		public override void OnNetworkDespawn()
		{
			if (!IsOwner)
				return;

			MainPlayer.Died -= OnDied;
			MainPlayer.Respawned -= OnRespawned;

			avatar.Damaged -= OnDamaged;
			avatar.InFriendlyBaseChanged -= OnInFriendlyBaseChanged;
			avatar.TeamOwner.TeamChanged -= OnTeamChanged;

			if (player != null)
				player.SetInPlay(false);
		}

		private void OnDamaged(float damage, ulong damagedBy) => player.Damage(damage, damagedBy);
		private void OnInFriendlyBaseChanged(bool inFriendlyBase) => player.SetInFriendlyBase(inFriendlyBase);
		private void OnTeamChanged(byte team) => player.SetTeam(team);
		private void OnRespawned() => avatar.SetAlive(true);

		private void OnDied(ulong killerId)
		{
			avatar.SetAlive(false);

			if (!PlayerAvatar.All.TryGetValue(killerId, out PlayerAvatar killer))
				return;

			avatar.KilledByPlayerRpc(killerId);

			if (MatchReferee.State == MatchState.Playing && killer.Team != avatar.Team)
				MatchReferee.Instance.Score(killer.Team, MatchReferee.Settings.pointsPerKill);
		}
	}
}
