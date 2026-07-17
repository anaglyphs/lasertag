using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using Object = UnityEngine.Object;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// An anchor handle represents an anchor
	/// that can be in any state, including an unloaded or loading state.
	/// ARFoundation anchor management functions are async and annoyingly do not support cancellation.
	/// This handle class tries to make it easier to reconcile an anchor's desired state with its actual state
	/// and uncancellable operations.
	///
	/// Despite writing this by hand, this is overly complicated slop that desperately needs a refactor.
	/// </summary>
	public class AnchorHandle : IDisposable
	{
		public enum State
		{
			Unloaded,
			Loading,
			AboutToBecomeActive,
			Active
		}

		// Share state is independent since an unloaded anchor can still be shared on the cloud
		public enum ShareState
		{
			Unknown, // The anchor may or may not be shared. This system doesn't yet know
			Uploading,
			Shared
		}

		private static ARAnchorManager anchorManager;
		private static MetaOpenXRAnchorSubsystem anchorSubsystem;

		private static readonly Dictionary<SerializableGuid, AnchorHandle> allHandles = new();

		private static CancellationTokenSource ctknSrc;

		/// <summary>
		/// Necessary before using any 
		/// </summary>
		/// <param name="aman"></param>
		/// <param name="asubsys"></param>
		public static void StartService(ARAnchorManager aman, MetaOpenXRAnchorSubsystem asubsys)
		{
			StopService();

			anchorManager = aman;
			anchorSubsystem = asubsys;

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);

			ctknSrc = new CancellationTokenSource();
		}

		public static void StopService()
		{
			ctknSrc?.Cancel();

			if (anchorManager != null)
				anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged); // for safety
		}


		// TODO refactor all these complicated logic paths into a state reconciliation loop
		// private static async void ReconciliationLoop(CancellationToken ctkn)
		// {
		// 	try
		// 	{
		// 		while (!ctkn.IsCancellationRequested)
		// 		{
		// 			await Awaitable.NextFrameAsync(ctkn);
		//
		// 			foreach (AnchorHandle handle in allHandles.Values)
		// 			{
		// 				
		// 			}
		// 		}
		// 	}
		// 	catch (OperationCanceledException)
		// 	{
		// 	}
		// 	catch (Exception e)
		// 	{
		// 	}
		// }

		public static AnchorHandle GetHandle(SerializableGuid guid)
		{
			return allHandles.TryGetValue(guid, out AnchorHandle handle) ? handle : new AnchorHandle(guid);
		}

		public static AnchorHandle GetHandle(ARAnchor anchor)
		{
			if (allHandles.TryGetValue(anchor.trackableId, out AnchorHandle handle))
			{
				handle.SetLoadedAnchor(anchor);
				return handle;
			}

			return new AnchorHandle(anchor);
		}

		/// <summary>
		/// Very annoyingly, ARAnchorManager.TryLoadAllSharedAnchorsAsync does not yield
		/// usable ARAnchors, but 'XRAnchor' structs.
		/// ARAnchors appear async after the load is done, so I need to register all existing
		/// handles to an 'allHandles' dict and then update them with this
		/// ARAnchorManager.trackablesChanged callback. Ugh
		/// </summary>
		/// <param name="eventData"></param>
		private static void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> eventData)
		{
			foreach (ARAnchor anchor in eventData.added)
				if (allHandles.TryGetValue(anchor.trackableId, out AnchorHandle handle))
					handle.SetLoadedAnchor(anchor);

			foreach ((SerializableGuid guid, ARAnchor _) in eventData.removed)
				if (allHandles.TryGetValue(guid, out AnchorHandle handle))
					handle.OnAnchorRemoved();
		}

		public SerializableGuid guid { get; }
		public State state { get; private set; }
		public ShareState shareState { get; private set; }
		public bool markedForUnloading { get; private set; }

		public ARAnchor anchor { get; private set; }

		// For when the handle should simultaneously dispose upon anchor removal
		private bool markedForDisposal = false;
		private bool downloadQueued = false;


		private CancellationTokenSource shareCtknSrc;
		private CancellationTokenSource downloadCtknSrc;

		/// <summary>
		/// Handle for an already active anchor
		/// </summary>
		/// <param name="anchor"></param>
		private AnchorHandle(ARAnchor anchor)
		{
			this.anchor = anchor;
			guid = anchor.trackableId;
			state = State.Active;

			allHandles.Add(guid, this);
		}

		/// <summary>
		/// An anchor that (probably) needs to be downloaded.
		/// Will check for an existing anchor with the specified GUID
		/// </summary>
		/// <param name="guid">The GUID of the anchor to load</param>
		private AnchorHandle(SerializableGuid guid)
		{
			this.guid = guid;
			state = State.Unloaded;
			anchor = null;

			allHandles.Add(guid, this);

			// make sure an existing ARAnchor with this guid doesn't already exist
			ARAnchor possibleAnchor = anchorManager.GetAnchor(guid);
			if (possibleAnchor != null)
				SetLoadedAnchor(possibleAnchor);
		}

		/// <summary>
		/// This does NOT destroy an associated active anchor.
		/// If you want to unload an anchor and then dispose of the handle,
		/// use <see cref="MarkForUnloadingAndDisposal"/>.
		/// You can Dispose without removing an anchor, but this will 'orphan' the anchor without a handle.
		/// </summary>
		public void Dispose()
		{
			allHandles.Remove(guid);
			shareCtknSrc?.Dispose();
			downloadCtknSrc?.Dispose();
		}

		/// <summary>
		/// Shares the anchor in an individual `sharedAnchorsGroupId` so that other clients can download
		/// select anchors at different points in time instead of all anchors at once.
		/// Unity annoyingly only provides `anchorManager.TryLoadAllSharedAnchorsAsync` and nothing like
		/// `anchorManager.TryLoadSharedAnchorAsync(*single anchor GUID*)`.
		/// Sharing with a different group GUID per anchor circumvents this limitation.
		/// </summary>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public async Awaitable<ShareState> ShareAnchor()
		{
			if (state != State.Active)
				return shareState;

			shareCtknSrc = new CancellationTokenSource();
			CancellationToken ctkn = shareCtknSrc.Token;

			float retryTime = 3;

			shareState = ShareState.Uploading;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					anchorSubsystem.sharedAnchorsGroupId = anchor.trackableId;
					XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);

					if (result.IsError())
					{
						Debug.LogError($"Failed to share anchors! {result.ToString()}");
						await Awaitable.WaitForSecondsAsync(retryTime, ctkn);
						continue;
					}

					shareState = ShareState.Shared;

					break;
				}
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
				if (shareState == ShareState.Uploading)
					shareState = ShareState.Unknown;
			}

			return shareState;
		}

		/// <summary>
		/// Download the AR anchor tied to this handle's GUID.
		/// The ARAnchor does NOT exist immediately upon a successful download.
		/// It should async appear shortly after in <see cref="OnTrackablesChanged"/>
		/// </summary>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public async Awaitable<State> DownloadSharedAnchor()
		{
			if (state != State.Active)
				downloadQueued = true;

			// cancel anchor removal if marked so
			UnmarkForUnloadingAndDisposal();

			if (state != State.Unloaded)
				return state; // already downloading or downloaded!

			downloadCtknSrc = new CancellationTokenSource();
			CancellationToken ctkn = downloadCtknSrc.Token;

			state = State.Loading;

			float retryTime = 3;

			// only one anchor per group. see `ShareAnchor()`
			List<XRAnchor> downloaded = new(1);

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					downloaded.Clear();
					anchorSubsystem.sharedAnchorsGroupId = guid;
					XRResultStatus result = await anchorManager.TryLoadAllSharedAnchorsAsync(downloaded, null);

					if (downloaded.Count == 0 || result.IsError())
					{
						ctkn.ThrowIfCancellationRequested();

						Debug.LogError(result.IsError()
							? $"Error loading shared anchor! {result.ToString()}"
							: $"Anchor download request worked but no anchors were present!");

						await Awaitable.WaitForSecondsAsync(retryTime, ctkn);
						retryTime += 3;
						continue;
					}

					// if the download succeeded, this means this anchor has been shared
					// or else we wouldn't be able to download it in the first place.
					shareState = ShareState.Shared;

					if (state == State.Loading)
						state = State.AboutToBecomeActive;

					downloadQueued = false;

					break;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				downloadQueued = false;
				Debug.LogException(e);
			}
			finally
			{
				if (state == State.Loading)
					OnAnchorRemoved();
			}

			if (downloadQueued && state == State.Unloaded)
				return await DownloadSharedAnchor();

			return state;
		}

		/// <summary>
		/// For when an anchor is downloaded from outside an anchor handle
		/// </summary>
		/// <param name="loadedAnchor"></param>
		/// <exception cref="Exception"></exception>
		public void SetLoadedAnchor(ARAnchor loadedAnchor)
		{
			if (loadedAnchor.trackableId != (TrackableId)guid)
				throw new Exception("This isn't the same anchor!");

			state = State.Active;
			anchor = loadedAnchor;
			downloadQueued = false;

			if (markedForUnloading)
				RemoveAnchor();
		}

		/// <summary>
		/// Mark the anchor for unloading and dispose the handle upon unloading.
		/// <see cref="MarkForUnloading"/>
		/// </summary>
		public void MarkForUnloadingAndDisposal()
		{
			markedForDisposal = true;

			if (state == State.Unloaded)
				Dispose(); // if the anchor is already unloaded, immediately dispose
			else
				MarkForUnloading();
		}

		/// <summary>
		/// Mark the anchor for unloadding. The anchor will unload as soon as it can.
		/// If already active, the anchor is immediately unloaded.
		/// If loading, the anchor is unloaded as soon as it loads
		/// </summary>
		public void MarkForUnloading()
		{
			if (markedForUnloading || state == State.Unloaded)
				return;

			downloadQueued = false;
			markedForUnloading = true;
			shareCtknSrc?.Cancel();
			downloadCtknSrc?.Cancel();

			if (state == State.Active)
				RemoveAnchor();
		}

		public void UnmarkForUnloadingAndDisposal()
		{
			markedForDisposal = false;
			markedForUnloading = false;
		}

		/// <summary>
		/// Actually remove the anchor
		/// </summary>
		/// <exception cref="Exception"></exception>
		private void RemoveAnchor()
		{
			if (state != State.Active)
				throw new Exception("Tried removing an anchor that wasn't active!");

			anchorManager.TryRemoveAnchor(anchor);
			if (anchor != null)
				Object.Destroy(anchor.gameObject);
			OnAnchorRemoved();
		}

		/// <summary>
		/// Handle anchor removal.
		/// This can come from <see cref="RemoveAnchor"/>,
		/// <see cref="OnTrackablesChanged"/>, or a cancelled <see cref="DownloadSharedAnchor"/> call.
		/// </summary>
		private void OnAnchorRemoved()
		{
			state = State.Unloaded;
			markedForUnloading = false;

			if (markedForDisposal)
				Dispose();
		}
	}
}