using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Flag : NetworkBehaviour
	{
		[SerializeField] private float radius = 0.5f;

		public const ulong NoPlayer = ulong.MaxValue;

		[SerializeField] private TeamOwner teamOwner;
		public byte Team => teamOwner.Team;

		public PlayerAvatar FlagHolder { get; private set; }

		[SerializeField] private Transform flagVisualTransform;
		[SerializeField] private float headOffset = 0.2f;
		private Vector3 defaultFlagPosition;

		// todo: move to other script?
		[SerializeField] private AudioClip scored;
		[SerializeField] private AudioClip enemyCapturedFlag;
		[SerializeField] private AudioClip enemyStoleFlag;

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Start()
		{
			defaultFlagPosition = flagVisualTransform.localPosition;
		}

		public override void OnNetworkSpawn()
		{
			MatchReferee.Instance.StateChanged += OnMatchStateChanged;
		}

		public override void OnNetworkDespawn() {

			if (MatchReferee.Instance != null)
				MatchReferee.Instance.StateChanged -= OnMatchStateChanged;
		}

		public override void OnGainedOwnership()
		{
			if (FlagHolder == null)
				DropFlagRpc();
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
				DropFlagRpc();
		}

		private void LateUpdate()
		{
			var mainPlayer = MainPlayer.Instance?.Avatar;

			if (mainPlayer == null)
				return;

			if (FlagHolder == null)
			{
				Vector3 playerHeadPos = mainPlayer.HeadTransform.position;
				bool isInside = Geo.PointIsInCylinder(transform.position, radius, 3, playerHeadPos);
				bool isOtherTeam = teamOwner.Team != mainPlayer.Team;

				if (isInside && isOtherTeam && mainPlayer.IsAlive)
					PickupFlagLocal();

				flagVisualTransform.transform.localPosition = defaultFlagPosition;
			}
			else
			{
				flagVisualTransform.transform.position = FlagHolder.HeadTransform.position + Vector3.up * headOffset;

				if (FlagHolder == mainPlayer && mainPlayer.IsInFriendlyBase && mainPlayer.IsAlive)
				{
					var referee = MatchReferee.Instance;
					referee.ScoreTeamRpc(mainPlayer.Team, referee.Settings.pointsPerFlagCapture);
					FlagCapturedRpc(NetworkManager.Singleton.LocalClientId);
				}
			}

			Vector3 camLook = (flagVisualTransform.position - mainPlayer.HeadTransform.position).normalized;
			flagVisualTransform.transform.rotation = Quaternion.LookRotation(camLook, Vector3.up);
		}

		private void PickupFlagLocal()
		{
			var mainPlayer = MainPlayer.Instance?.Avatar;
			mainPlayer.onKilled.AddListener(DropFlagLocal);
			FlagHolder = mainPlayer;
			PickupFlagRpc(mainPlayer.OwnerClientId);
		}

		[Rpc(SendTo.Everyone)]
		private void PickupFlagRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			FlagHolder = player;

			if (MainPlayer.Instance.Avatar?.Team != FlagHolder.Team)
				AudioSource.PlayClipAtPoint(enemyStoleFlag, transform.position);
		}

		private void DropFlagLocal()
		{
			var mainPlayer = MainPlayer.Instance?.Avatar;
			mainPlayer.onKilled.RemoveListener(DropFlagLocal);
			DropFlagRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void DropFlagRpc()
		{
			FlagHolder = null;
		}

		[Rpc(SendTo.Everyone)]
		private void FlagCapturedRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			var capturePosition = player.HeadTransform.position;

			if(MainPlayer.Instance.Avatar?.Team == player.Team)
				AudioSource.PlayClipAtPoint(scored, capturePosition);
			else
				AudioSource.PlayClipAtPoint(enemyCapturedFlag, capturePosition);

			FlagHolder = null;
		}
	}
}
