using System;
using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Flag : NetworkBehaviour
	{
		[SerializeField] private float radius = 0.5f;
		[SerializeField] private TeamOwner teamOwner;
		public byte Team => teamOwner.Team;
		
		public event Action<PlayerAvatar> PickedUp = delegate { };
		public event Action<PlayerAvatar> Captured = delegate { };

		public PlayerAvatar FlagHolder { get; private set; }

		[SerializeField] private Transform flagVisualTransform;
		[SerializeField] private float headOffset = 0.2f;
		private Vector3 defaultFlagPosition;

		private static Camera mainCamera;

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void Start()
		{
			defaultFlagPosition = flagVisualTransform.localPosition;
		}

		public override void OnNetworkSpawn()
		{
			MatchReferee.StateChanged += OnMatchStateChanged;
			NetworkManager.OnClientConnectedCallback += OnClientConnected;
			MainPlayer.Instance.Died += OnDied;
		}

		public override void OnNetworkDespawn() 
		{
			if(FlagHolder == PlayerAvatar.Local)
				DropEveryoneRpc();

			FlagHolder = null;
			
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetworkManager.OnClientConnectedCallback -= OnClientConnected;
			MainPlayer.Instance.Died -= OnDied;
		}

		private void OnDied()
		{
			if(FlagHolder == PlayerAvatar.Local)
				DropEveryoneRpc();

			FlagHolder = null;
		}

		void OnClientConnected(ulong id)
		{
			if (id == NetworkManager.LocalClientId)
				return;

			if (FlagHolder)
			{
				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				PickupFlagRpc(FlagHolder.OwnerClientId, sendTo);
			}
		}

		public override void OnGainedOwnership()
		{
			if (FlagHolder)
				DropEveryoneRpc();
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
				DropEveryoneRpc();
		}

		private void Update()
		{
			if (!PlayerAvatar.Local)
				return;
			
			if (!FlagHolder)
			{
				Vector3 playerHeadPos = MainPlayer.Instance.HeadTransform.position;
				bool isInside = Geo.PointIsInCylinder(transform.position, radius, 3, playerHeadPos);
				bool isOtherTeam = teamOwner.Team != PlayerAvatar.Local.Team;

				if (isInside && isOtherTeam && PlayerAvatar.Local.IsAlive)
					PickupFlagRpc(NetworkManager.LocalClientId);

				flagVisualTransform.transform.localPosition = defaultFlagPosition;
			}
			else
			{
				flagVisualTransform.transform.position = FlagHolder.HeadTransform.position + Vector3.up * headOffset;

				if (FlagHolder == PlayerAvatar.Local && PlayerAvatar.Local.IsInFriendlyBase && PlayerAvatar.Local.IsAlive)
				{
					var referee = MatchReferee.Instance;
					referee.TeamScoredRpc(PlayerAvatar.Local.Team, MatchReferee.Settings.pointsPerFlagCapture);
					FlagCapturedRpc(NetworkManager.Singleton.LocalClientId);
				}
			}
		}

		private void LateUpdate()
		{
			if (!mainCamera)
				return;
			
			Vector3 camLook = (flagVisualTransform.position - mainCamera.transform.position).normalized;
			flagVisualTransform.transform.rotation = Quaternion.LookRotation(camLook, Vector3.up);
		}

		[Rpc(SendTo.Everyone, AllowTargetOverride = true)]
		private void PickupFlagRpc(ulong id, RpcParams rpcParams = default)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			if (FlagHolder == PlayerAvatar.Local)
				return;

			FlagHolder = player;
			
			PickedUp.Invoke(FlagHolder);
		}

		[Rpc(SendTo.Everyone)]
		private void DropEveryoneRpc()
		{
			FlagHolder = null;
		}

		[Rpc(SendTo.Everyone)]
		private void FlagCapturedRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			Captured.Invoke(player);

			FlagHolder = null;
		}
	}
}
