using System;
using System.Collections.Generic;
using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.Lasertag
{
	public class Flag : NetworkBehaviour
	{
		[SerializeField] private float radius = 0.5f;
		[SerializeField] private TeamOwner teamOwner;
		public byte Team => teamOwner.Team;

		public event Action<PlayerAvatar> Taken = delegate { };
		public event Action<PlayerAvatar> Captured = delegate { };

		public PlayerAvatar FlagHolder { get; private set; }

		[SerializeField] private Transform visual;

		[SerializeField] private Vector3 heldOffsetGlobal = new(0, -1, 0);
		[SerializeField] private Vector3 heldOffsetHeadRelative = new(0, 0, -0.2f);

		private Vector3 visualRestPos;

		private static Camera mainCamera;

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void Start()
		{
			visualRestPos = visual.localPosition;
		}

		public override void OnNetworkSpawn()
		{
			MainPlayer.Died += OnDied;
			MatchReferee.StateChanged += OnMatchStateChanged;
		}

		protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
		{
			ulong id = ulong.MaxValue;
			if (FlagHolder)
				id = FlagHolder.NetworkObjectId;

			serializer.SerializeValue(ref id);

			if (serializer.IsReader)
			{
				PlayerAvatar holder = null;
				Dictionary<ulong, NetworkObject> netObjs = NetworkManager.SpawnManager.SpawnedObjects;
				if (netObjs.TryGetValue(id, out NetworkObject netObj))
					holder = netObj.GetComponent<PlayerAvatar>();

				FlagHolder = holder;
			}
		}

		public override void OnNetworkDespawn()
		{
			MainPlayer.Died -= OnDied;
			MatchReferee.StateChanged -= OnMatchStateChanged;

			if (FlagHolder == PlayerAvatar.Local)
				DropRpc();
		}

		private void OnDied()
		{
			if (FlagHolder == PlayerAvatar.Local)
				DropRpc();
		}

		public override void OnGainedOwnership()
		{
			if (FlagHolder)
				DropRpc();
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
				DropRpc();
		}

		private void Update()
		{
			if (!PlayerAvatar.Local)
				return;

			if (FlagHolder)
			{
				if (FlagHolder == PlayerAvatar.Local && PlayerAvatar.Local.IsInFriendlyBase &&
				    PlayerAvatar.Local.IsAlive)
				{
					MatchReferee referee = MatchReferee.Instance;
					referee.ScoreRpc(PlayerAvatar.Local.Team, MatchReferee.Settings.pointsPerFlagCapture);
					CaptureRpc(NetworkManager.LocalClientId);
				}
			}
			else
			{
				Vector3 playerHeadPos = MainPlayer.Instance.HeadTransform.position;
				bool isInside = Geo.PointIsInCylinder(transform.position, radius, 3, playerHeadPos);
				bool isOtherTeam = teamOwner.Team != PlayerAvatar.Local.Team;

				if (PlayerAvatar.Local.IsAlive && isInside && isOtherTeam)
					TakeRpc(NetworkManager.LocalClientId);
			}
		}

		private void LateUpdate()
		{
			if (FlagHolder)
			{
				Vector3 heldPosHeadRelative = FlagHolder.HeadTransform.TransformPoint(heldOffsetHeadRelative);
				visual.position = heldPosHeadRelative + heldOffsetGlobal;

				Vector3 headForw = FlagHolder.HeadTransform.forward;
				Vector3 headForwFlat = new Vector3(headForw.x, 0, headForw.z).normalized;

				visual.rotation = Quaternion.LookRotation(headForwFlat, Vector3.up);
			}
			else
			{
				visual.localPosition = visualRestPos;
			}
		}

		[Rpc(SendTo.Everyone, AllowTargetOverride = true)]
		private void TakeRpc(ulong id, RpcParams rpcParams = default)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				return;

			if (FlagHolder == PlayerAvatar.Local)
				return;

			FlagHolder = player;

			Taken.Invoke(FlagHolder);
		}

		[Rpc(SendTo.Everyone)]
		private void DropRpc()
		{
			FlagHolder = null;
		}

		[Rpc(SendTo.Everyone)]
		private void CaptureRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				return;

			Captured.Invoke(player);

			FlagHolder = null;
		}
	}
}