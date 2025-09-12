//using Anaglyph.Lasertag.Networking;
//using Unity.Netcode;
//using UnityEngine;

//namespace Anaglyph.Lasertag
//{
//	public class Flag : NetworkBehaviour
//	{
//		public const float Radius = 0.1f;

//		[SerializeField] private TeamOwner teamOwner;
//		public byte Team => teamOwner.Team;

//		public NetworkVariable<ulong> flagHolderID = new(ulong.MaxValue);
//		public ulong FlagHolderPlayerID => flagHolderID.Value;

//		public PlayerAvatar FlagHoldingPlayer { get; private set; }

//		[SerializeField] private Transform flagVisualTransform;

//		private void OnValidate()
//		{
//			TryGetComponent(out teamOwner);
//		}

//		private void Start()
//		{
//			MatchReferee.Instance.StateChanged += OnMatchStateChanged;

//			flagHolderID.OnValueChanged += OnFlagHolderIdChanged;
//		}

//		public override void OnDestroy()
//		{
//			base.OnDestroy();

//			if(MatchReferee.Instance != null)
//				MatchReferee.Instance.StateChanged -= OnMatchStateChanged;
//		}

//		private void OnFlagHolderIdChanged(ulong prev, ulong id)
//		{
//			if(PlayerAvatar.All.TryGetValue(id, out var player))
//			{
//				FlagHoldingPlayer = player;
//			} else
//			{
//				FlagHoldingPlayer = null;
//			}
//		}

//		private void OnMatchStateChanged(MatchState state)
//		{
//			if (IsOwner)
//			{
//				DropFlagRpc();
//			}
//		}

//		private void Update()
//		{
//			var player = MainPlayer.Instance.Avatar;

//			if (player == null)
//				return;

//			if (FlagHoldingPlayer != null)
//			{
//				if (player.IsInFriendlyBase && player == FlagHoldingPlayer)
//				{
//					CaptureFlagRpc(player.NetworkObjectId);
//					player.onKilled.AddListener(DropFlagRpc);
//				}

//				flagVisualTransform.transform.position = FlagHoldingPlayer.HeadTransform.position;
//				Vector3 camLook = (player.HeadTransform.position - flagVisualTransform.position).normalized;
//				flagVisualTransform.transform.rotation = Quaternion.LookRotation(camLook, Vector3.up);
//			}
//			else
//			{
//				Vector3 playerHeadPos = player.HeadTransform.position;
//				bool inside = Geo.PointIsInCylinder(transform.position, Radius, 3, playerHeadPos);

//				if (inside)
//				{
//					PickupFlagRpc(player.OwnerClientId);
//				}
//			}
//		}

//		[Rpc(SendTo.Owner)]
//		private void PickupFlagRpc(ulong id)
//		{
//			if (FlagHoldingPlayer != null)
//				return;

//			flagHolderID.Value = id;
//		}

//		[Rpc(SendTo.Owner)]
//		private void CaptureFlagRpc(ulong id)
//		{
//			var referee = MatchReferee.Instance;

//			if (PlayerAvatar.All.TryGetValue(id, out var player))
//			{
//				referee.ScoreTeamRpc(player.Team, referee.Settings.pointsPerFlagCapture);
//			}

//			DropFlagRpc();
//		}

//		[Rpc(SendTo.Owner)]
//		private void DropFlagRpc()
//		{
//			FlagHoldingPlayer = null;

//			var mainAvatar = MainPlayer.Instance?.Avatar;
//			if (mainAvatar != null)
//			{
//				mainAvatar.onKilled.RemoveListener(DropFlagRpc);
//			}
//		}
//	}
//}
