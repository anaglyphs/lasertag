using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Debugging.Visuals;
using Anaglyph.Netcode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Colocates devices via Meta Shared Spatial Anchors. Uses ARFoundation anchors.
	/// Session owner creates and shares multiple anchors as they move around
	/// and also synchronizes 'canon' poses (where anchors *should* be).
	/// Other devices download these anchors.
	/// All devices find the best fit between anchor positions and known positions
	/// and transform the XR rig pose (inversely transforming the virtual environment) to align that best fit.
	///
	/// I wrote this all manually, but it is currently total slop and I desperately need to refactor.
	/// Fable did a better job... :(
	/// </summary>
	[DefaultExecutionOrder(999)]
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		// Distance from all other anchors the session owner headset needs to be to spawn a new anchor
		[SerializeField] private float newAnchorDist = 6f;
		[SerializeField] private LayerMask anchorPlacementRaycastlayerMask = Physics.DefaultRaycastLayers;

		public event Action Colocated = delegate { };

		// Becomes false when the device sleeps
		// TODO: also set to false on tracking loss
		private bool isAligned = false;
		private bool isActive = false;

		private CancellationTokenSource sessionCtknSrc;

		private readonly SyncDictionary<SerializableGuid, Pose> canonPoses = new("colo.meta.poses");
		private readonly Dictionary<SerializableGuid, AnchorHandle> anchors = new();

		private readonly List<(float3 subject, float3 target)> positionPairs = new();

		private ARAnchorManager anchorManager;
		private MetaOpenXRAnchorSubsystem metaAnchorSubsystem;

		public void Awake()
		{
			anchorManager = FindFirstObjectByType<ARAnchorManager>();
			metaAnchorSubsystem = (MetaOpenXRAnchorSubsystem)anchorManager.subsystem;

			canonPoses.Changed += OnCanonPosesChanged;
			canonPoses.Register();

			AnchorHandle.StartService(anchorManager, metaAnchorSubsystem);
		}

		private void OnDestroy()
		{
			StopColocation();
			canonPoses.Unregister();
			sessionCtknSrc?.Cancel();
		}

		private void OnApplicationFocus(bool isFocused)
		{
			// Device sleeping = tracking pauses = tracking lost
			// This prevents the device from creating erroneous anchors when it wakes up
			// until it aligns itself again
			if (!isFocused)
				isAligned = false;
		}

		public void StartColocation()
		{
			if (isActive) return;

			Supported shareSupportState = metaAnchorSubsystem.isSharedAnchorsSupported;
			if (shareSupportState != Supported.Supported)
				Debug.LogWarning($"Shared anchors are not enabled/supported! {shareSupportState}");

			sessionCtknSrc = new CancellationTokenSource();
			CancellationToken ctkn = sessionCtknSrc.Token;

			AnchorCreationLoop(ctkn);
			AlignLoop(ctkn);

			isActive = true;

			// kick off colocation as the host by sharing the first anchor
			// this bypasses the initial distance check since we need an initial anchor, duh
			if (SyncBus.IsAuthority)
				_ = CreateAndShareNewAnchorUnderPlayer(ctkn);
			else
				ReconcileHandlesWithCanonPoses();
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;

			isAligned = false;

			sessionCtknSrc?.Cancel();

			foreach (AnchorHandle handle in anchors.Values) handle.MarkForUnloadingAndDisposal();
			if (SyncBus.IsAuthority) canonPoses.Clear(); // already done when network ends but just to be safe
			anchors.Clear();
		}

		private void TryRegisterNewHandle(SerializableGuid guid)
		{
			if (anchors.TryGetValue(guid, out AnchorHandle handle))
			{
				if (handle.state != AnchorHandle.State.Unloaded)
					return; // already exists. this is probably the host
			}
			else
			{
				handle = AnchorHandle.GetHandle(guid);
				anchors[handle.guid] = handle;
			}

			if (handle.markedForUnloading)
				handle.UnmarkForUnloadingAndDisposal();

			if (handle.state != AnchorHandle.State.Active)
				_ = handle.DownloadSharedAnchor();
		}

		private void QueueHandleRemoval(SerializableGuid guid)
		{
			if (anchors.TryGetValue(guid, out AnchorHandle handle))
			{
				handle.MarkForUnloadingAndDisposal();
				anchors.Remove(guid);
			}
		}

		private void OnCanonPosesChanged(SyncDictionary<SerializableGuid, Pose>.EventData data)
		{
			if (!isActive)
				return;

			switch (data.op)
			{
				case SyncDictionaryOp.Set:
				{
					// download single anchor
					SerializableGuid guid = data.eventKey;
					TryRegisterNewHandle(guid);
					break;
				}

				case SyncDictionaryOp.Snapshot:
				{
					ReconcileHandlesWithCanonPoses();
					break;
				}

				case SyncDictionaryOp.Remove:
				{
					SerializableGuid guid = data.eventKey;
					QueueHandleRemoval(guid);
					break;
				}

				case SyncDictionaryOp.Clear:
				{
					foreach (SerializableGuid guid in canonPoses.Keys) QueueHandleRemoval(guid);
					break;
				}
			}
		}

		private void ReconcileHandlesWithCanonPoses()
		{
			List<SerializableGuid> staleGuids = new();

			foreach (SerializableGuid guid in anchors.Keys)
				if (!canonPoses.ContainsKey(guid))
					staleGuids.Add(guid);

			foreach (SerializableGuid guid in staleGuids)
				QueueHandleRemoval(guid);

			foreach (SerializableGuid guid in canonPoses.Keys)
				TryRegisterNewHandle(guid);
		}

		private async void AlignLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					(Pose subject, Pose target) posePair = new(Pose.identity, Pose.identity);

					positionPairs.Clear();

					int correspondingCount = 0;

					foreach ((SerializableGuid guid, AnchorHandle handle) in anchors)
					{
						if (handle.state != AnchorHandle.State.Active) continue;

						bool isTracked = handle.anchor.trackingState == TrackingState.Tracking;
						if (!isTracked || !canonPoses.TryGetValue(guid, out Pose pose)) continue;

						// add corresponding pair
						positionPairs.Add((handle.anchor.transform.position, pose.position));

						Transform aTrans = handle.anchor.transform;

						posePair = (new Pose(aTrans.position, aTrans.rotation), pose);

						correspondingCount++;
					}

					switch (correspondingCount)
					{
						case 1: // single anchor with a corresponding pose

							Pose s = posePair.subject;
							Pose t = posePair.target;

							Matrix4x4 anchorMat = Matrix4x4.TRS(s.position, s.rotation, Vector3.one);
							Matrix4x4 canonMat = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

							MainXRRig.Instance.AlignSpace(anchorMat, canonMat);

							if (!isAligned)
							{
								Colocated.Invoke();
								isAligned = true;
							}

							break;

						case > 1:

							float4x4 fitShift = BestFit.Find4DOF(positionPairs);
							MainXRRig.Instance.ShiftSpace(fitShift);

							if (!isAligned)
							{
								Colocated.Invoke();
								isAligned = true;
							}

							break;

						default:
							isAligned = false;

							break;
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				AlignLoop(ctkn);
			}
		}

		// ------- host anchor sharing -------------------------------

		private async void AnchorCreationLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.FixedUpdateAsync(ctkn);

					// non authority clients run this loop but don't do anything
					// unless they become the session owner
					if (!SyncBus.IsAuthority) continue;

					// don't instantiate anchors if not already aligned
					if (!isAligned) continue;

					float spawnEverySqr = newAnchorDist * newAnchorDist;
					float3 headPos = MainXRRig.Camera.transform.position;
					float closestDistSq = float.MaxValue;

					foreach (Pose pose in canonPoses.Values)
					{
						float distSq = math.distancesq(pose.position, headPos);
						if (distSq < closestDistSq) closestDistSq = distSq;
					}

					if (closestDistSq > spawnEverySqr)
						await CreateAndShareNewAnchorUnderPlayer(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				AnchorCreationLoop(ctkn);
			}
		}

		public async Awaitable CreateAndShareNewAnchorUnderPlayer(CancellationToken ctkn)
		{
			try
			{
				Pose playerFeetPos;
				playerFeetPos.rotation = Quaternion.identity;

				Vector3 headPos = MainXRRig.Camera.transform.position;

				Ray ray = new(headPos, Vector3.down);

				if (Physics.Raycast(ray, out RaycastHit hit, 2f, anchorPlacementRaycastlayerMask,
					    QueryTriggerInteraction.Ignore))
					playerFeetPos.position = hit.point;
				else
					playerFeetPos.position = headPos - Vector3.up * 1.5f;

				await CreateAndShareNewAnchor(playerFeetPos, ctkn);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		public async Awaitable CreateAndShareNewAnchor(Pose pose, CancellationToken ctkn)
		{
			AnchorHandle handle = null;
			bool published = false;

			try
			{
				Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(pose);
				if (!result.status.IsSuccess())
					throw new Exception("Failed to create new anchor!");

				handle = AnchorHandle.GetHandle(result.value);
				anchors[handle.guid] = handle;

				ctkn.ThrowIfCancellationRequested();

				AnchorHandle.ShareState shareState = await handle.ShareAnchor();
				ctkn.ThrowIfCancellationRequested();
				if (shareState != AnchorHandle.ShareState.Shared) return;

				canonPoses.Set(handle.guid, pose);
				published = true;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				if (handle != null && !published)
				{
					handle.MarkForUnloadingAndDisposal();
					anchors.Remove(handle.guid);
				}
			}
		}

		private void Update()
		{
			if (!AnaglyphDebugging.DebugMode)
				return;

			foreach (Pose pose in canonPoses.Values)
				DebugAxisVisual.DrawDebugAxis(pose.position, pose.rotation, Color.cyan);
		}
	}
}