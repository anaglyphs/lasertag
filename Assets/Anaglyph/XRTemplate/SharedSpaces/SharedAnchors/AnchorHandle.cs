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
	/// Owns the local AR Foundation anchor handles for a process and routes trackable
	/// materialization/removal events to them.
	///
	/// Annoyingly, AR Foundation anchor operations are not cancellable.
	/// E.g. I can't stop an anchor download from completing and instantiating an ARAnchor
	/// if I don't need the anchor anymore.
	/// This system is here to make this limitation manageable.
	/// </summary>
	public sealed class AnchorRegistry : IDisposable
	{
		private readonly ARAnchorManager anchorManager;
		private readonly MetaOpenXRAnchorSubsystem anchorSubsystem;
		private readonly Dictionary<SerializableGuid, AnchorHandle> handles = new();
		private readonly CancellationTokenSource lifetimeCtknSrc = new();
		
		private readonly List<AnchorHandle> reconciliationSnapshot = new();

		private bool disposed;

		public AnchorRegistry(ARAnchorManager anchorManager, MetaOpenXRAnchorSubsystem anchorSubsystem)
		{
			this.anchorManager = anchorManager ?? throw new ArgumentNullException(nameof(anchorManager));
			this.anchorSubsystem =
				anchorSubsystem ?? throw new ArgumentNullException(nameof(anchorSubsystem));

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);
			ReconciliationLoop(lifetimeCtknSrc.Token);
		}

		public AnchorLease Acquire(SerializableGuid guid)
		{
			ThrowIfDisposed();

			if (!handles.TryGetValue(guid, out AnchorHandle handle))
			{
				handle = new AnchorHandle(this, guid);
				handles.Add(guid, handle);
			}

			handle.Retain(anchorManager.GetAnchor(guid));
			return new AnchorLease(handle);
		}

		public AnchorLease Acquire(ARAnchor anchor)
		{
			if (anchor == null)
				throw new ArgumentNullException(nameof(anchor));

			ThrowIfDisposed();

			SerializableGuid guid = anchor.trackableId;
			if (!handles.TryGetValue(guid, out AnchorHandle handle))
			{
				handle = new AnchorHandle(this, guid);
				handles.Add(guid, handle);
			}

			handle.Retain(anchor);
			return new AnchorLease(handle);
		}

		internal bool isDisposed => disposed;
		internal float time => Time.unscaledTime;

		internal async Awaitable<bool> TryLoadSharedAnchorAsync(SerializableGuid guid)
		{
			List<XRAnchor> downloaded = new(1);

			anchorSubsystem.sharedAnchorsGroupId = guid;
			XRResultStatus result =
				await anchorManager.TryLoadAllSharedAnchorsAsync(downloaded, null);

			if (result.IsError())
			{
				Debug.LogWarning($"Failed to load shared anchor {guid}: {result}");
				return false;
			}

			if (downloaded.Count == 0)
			{
				Debug.LogWarning($"Shared anchor group {guid} did not contain any anchors.");
				return false;
			}

			return true;
		}

		internal void RemoveAnchor(ARAnchor anchor)
		{
			if (anchor == null)
				return;

			anchorManager.TryRemoveAnchor(anchor);
			if (anchor.gameObject != null)
				Object.Destroy(anchor.gameObject);
		}

		internal void TryEvict(AnchorHandle handle)
		{
			if (!handle.canEvict)
				return;

			if (handles.TryGetValue(handle.guid, out AnchorHandle registered) &&
			    ReferenceEquals(registered, handle))
				handles.Remove(handle.guid);
		}

		private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> eventData)
		{
			foreach (ARAnchor anchor in eventData.added)
				if (handles.TryGetValue(anchor.trackableId, out AnchorHandle handle))
					handle.OnAnchorAdded(anchor);

			foreach ((SerializableGuid guid, ARAnchor _) in eventData.removed)
				if (handles.TryGetValue(guid, out AnchorHandle handle))
					handle.OnAnchorRemoved();
		}

		private async void ReconciliationLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					reconciliationSnapshot.Clear();
					reconciliationSnapshot.AddRange(handles.Values);

					foreach (AnchorHandle handle in reconciliationSnapshot)
						handle.Reconcile();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(AnchorRegistry));
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			lifetimeCtknSrc.Cancel();
			anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
			lifetimeCtknSrc.Dispose();
		}
	}

	/// <summary>
	/// A reversible claim that an <see cref="AnchorHandle"/> should remain loaded.
	/// Releasing the final lease requests unloading; it does not dispose an in-flight handle.
	/// </summary>
	public sealed class AnchorLease : IDisposable
	{
		private bool disposed;

		internal AnchorLease(AnchorHandle handle)
		{
			Handle = handle ?? throw new ArgumentNullException(nameof(handle));
		}

		public AnchorHandle Handle { get; }

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			Handle.Release();
		}
	}

	/// <summary>
	/// Reconciles the desired local presence of one shared anchor with AR Foundation's
	/// observed, asynchronously materialized anchor state.
	///
	/// 
	/// </summary>
	public sealed class AnchorHandle
	{
		public enum State
		{
			Unloaded,
			Loading,
			Materializing,
			Active,
			Removing
		}

		private const float RetryStepSeconds = 3f;
		private const float MaximumRetrySeconds = 30f;

		private readonly AnchorRegistry registry;

		private int leaseCount;
		private bool loadInFlight;
		private bool materializedDuringLoad;
		private int failedLoadCount;
		private float retryAt;

		private bool reconcilingCurrently;
		private bool shouldReconcileAgain;

		internal AnchorHandle(AnchorRegistry registry, SerializableGuid guid)
		{
			this.registry = registry;
			this.guid = guid;
			state = State.Unloaded;
		}

		public event Action<AnchorHandle> StateChanged = delegate { };

		public SerializableGuid guid { get; }
		public State state { get; private set; }
		public ARAnchor anchor { get; private set; }
		public bool desiredLoaded => leaseCount > 0;

		internal bool canEvict =>
			leaseCount == 0 &&
			!loadInFlight &&
			state == State.Unloaded &&
			anchor == null;

		internal void Retain(ARAnchor observedAnchor)
		{
			leaseCount++;

			if (observedAnchor != null)
				ObserveAnchor(observedAnchor);

			Reconcile();
		}

		internal void Release()
		{
			if (leaseCount == 0)
			{
				Debug.LogError($"Anchor handle {guid} was released more times than it was acquired.");
				return;
			}

			leaseCount--;
			Reconcile();
		}

		internal void OnAnchorAdded(ARAnchor addedAnchor)
		{
			if (addedAnchor.trackableId != (TrackableId)guid)
				return;

			ObserveAnchor(addedAnchor);
			Reconcile();
		}

		internal void OnAnchorRemoved()
		{
			anchor = null;
			SetState(State.Unloaded);
			Reconcile();
		}

		internal void Reconcile()
		{
			if (registry.isDisposed)
				return;

			if (reconcilingCurrently)
			{
				shouldReconcileAgain = true;
				return;
			}

			do
			{
				shouldReconcileAgain = false;
				reconcilingCurrently = true;

				try
				{
					ReconcileOnce();
				}
				finally
				{
					reconcilingCurrently = false;
				}
			} while (shouldReconcileAgain);
		}

		private void ReconcileOnce()
		{
			if (desiredLoaded)
			{
				if (anchor != null)
				{
					SetState(State.Active);
					return;
				}

				if (loadInFlight || state == State.Materializing)
					return;

				if (registry.time >= retryAt)
					StartLoad();

				return;
			}

			if (anchor != null)
			{
				RemoveAnchor();
				return;
			}

			if (loadInFlight || state == State.Materializing)
				return;

			SetState(State.Unloaded);
			registry.TryEvict(this);
		}

		private void StartLoad()
		{
			if (loadInFlight)
				return;

			loadInFlight = true;
			materializedDuringLoad = false;
			SetState(State.Loading);
			RunLoadAsync();
		}

		private async void RunLoadAsync()
		{
			bool requestSucceeded = false;

			try
			{
				requestSucceeded = await registry.TryLoadSharedAnchorAsync(guid);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				loadInFlight = false;

				if (anchor != null)
				{
					failedLoadCount = 0;
					retryAt = 0;
					SetState(State.Active);
				}
				else if (requestSucceeded && !materializedDuringLoad)
				{
					failedLoadCount = 0;
					retryAt = 0;
					SetState(State.Materializing);
				}
				else
				{
					SetState(State.Unloaded);
					ScheduleRetry();
				}

				Reconcile();
			}
		}

		private void ObserveAnchor(ARAnchor observedAnchor)
		{
			if (observedAnchor.trackableId != (TrackableId)guid)
				throw new ArgumentException("The observed anchor does not match this handle.", nameof(observedAnchor));

			if (loadInFlight)
				materializedDuringLoad = true;

			anchor = observedAnchor;
			failedLoadCount = 0;
			retryAt = 0;
			SetState(State.Active);
		}

		private void RemoveAnchor()
		{
			ARAnchor anchorToRemove = anchor;

			SetState(State.Removing);
			anchor = null;
			registry.RemoveAnchor(anchorToRemove);
			SetState(State.Unloaded);

			Reconcile();
		}

		private void ScheduleRetry()
		{
			if (!desiredLoaded)
			{
				retryAt = 0;
				return;
			}

			failedLoadCount++;
			retryAt = registry.time +
				Mathf.Min(RetryStepSeconds * failedLoadCount, MaximumRetrySeconds);
		}

		private void SetState(State next)
		{
			if (state == next)
				return;

			state = next;
			StateChanged.Invoke(this);
		}
	}
}
