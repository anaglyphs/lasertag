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
		}

		public override void OnNetworkDespawn() 
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
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
					PickupLocal();

				flagVisualTransform.transform.localPosition = defaultFlagPosition;
			}
			else
			{
				flagVisualTransform.transform.position = FlagHolder.HeadTransform.position + Vector3.up * headOffset;

				if (FlagHolder == PlayerAvatar.Local && PlayerAvatar.Local.IsInFriendlyBase && PlayerAvatar.Local.IsAlive)
				{
					var referee = MatchReferee.Instance;
					referee.ScoreTeamRpc(PlayerAvatar.Local.Team, referee.Settings.pointsPerFlagCapture);
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

		private void PickupLocal()
		{
			MainPlayer.Instance.Died += DropLocal;
			
			FlagHolder = PlayerAvatar.Local;
			PickupFlagRpc(NetworkManager.LocalClientId);
		}

		[Rpc(SendTo.Everyone)]
		private void PickupFlagRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			FlagHolder = player;
			
			PickedUp.Invoke(FlagHolder);
		}

		private void DropLocal()
		{
			MainPlayer.Instance.Died -= DropLocal;
			DropFlagRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void DropFlagRpc()
		{
			Captured.Invoke(FlagHolder);
			FlagHolder = null;
		}

		[Rpc(SendTo.Everyone)]
		private void FlagCapturedRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out var player))
				return;

			var capturePosition = player.HeadTransform.position;

			FlagHolder = null;
		}
	}
}
