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

		public NetworkVariable<ulong> flagHolderID = new(NoPlayer);
		public ulong FlagHolderPlayerID => flagHolderID.Value;

		public PlayerAvatar FlagHolder { get; private set; }

		[SerializeField] private Transform flagVisualTransform;
		[SerializeField] private float headOffset = 0.2f;
		private Vector3 defaultFlagPosition;

		// todo: move to other script?
		[SerializeField] private AudioSource audioSource;
		[SerializeField] private AudioClip scored;
		[SerializeField] private AudioClip enemyCapturedFlag;
		[SerializeField] private AudioClip enemyStoleFlag;

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Start()
		{
			MatchReferee.Instance.StateChanged += OnMatchStateChanged;

			flagHolderID.OnValueChanged += OnFlagHolderIdChanged;

			defaultFlagPosition = flagVisualTransform.localPosition;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			if (MatchReferee.Instance != null)
				MatchReferee.Instance.StateChanged -= OnMatchStateChanged;
		}

		private void OnFlagHolderIdChanged(ulong prev, ulong id)
		{
			if (PlayerAvatar.All.TryGetValue(id, out var player))
			{
				FlagHolder = player;

				if(player.Team != MainPlayer.Instance?.Avatar?.Team)
				{
					audioSource.PlayOneShot(enemyStoleFlag);
				}
			}
			else
			{
				FlagHolder = null;
			}
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

				if (isInside && isOtherTeam)
					PickupFlag();

				flagVisualTransform.transform.localPosition = defaultFlagPosition;
			}
			else
			{
				flagVisualTransform.transform.position = FlagHolder.HeadTransform.position + Vector3.up * headOffset;

				if (FlagHolder == mainPlayer && mainPlayer.IsInFriendlyBase)
				{
					var referee = MatchReferee.Instance;
					referee.ScoreTeamRpc(mainPlayer.Team, referee.Settings.pointsPerFlagCapture);
					FlagCapturedRpc(mainPlayer.Team);
					DropFlag();
				}
			}

			Vector3 camLook = (flagVisualTransform.position - mainPlayer.HeadTransform.position).normalized;
			flagVisualTransform.transform.rotation = Quaternion.LookRotation(camLook, Vector3.up);
		}

		private void PickupFlag()
		{
			var mainPlayer = MainPlayer.Instance?.Avatar;
			mainPlayer.onKilled.AddListener(DropFlag);
			FlagHolder = mainPlayer;
			// flagVisualTransform.gameObject.SetActive(false);
			PickupFlagRpc(mainPlayer.OwnerClientId);
		}

		[Rpc(SendTo.Owner)]
		private void PickupFlagRpc(ulong id)
		{
			flagHolderID.Value = id;
		}

		private void DropFlag()
		{
			var mainPlayer = MainPlayer.Instance.Avatar;
			mainPlayer.onKilled.RemoveListener(DropFlag);
			FlagHolder = null;
			// flagVisualTransform.gameObject.SetActive(true);
			DropFlagRpc();
		}

		[Rpc(SendTo.Owner)]
		private void DropFlagRpc()
		{
			flagHolderID.Value = NoPlayer;
		}

		[Rpc(SendTo.Everyone)]
		private void FlagCapturedRpc(byte team)
		{
			if(MainPlayer.Instance.Avatar?.Team == team)
				audioSource.PlayOneShot(scored);
			else
				audioSource.PlayOneShot(enemyCapturedFlag);
		}
	}
}
