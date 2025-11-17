using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using static OVRSpatialAnchor;

namespace Anaglyph.XRTemplate.SharedSpaces
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

	public class NetworkedAnchorException : Exception
	{
		public NetworkedAnchorException()
		{
		}

		public NetworkedAnchorException(string message)
			: base(message)
		{
		}

		public NetworkedAnchorException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}

	[RequireComponent(typeof(OVRSpatialAnchor))]
	public class NetworkedAnchor : NetworkBehaviour
	{
		private void Log(string str)
		{
			Debug.Log($"[NetworkedAnchor] {str}");
		}

		private OVRSpatialAnchor spatialAnchor;
		public OVRSpatialAnchor Anchor => spatialAnchor;

		//private Guid serverUuid;
		public NetworkVariable<Pose> OriginalPoseSync = new();

		public Pose DesiredPose => OriginalPoseSync.Value;

		public NetworkVariable<NetworkGuid> Uuid = new(new NetworkGuid(Guid.Empty));

		private void OnValidate()
		{
			TryGetComponent(out spatialAnchor);
		}

		private void Awake()
		{
			spatialAnchor = GetComponent<OVRSpatialAnchor>();
			spatialAnchor.enabled = false;

			Uuid.OnValueChanged += OnGuidChanged;
		}

		private async void OnGuidChanged(NetworkGuid previous, NetworkGuid current)
		{
			if (IsOwner || Uuid.Value.guid == Guid.Empty)
				return;

			await Load(Uuid.Value.guid);
		}

		public override async void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				await Share();
			}
			else
			{
				if (Uuid.Value.guid == Guid.Empty)
					return;

				await Load(Uuid.Value.guid);
			}
		}

		public async Task Share()
		{
			if (!XRSettings.enabled)
				return;

			if (!IsOwner)
				throw new Exception("Only the anchor owner can share it!");

			Redo:
			try
			{
				OriginalPoseSync.Value = transform.GetWorldPose();

				ExitIfBehaviorDisabled();

				Log("Sharing new anchor");

				spatialAnchor.enabled = true;

				var localizeSuccess = await spatialAnchor.WhenLocalizedAsync();

#if UNITY_EDITOR
				await Awaitable.WaitForSecondsAsync(0.5f);
#endif

				ExitIfBehaviorDisabled();
				if (!localizeSuccess)
					throw new NetworkedAnchorException($"Failed to localize anchor {spatialAnchor.Uuid}");

				//Log($"Saving anchor {spatialAnchor.Uuid}...");

				//var saveResult = await spatialAnchor.SaveAnchorAsync();
				//ExitIfBehaviorDisabled();
				//if (!saveResult.Success)
				//	throw new NetworkedAnchorException($"Failed to save anchor {spatialAnchor.Uuid}");

				// AnchorGuidSaving.AddAndSaveGuid(spatialAnchor.Uuid);

				Log($"Sharing anchor {spatialAnchor.Uuid}...");

				var shareResult = await spatialAnchor.ShareAsync(spatialAnchor.Uuid);

#if UNITY_EDITOR
				await Awaitable.WaitForSecondsAsync(0.5f);
#endif

				ExitIfBehaviorDisabled();
				if (shareResult.Success)
					Log($"Successfully shared anchor {spatialAnchor.Uuid}");
				else
					throw new NetworkedAnchorException($"Failed to share anchor {spatialAnchor.Uuid}: {shareResult}");

				Uuid.Value = new NetworkGuid(spatialAnchor.Uuid);
			}
			catch (NetworkedAnchorException e)
			{
				Debug.LogException(e);

				Debug.Log("Trying again in 5 seconds");
				await Awaitable.WaitForSecondsAsync(5);
				goto Redo;
			}
			catch (TaskCanceledException)
			{
				Debug.Log("Task cancelled");
			}
		}

		public async Task LocalizeAndBindAsync(UnboundAnchor unboundAnchor)
		{
			if (!XRSettings.enabled)
				return;

			Redo:
			try
			{
				ExitIfBehaviorDisabled();

				var localizeSuccess = await unboundAnchor.LocalizeAsync();

#if UNITY_EDITOR
				await Awaitable.WaitForSecondsAsync(0.5f);
#endif

				ExitIfBehaviorDisabled();
				if (!localizeSuccess)
					throw new NetworkedAnchorException($"Could not localize anchor {unboundAnchor.Uuid}");

				var gotAnchorPose = unboundAnchor.TryGetPose(out var anchorPose);
				if (!gotAnchorPose)
					throw new NetworkedAnchorException($"Couldn't get anchor {unboundAnchor.Uuid} pose");
				spatialAnchor.transform.SetWorldPose(anchorPose);

				unboundAnchor.BindTo(spatialAnchor);
				spatialAnchor.enabled = true;

				await Awaitable.NextFrameAsync();

				//if (IsOwner)
				//	OriginalPoseSync.Value = new(anchorPose);

				//Log($"Saving anchor {spatialAnchor.Uuid}...");

				//var saveResult = await spatialAnchor.SaveAnchorAsync();
				//ExitIfBehaviorDisabled();
				//if (!saveResult.Success)
				//	throw new NetworkedAnchorException($"Failed to save anchor {spatialAnchor.Uuid}");

				// AnchorGuidSaving.AddAndSaveGuid(spatialAnchor.Uuid);
			}
			catch (NetworkedAnchorException e)
			{
				Debug.LogException(e);
				Debug.Log("Trying again in 5 seconds");
				await Awaitable.WaitForSecondsAsync(5);
				goto Redo;
			}
			catch (TaskCanceledException)
			{
				Debug.Log("Task cancelled");
			}
		}

		private async Task Load(Guid uuid)
		{
			if (!XRSettings.enabled)
				return;

			Redo:
			try
			{
				ExitIfBehaviorDisabled();

				List<UnboundAnchor> loadedAnchors = new();

				UnboundAnchor unboundAnchor = default;

				//Log($"Checking if anchor {uuid} is saved locally...");

				//var loadResult = await LoadUnboundAnchorsAsync(new[] { uuid }, loadedAnchors);
				//ExitIfBehaviorDisabled();
				//if (!loadResult.Success)
				//{
				//	throw new NetworkedAnchorException($"Failed to load anchor {uuid}");
				//}
				//else if (loadResult.Value.Count == 0)
				//{
				//	Log($"Did not find anchors {uuid} saved locally. Downloading...");

				//	var downloadResult = await LoadUnboundSharedAnchorsAsync(uuid, loadedAnchors);
				//	ExitIfBehaviorDisabled();
				//	if (!downloadResult.Success)
				//		throw new NetworkedAnchorException($"Failed to download anchor {uuid}: {downloadResult}");
				//}

				var downloadResult = await LoadUnboundSharedAnchorsAsync(uuid, loadedAnchors);
				ExitIfBehaviorDisabled();
				if (!downloadResult.Success)
					throw new NetworkedAnchorException($"Failed to download anchor {uuid}: {downloadResult}");

				Log($"Loaded anchor {uuid}");
				unboundAnchor = loadedAnchors[0];

				await LocalizeAndBindAsync(unboundAnchor);
			}
			catch (NetworkedAnchorException e)
			{
				Debug.LogException(e);
				Debug.Log("Trying again in 5 seconds");
				await Awaitable.WaitForSecondsAsync(5);
				goto Redo;
			}
			catch (TaskCanceledException)
			{
				Debug.Log("Task cancelled");
			}
		}

		private void ExitIfBehaviorDisabled()
		{
			if (this == null || !enabled)
				throw new TaskCanceledException("Behavior disabled during async operation");
		}
	}
}