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

		private readonly NetworkVariable<NetworkBehaviourReference> holderSync = new();
		// Changes every time the flag is successfully picked up. Capture requests
		// carry this value so a late/repeated request cannot capture a later carry.
		private readonly NetworkVariable<uint> carryEpochSync = new();
		public PlayerAvatar Holder { get; private set; }

		// This only prevents the local holder from sending the same request every
		// frame. Correctness comes from the authority-side holder/epoch checks.
		private bool captureRequested;

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
			carryEpochSync.OnValueChanged += OnCarryEpochChanged;
			ResolveHolder();
		}

		public override void OnNetworkDespawn()
		{
			MainPlayer.Died -= OnDied;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			holderSync.OnValueChanged -= OnHolderSyncChanged;
			carryEpochSync.OnValueChanged -= OnCarryEpochChanged;

			if (Holder == PlayerAvatar.Local)
				RequestDropRpc();
		}

		private void OnHolderSyncChanged(NetworkBehaviourReference previous, NetworkBehaviourReference current)
		{
			ResolveHolder();
			if (Holder != PlayerAvatar.Local)
				captureRequested = false;

			if (Holder != null)
				Taken.Invoke(Holder);
		}

		private void OnCarryEpochChanged(uint previous, uint current)
		{
			// A holder update and its epoch can arrive independently. If this client
			// tried with the previous epoch, let it retry once the current carry is known.
			captureRequested = false;
		}

		private void ResolveHolder()
		{
			holderSync.Value.TryGet(out PlayerAvatar holder);
			Holder = holder;
		}

		private void OnDied()
		{
			if (Holder == PlayerAvatar.Local)
			{
				captureRequested = false;
				RequestDropRpc();
			}
		}

		private void OnMatchStateChanged(MatchState state)
		{
			captureRequested = false;

			if (IsOwner)
				holderSync.Value = default;
		}

		private void Update()
		{
			if (!PlayerAvatar.Local)
				return;

			if (Holder == PlayerAvatar.Local && PlayerAvatar.Local.IsInFriendlyBase && PlayerAvatar.Local.IsAlive)
			{
				if (!captureRequested)
				{
					captureRequested = true;
					RequestCaptureRpc(carryEpochSync.Value);
				}
			}
			else
			{
				captureRequested = false;
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
				RequestTakeRpc();
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

		// The flag owner is the single writer for holder state. Requests are validated
		// against the RPC sender so clients cannot act on behalf of another player.

		[Rpc(SendTo.Owner)]
		private void RequestTakeRpc(RpcParams rpc = default)
		{
			if (holderSync.Value.TryGet(out PlayerAvatar _))
				return; // already claimed - whichever request the server processes first wins

			ulong sender = rpc.Receive.SenderClientId;
			if (!PlayerAvatar.All.TryGetValue(sender, out PlayerAvatar player))
				return;

			holderSync.Value = new NetworkBehaviourReference(player);
			carryEpochSync.Value++;
		}

		[Rpc(SendTo.Owner)]
		private void RequestDropRpc(RpcParams rpc = default)
		{
			if (!holderSync.Value.TryGet(out PlayerAvatar holder) ||
			    holder.OwnerClientId != rpc.Receive.SenderClientId)
				return;

			holderSync.Value = default;
		}

		[Rpc(SendTo.Owner)]
		private void RequestCaptureRpc(uint carryEpoch, RpcParams rpc = default)
		{
			ulong sender = rpc.Receive.SenderClientId;
			if (!PlayerAvatar.All.TryGetValue(sender, out PlayerAvatar player))
				return;

			if (MatchReferee.State != MatchState.Playing ||
			    carryEpoch != carryEpochSync.Value ||
			    !holderSync.Value.TryGet(out PlayerAvatar holder) ||
			    holder != player ||
			    !player.IsAlive ||
			    !player.IsInFriendlyBase ||
			    player.Team == 0 ||
			    player.Team == Team)
				return;

			// Clearing the holder before raising the score makes this transition
			// idempotent: duplicate or delayed requests fail the holder/epoch checks.
			holderSync.Value = default;

			MatchReferee.Instance.Score(player.Team, MatchReferee.Settings.pointsPerFlagCapture);
			AnnounceCapturedRpc(sender);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void AnnounceCapturedRpc(ulong id)
		{
			if (PlayerAvatar.All.TryGetValue(id, out PlayerAvatar player))
				Captured.Invoke(player);
		}
	}
}
