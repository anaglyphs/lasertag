using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
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

		private void Log(string str) => Debug.Log($"[NetworkedAnchor] {str}");

		[Serializable]
		private struct GuidSave
		{
			public List<Guid> guids;
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

		private void OnGuidChanged(NetworkGuid previous, NetworkGuid current)
		{
			if (IsOwner || Uuid.Value.guid == Guid.Empty)
				return;

			Load(Uuid.Value.guid);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				CreateAndShare();
			}
			else
			{
				if (Uuid.Value.guid == Guid.Empty)
					return;

				Load(Uuid.Value.guid);
			}
		}

		private async void CreateAndShare()
		{
			try
			{
				Log("Checking for local anchors");
				GuidSave save = GetGuidSave();
				List<OVRSpatialAnchor.UnboundAnchor> loadedAnchors = new();
				if (save.guids != null)
				{
					var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(save.guids, loadedAnchors);
					ThrowExceptionIfBehaviorDisabled();
					if (!loadResult.Success)
						throw new Exception($"Could not load achors");
				}

				bool foundSavedAnchor = loadedAnchors.Count > 0;
				if (foundSavedAnchor)
				{
					Log("Found locally saved anchor");

					OVRSpatialAnchor.UnboundAnchor unboundAnchor = loadedAnchors[0];
					Log($"Loaded unbound anchor {unboundAnchor.Uuid}");

					var localizeSuccess = await unboundAnchor.LocalizeAsync();
					ThrowExceptionIfBehaviorDisabled();
					if (!localizeSuccess)
						throw new Exception($"Could not localize anchor {unboundAnchor.Uuid}");

					unboundAnchor.BindTo(spatialAnchor);
					unboundAnchor.TryGetPose(out Pose anchorPose);
					OriginalPoseSync.Value = new(anchorPose);
					Uuid.Value = new(spatialAnchor.Uuid);

					spatialAnchor.enabled = true;
				}
				else
				{
					Log("Couldn't find locally saved anchor.\nCreating and uploading a new one");

					spatialAnchor.enabled = true;

					bool localizeSuccess = await spatialAnchor.WhenLocalizedAsync();
					ThrowExceptionIfBehaviorDisabled();
					if (!localizeSuccess)
						throw new Exception($"Failed to localize anchor {spatialAnchor.Uuid}");

					Log($"Saving anchor {spatialAnchor.Uuid}...");

					var saveResult = await spatialAnchor.SaveAnchorAsync();
					ThrowExceptionIfBehaviorDisabled();
					if (!saveResult.Success)
						throw new Exception($"Failed to save anchor {spatialAnchor.Uuid}");

					AddSavedAnchorToGuidSave(spatialAnchor.Uuid);

					Log($"Sharing anchor {spatialAnchor.Uuid}...");

					var shareResult = await spatialAnchor.ShareAsync(spatialAnchor.Uuid);
					ThrowExceptionIfBehaviorDisabled();
					if (shareResult.Success)
						Log($"Successfully saved anchor {spatialAnchor.Uuid}");
					else
						throw new Exception($"Failed to save anchor {spatialAnchor.Uuid}: {shareResult.ToString()}");

					OriginalPoseSync.Value = new NetworkPose(transform);
					Uuid.Value = new(spatialAnchor.Uuid);
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				Log("Trying again in five seconds");
				await Awaitable.WaitForSecondsAsync(5);
				CreateAndShare();
			}
		}

		private GuidSave GetGuidSave()
		{
			Log($"Reading saved anchor uuids...");
			GameSave.ReadFile(LocalAnchorsFilename, out GuidSave localAnchors);
			return localAnchors;
		}

		private void AddSavedAnchorToGuidSave(Guid uuid)
		{
			Log($"Saving anchor uuid to file...");
			GuidSave localAnchors = GetGuidSave();
			if (localAnchors.guids != null && !localAnchors.guids.Contains(spatialAnchor.Uuid))
				localAnchors.guids.Add(spatialAnchor.Uuid);
			GameSave.WriteFile(LocalAnchorsFilename, localAnchors);
		}

		private async void Load(Guid uuid)
		{
			try
			{
				List<OVRSpatialAnchor.UnboundAnchor> loadedAnchors = new();

				OVRSpatialAnchor.UnboundAnchor unboundAnchor = default;

				Log($"Checking if anchor {uuid} is saved locally...");

				var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, loadedAnchors);
				ThrowExceptionIfBehaviorDisabled();
				if (!loadResult.Success)
				{
					throw new Exception($"Failed to load anchor {uuid}");
				}
				else if (loadResult.Value.Count == 0)
				{
					Log($"Did not find anchors {uuid} saved locally. Downloading...");

					var downloadResult = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(uuid, loadedAnchors);
					ThrowExceptionIfBehaviorDisabled();
					if (downloadResult.Success)
						throw new Exception($"Failed to download anchor {uuid}");
				}

				unboundAnchor = loadedAnchors[0];

				Log($"Attempting to localize and bind {unboundAnchor.Uuid}...");

				var localizeSuccess = await unboundAnchor.LocalizeAsync(5);
				ThrowExceptionIfBehaviorDisabled();
				if (!localizeSuccess)
					throw new Exception($"Couldn't localize anchor {uuid}");

				unboundAnchor.BindTo(spatialAnchor);
				spatialAnchor.enabled = true;
			} catch (Exception e)
			{
				Debug.LogException(e);
				Log("Trying again in five seconds...");
				await Awaitable.WaitForSecondsAsync(5);
				Load(uuid);
			}
		}

		private void ThrowExceptionIfBehaviorDisabled()
		{
			if (!enabled)
				throw new Exception("Behavior disabled during async operation");
		}
	}
}