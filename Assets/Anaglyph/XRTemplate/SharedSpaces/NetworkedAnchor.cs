using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using static OVRSpatialAnchor;

namespace Anaglyph.SharedSpaces
{
	[Serializable]
	public struct NetworkGuid : INetworkSerializeByMemcpy
	{
		public NetworkGuid(Guid guid)
		{
			this.guid = guid;
		}

		public Guid guid;
	}

	[RequireComponent(typeof(OVRSpatialAnchor))]
	public class NetworkedAnchor : NetworkBehaviour, IDesiredPose
	{
		private void Log(string str) => Debug.Log($"[NetworkedAnchor] {str}");

		[SerializeField] private OVRSpatialAnchor spatialAnchor;
		public OVRSpatialAnchor Anchor => spatialAnchor;

		//private Guid serverUuid;
		public NetworkVariable<NetworkPose> OriginalPoseSync = new NetworkVariable<NetworkPose>();
		public Pose DesiredPose => OriginalPoseSync.Value;

		public NetworkVariable<NetworkGuid> Uuid = new NetworkVariable<NetworkGuid>(new(Guid.Empty));

		private void OnValidate()
		{
			TryGetComponent(out spatialAnchor);
		}

		private void Awake()
		{
			Uuid.OnValueChanged += OnGuidChanged;
			spatialAnchor.enabled = false;
		}

		private async void OnGuidChanged(NetworkGuid previous, NetworkGuid current)
		{
			if (IsOwner || Uuid.Value.guid == Guid.Empty)
				return;

			await Load(Uuid.Value.guid);
		}

		public override async void OnNetworkSpawn()
		{
			if(!IsOwner)
			{
				if (Uuid.Value.guid == Guid.Empty)
					return;

				await Load(Uuid.Value.guid);
			}
		}

		public async Task Share()
		{
			if (!IsOwner)
				throw new Exception("Only the anchor owner can share it!");

			Log("Sharing new anchor");

			spatialAnchor.enabled = true;

			bool localizeSuccess = await spatialAnchor.WhenLocalizedAsync();
			ThrowExceptionIfBehaviorDisabled();
			if (!localizeSuccess)
				throw new Exception($"Failed to localize anchor {spatialAnchor.Uuid}");
			OriginalPoseSync.Value = new NetworkPose(transform);

			Log($"Saving anchor {spatialAnchor.Uuid}...");

			var saveResult = await spatialAnchor.SaveAnchorAsync();
			ThrowExceptionIfBehaviorDisabled();
			if (!saveResult.Success)
				throw new Exception($"Failed to save anchor {spatialAnchor.Uuid}");

			AnchorGuidSaving.AddGuid(spatialAnchor.Uuid);

			Log($"Sharing anchor {spatialAnchor.Uuid}...");

			var shareResult = await spatialAnchor.ShareAsync(spatialAnchor.Uuid);
			ThrowExceptionIfBehaviorDisabled();
			if (shareResult.Success)
				Log($"Successfully saved anchor {spatialAnchor.Uuid}");
			else
				throw new Exception($"Failed to share anchor {spatialAnchor.Uuid}: {shareResult}");

			Uuid.Value = new(spatialAnchor.Uuid);
		}

		public async Task LocalizeAndBindAsync(UnboundAnchor unboundAnchor)
		{
			var localizeSuccess = await unboundAnchor.LocalizeAsync();
			ThrowExceptionIfBehaviorDisabled();
			if (!localizeSuccess)
				throw new Exception($"Could not localize anchor {unboundAnchor.Uuid}");

			unboundAnchor.BindTo(spatialAnchor);
			bool gotAnchorPose = unboundAnchor.TryGetPose(out Pose anchorPose);
			if (!gotAnchorPose)
				throw new Exception($"Couldn't get anchor {unboundAnchor.Uuid} pose");
			spatialAnchor.enabled = true;
		}

		private async Task Load(Guid uuid)
		{
			List<UnboundAnchor> loadedAnchors = new();

			UnboundAnchor unboundAnchor = default;

			Log($"Checking if anchor {uuid} is saved locally...");

			var loadResult = await LoadUnboundAnchorsAsync(new[] { uuid }, loadedAnchors);
			ThrowExceptionIfBehaviorDisabled();
			if (!loadResult.Success)
			{
				throw new Exception($"Failed to load anchor {uuid}");
			}
			else if (loadResult.Value.Count == 0)
			{
				Log($"Did not find anchors {uuid} saved locally. Downloading...");

				var downloadResult = await LoadUnboundSharedAnchorsAsync(uuid, loadedAnchors);
				ThrowExceptionIfBehaviorDisabled();
				if (downloadResult.Success)
					throw new Exception($"Failed to download anchor {uuid}: {downloadResult}");
			}

			unboundAnchor = loadedAnchors[0];

			await LocalizeAndBindAsync(unboundAnchor);
		}

		private void ThrowExceptionIfBehaviorDisabled()
		{
			if (!enabled)
				throw new TaskCanceledException("Behavior disabled during async operation");
		}
	}
}