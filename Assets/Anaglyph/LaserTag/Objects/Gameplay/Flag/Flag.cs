using System;
using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Flag : NetworkBehaviour
	{
		[SerializeField] private TeamOwner teamOwner;
		public byte Team => teamOwner.Team;

		public event Action<PlayerAvatar> Taken = delegate { };
		public event Action<PlayerAvatar> Captured = delegate { };

		private NetworkVariable<NetworkBehaviourReference> holderSync = new();
		public PlayerAvatar Holder { get; private set; }

		[SerializeField] private Transform visual;

		[SerializeField] private Vector3 heldOffsetGlobal = new(0, -1, 0);
		[SerializeField] private Vector3 heldOffsetHeadRelative = new(0, 0, -0.2f);

		private Vector3 visualRestPos;

		public const string Tag = "Flag";

		private void Awake()
		{
			gameObject.tag = Tag;
		}

		private void Start()
		{
			visualRestPos = visual.localPosition;
		}

		public override void OnNetworkSpawn()
		{
			MainPlayer.Died += OnDied;
			MatchReferee.StateChanged += OnMatchStateChanged;

			holderSync.OnValueChanged += OnHolderSyncChanged;
			ResolveHolder();
		}

		public override void OnNetworkDespawn()
		{
			MainPlayer.Died -= OnDied;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			holderSync.OnValueChanged -= OnHolderSyncChanged;

			if (Holder == PlayerAvatar.Local)
				RequestDropRpc();
		}

		private void OnHolderSyncChanged(NetworkBehaviourReference previous, NetworkBehaviourReference current)
		{
			ResolveHolder();

			if (Holder != null)
				Taken.Invoke(Holder);
		}

		private void ResolveHolder()
		{
			holderSync.Value.TryGet(out PlayerAvatar holder);
			Holder = holder;
		}

		public override void OnGainedOwnership()
		{
			if (Holder)
				RequestDropRpc();
		}

		private void OnDied()
		{
			if (Holder == PlayerAvatar.Local)
				RequestDropRpc();
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
				RequestDropRpc();
		}

		private void Update()
		{
			if (!PlayerAvatar.Local)
				return;

			if (Holder == PlayerAvatar.Local && PlayerAvatar.Local.IsInFriendlyBase && PlayerAvatar.Local.IsAlive)
			{
				MatchReferee referee = MatchReferee.Current;
				referee.Score(PlayerAvatar.Local.Team, MatchReferee.Settings.pointsPerFlagCapture);
				RequestCaptureRpc(NetworkManager.LocalClientId);
			}
		}

		private void OnTriggerStay(Collider other)
		{
			if (Holder != null || !other.CompareTag(PlayerAvatar.Tag))
				return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player == null || player != PlayerAvatar.Local)
				return;

			bool isAlive = player.IsAlive;
			bool hasTeam = player.Team != 0;
			bool isOtherTeam = teamOwner.Team != player.Team;
			bool isInBase = player.IsInBase; // prevent crazy flag take & score loop if flag is in base lol

			if (isAlive && hasTeam && isOtherTeam && !isInBase)
				RequestTakeRpc(NetworkManager.LocalClientId);
		}

		private void LateUpdate()
		{
			if (Holder)
			{
				Vector3 heldPosHeadRelative = Holder.HeadTransform.TransformPoint(heldOffsetHeadRelative);
				visual.position = heldPosHeadRelative + heldOffsetGlobal;

				Vector3 headForw = Holder.HeadTransform.forward;
				Vector3 headForwFlat = new Vector3(headForw.x, 0, headForw.z).normalized;

				visual.rotation = Quaternion.LookRotation(headForwFlat, Vector3.up);
			}
			else
			{
				visual.localPosition = visualRestPos;
			}
		}

		// These are SendTo.Server (not SendTo.Everyone) so holderSync has a single
		// writer. Two clients racing to take the flag now resolve deterministically
		// by server processing order instead of each client evaluating its own copy
		// of the take/drop logic and potentially disagreeing on the result.

		[Rpc(SendTo.Owner)]
		private void RequestTakeRpc(ulong id)
		{
			if (holderSync.Value.TryGet(out PlayerAvatar _))
				return; // already claimed - whichever request the server processes first wins

			if (!PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				return;

			holderSync.Value = new NetworkBehaviourReference(player);
		}

		[Rpc(SendTo.Owner)]
		private void RequestDropRpc()
		{
			holderSync.Value = default;
		}

		[Rpc(SendTo.Owner)]
		private void RequestCaptureRpc(ulong id)
		{
			if (!PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				return;

			holderSync.Value = default;

			AnnounceCapturedRpc(id);
		}

		[Rpc(SendTo.Everyone)]
		private void AnnounceCapturedRpc(ulong id)
		{
			if (PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				Captured.Invoke(player);
		}
	}
}