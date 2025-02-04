using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

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
		private const string LocalAnchorsFilename = "anchors.json";

		[Serializable]
		private struct LocalAnchors
		{
			public Guid[] anchorGuids;
		}

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

			await DownloadAndLocalizeAsync(Uuid.Value.guid);
		}

		public override async void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				OriginalPoseSync.Value = new NetworkPose(transform);
				await CreateAndShareAsync();
			}
			else
			{
				if (Uuid.Value.guid == Guid.Empty)
					return;

				await LoadAndLocalizeAsync(new[]{ Uuid.Value.guid});
			}
		}

		private async Task CreateAndShareAsync()
		{
			Debug.Log("Reading local anchor uuids...");
			GameSave.ReadFile(LocalAnchorsFilename, out LocalAnchors localAnchors);

			
			if (localAnchors.anchorGuids?.Length > 0)
			{
				Debug.Log("Trying to load anchors...");
				try
				{
					await LoadAndLocalizeAsync(localAnchors.anchorGuids);
					return;

				}
				catch (Exception)
				{
					Debug.Log("Could not find any local anchors\nCreating and uploading...");
				}
			}

			spatialAnchor.enabled = true;
			
			bool localizeSuccess = await spatialAnchor.WhenLocalizedAsync();
			ThrowExceptionIfBehaviorDisabled();
			if (!localizeSuccess)
				throw new Exception($"Failed to localize anchor {spatialAnchor.Uuid}");
			
			Debug.Log($"Uploading anchor {spatialAnchor.Uuid}...");

			var saveResult = await spatialAnchor.SaveAnchorAsync();
			ThrowExceptionIfBehaviorDisabled();
			if (!saveResult.Success)
				throw new Exception($"Failed to upload anchor {spatialAnchor.Uuid}");
			
			Debug.Log($"Sharing anchor {spatialAnchor.Uuid}...");

			var shareResult = await spatialAnchor.ShareAsync(spatialAnchor.Uuid);
			ThrowExceptionIfBehaviorDisabled();
			if (shareResult.Success)
				Debug.Log($"Successfully saved anchor {spatialAnchor.Uuid}");
			else
				throw new Exception($"Failed to save anchor {spatialAnchor.Uuid}: {shareResult.ToString()}");

			if(IsOwner)
				Uuid.Value = new(spatialAnchor.Uuid);
		}

		private async Task DownloadAndLocalizeAsync(Guid uuid)
		 => await LoadAndLocalizeAsync(new[] { uuid });

		private async Task LoadAndLocalizeAsync(Guid[] uuids)
		{
			List<OVRSpatialAnchor.UnboundAnchor> loadedAnchors = new();

			OVRSpatialAnchor.UnboundAnchor unboundAnchor = default;

			Debug.Log($"Checking if anchors {uuids} is saved locally...");
			
			var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, loadedAnchors);
			ThrowExceptionIfBehaviorDisabled();
			if (!loadResult.Success || loadResult.Value.Count == 0)
			{
				Debug.Log($"Did not find anchors {uuids} saved locally. Downloading...");

				var downloadResult = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(uuids, loadedAnchors);
				ThrowExceptionIfBehaviorDisabled();
				if (downloadResult.Success)
					throw new Exception($"Failed to download anchor {uuids}");
			}

			unboundAnchor = loadedAnchors[0];

			Debug.Log($"Attempting to localize and bind {unboundAnchor.Uuid}...");

			var localizeSuccess = await unboundAnchor.LocalizeAsync(5);
			ThrowExceptionIfBehaviorDisabled();
			if (!localizeSuccess)
				throw new Exception($"Couldn't localize anchor {uuids}");

			unboundAnchor.BindTo(spatialAnchor);
			spatialAnchor.enabled = true;

			if(IsOwner)
				Uuid.Value = new(spatialAnchor.Uuid);
		}

		private void ThrowExceptionIfBehaviorDisabled()
		{
			if (!enabled)
				throw new Exception("Behavior disabled during async operation");
		}
	}
}