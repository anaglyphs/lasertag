using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	/// <summary>One spatial entity a discovery handed back.</summary>
	internal readonly struct SpatialEntity
	{
		public SpatialEntity(ulong handle, Guid uuid)
		{
			this.handle = handle;
			this.uuid = uuid;
		}

		/// <summary>
		/// The runtime's handle for this entity. Valid until <see cref="MetaSpatialEntities.Release"/>,
		/// which every discovered entity needs eventually — the runtime allocates one per result.
		/// </summary>
		public readonly ulong handle;

		public readonly Guid uuid;
	}

	/// <summary>
	/// Meta's spatial entity discovery and localization, spoken directly to OVRPlugin.
	///
	/// The OVRAnchor API is unusable in this project: every async operation it exposes is completed
	/// by OVRManager's event loop, and this project runs Unity's OpenXR stack with no OVRManager in
	/// any scene, so an OVRTask from it never finishes at all. Everything here issues the same
	/// OVRPlugin requests and drains the event queue itself.
	///
	/// OVRPlugin keeps its own event queue, filled by the xrPollEvent hook MetaXRFeature installs,
	/// so draining it takes nothing away from Unity's OpenXR loader. It is destructive within
	/// OVRPlugin though: adding an OVRManager would give that queue a second reader, and each
	/// reader would miss whatever the other consumed first.
	/// </summary>
	internal static class MetaSpatialEntities
	{
		/// <summary>
		/// Whether Meta's runtime is the one underneath this process. OVRPlugin p/invokes a native
		/// library that only exists where that runtime does, so a missing entry point reads as
		/// "not a Quest" rather than escaping.
		/// </summary>
		public static bool IsAvailable
		{
			get
			{
				try
				{
					return OVRPlugin.initialized;
				}
				catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
				{
					return false;
				}
			}
		}

		/// <summary>
		/// One outstanding OVRPlugin request, against the waiter that will read its outcome.
		/// </summary>
		private sealed class Request
		{
			/// <summary>Where discovery results are accumulated; null for every other request.</summary>
			public List<SpatialEntity> discovered;

			public bool answered;
			public bool succeeded;
		}

		private static readonly Dictionary<ulong, Request> requests = new();
		private static OVRPlugin.EventDataBuffer eventBuffer;

		/// <summary>
		/// How much longer than the runtime's own timeout a request is waited on, so that a request
		/// which times out has done so at the runtime first.
		/// </summary>
		private const float RequestGraceSeconds = 2f;

		/// <summary>
		/// Finds spatial entities the runtime holds for this app, appending them to
		/// <paramref name="results"/>.
		///
		/// With no uuids this returns everything discoverable in the space the headset is standing
		/// in — saved anchors and the system's own scene entities alike. Pass the uuids of the
		/// anchors actually wanted: it is the only way to tell one from the other, and it keeps the
		/// runtime from handing over entities that are in use.
		///
		/// Cancellation reports the request as unanswered rather than throwing.
		/// </summary>
		public static async Awaitable<bool> TryDiscoverAsync(List<SpatialEntity> results,
			IReadOnlyCollection<Guid> uuids, float timeoutSeconds, CancellationToken ctkn)
		{
			if (!IsAvailable)
				return false;

			OVRPlugin.Result started = StartDiscovery(uuids, out ulong requestId);

			if (!started.IsSuccess())
			{
				Debug.LogWarning($"Could not start spatial entity discovery: {started}");
				return false;
			}

			Request request = new() { discovered = results };
			return await AwaitRequestAsync(requestId, request, timeoutSeconds, ctkn);
		}

		private static unsafe OVRPlugin.Result StartDiscovery(IReadOnlyCollection<Guid> uuids,
			out ulong requestId)
		{
			if (uuids == null || uuids.Count == 0)
				return OVRPlugin.DiscoverSpaces(new OVRPlugin.SpaceDiscoveryInfo(), out requestId);

			NativeArray<Guid> ids = new(uuids.Count, Allocator.Temp);

			try
			{
				int index = 0;
				foreach (Guid uuid in uuids)
					ids[index++] = uuid;

				OVRPlugin.SpaceDiscoveryFilterInfoIds filter = new()
				{
					Type = OVRPlugin.SpaceDiscoveryFilterType.Ids,
					NumIds = ids.Length,
					Ids = (Guid*)ids.GetUnsafePtr(),
				};

				OVRPlugin.SpaceDiscoveryFilterInfoHeader* header =
					(OVRPlugin.SpaceDiscoveryFilterInfoHeader*)&filter;

				return OVRPlugin.DiscoverSpaces(new OVRPlugin.SpaceDiscoveryInfo
				{
					NumFilters = 1,
					Filters = &header,
				}, out requestId);
			}
			finally
			{
				ids.Dispose();
			}
		}

		/// <summary>
		/// Turns tracking for this entity on or off. An entity has to be locatable before
		/// <see cref="IsPositionTracked"/> can say anything about it, and enabling it is what makes
		/// the runtime go looking for the entity in the current space.
		///
		/// Cancellation reports the request as unanswered rather than throwing.
		/// </summary>
		public static async Awaitable<bool> TrySetLocatableAsync(ulong handle, bool enable,
			float timeoutSeconds, CancellationToken ctkn)
		{
			if (!IsAvailable)
				return false;

			if (IsLocatable(handle) == enable)
				return true;

			if (!OVRPlugin.SetSpaceComponentStatus(handle, OVRPlugin.SpaceComponentType.Locatable,
				    enable, timeoutSeconds, out ulong requestId))
				// A component already in the requested state is reported as a failed request, and
				// the runtime raises no event for it, so read the status back rather than wait.
				return IsLocatable(handle) == enable;

			// Outlast the timeout the runtime was given, so that giving up here means the request
			// really has resolved. Callers release the entity's handle once this returns, and the
			// runtime does not define what destroying it out from under a live request does.
			return await AwaitRequestAsync(requestId, new Request(),
				timeoutSeconds + RequestGraceSeconds, ctkn);
		}

		private static bool IsLocatable(ulong handle) =>
			OVRPlugin.GetSpaceComponentStatus(handle, OVRPlugin.SpaceComponentType.Locatable,
				out bool enabled, out bool changePending) && enabled && !changePending;

		/// <summary>
		/// Whether the runtime is tracking this entity's position right now, which is the test for
		/// whether it belongs to the space the headset is standing in. Only meaningful once
		/// <see cref="TrySetLocatableAsync"/> has succeeded.
		/// </summary>
		public static bool IsPositionTracked(ulong handle) =>
			OVRPlugin.TryLocateSpace(handle, OVRPlugin.GetTrackingOriginType(), out _,
				out OVRPlugin.SpaceLocationFlags flags) && flags.IsPositionTracked();

		/// <summary>
		/// What the runtime says about locating this entity, in its own terms. A probe that finds
		/// nothing looks identical whether the locate call is failing outright or the anchors
		/// really are in another room, and those want opposite fixes.
		/// </summary>
		public static string DescribeLocation(ulong handle) =>
			OVRPlugin.TryLocateSpace(handle, OVRPlugin.GetTrackingOriginType(), out _,
				out OVRPlugin.SpaceLocationFlags flags)
				? $"flags {flags}"
				: "the runtime refused to locate it";

		/// <summary>
		/// Gives a discovered entity's handle back. The entity itself is untouched — this only ends
		/// this process's reference to it, and the same uuid can be loaded again afterwards.
		/// </summary>
		public static void Release(ulong handle)
		{
			if (handle != 0)
				OVRPlugin.DestroySpace(handle);
		}

		/// <summary>
		/// Waits for a request's completion event, pumping the queue that carries it. Reports
		/// whether the request answered in time and succeeded; a request that outlives its wait is
		/// forgotten, so its event lands nowhere once it finally arrives.
		/// </summary>
		private static async Awaitable<bool> AwaitRequestAsync(ulong requestId, Request request,
			float timeoutSeconds, CancellationToken ctkn)
		{
			requests[requestId] = request;
			float deadline = Time.unscaledTime + timeoutSeconds;

			try
			{
				while (!request.answered)
				{
					if (Time.unscaledTime >= deadline)
						return false;

					await Awaitable.NextFrameAsync(ctkn);
					DrainEvents();
				}
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			finally
			{
				requests.Remove(requestId);
			}

			return request.succeeded;
		}

		/// <summary>
		/// Takes everything OVRPlugin has queued and routes the parts any waiter asked for.
		/// Anything else in the queue is discarded, which is what this project already does with it
		/// — nothing else here reads OVRPlugin events.
		/// </summary>
		private static void DrainEvents()
		{
			while (OVRPlugin.PollEvent(ref eventBuffer))
			{
				byte[] data = eventBuffer.EventData;

				switch (eventBuffer.EventType)
				{
					case OVRPlugin.EventType.SpaceDiscoveryResultsAvailable:
						CollectDiscoveryResults(ReadRequestId(data));
						break;

					case OVRPlugin.EventType.SpaceDiscoveryComplete:
					case OVRPlugin.EventType.SpaceSetComponentStatusComplete:
						Answer(ReadRequestId(data), ReadSucceeded(data));
						break;
				}
			}
		}

		// Every payload routed above begins with the request id, and those that report an outcome
		// follow it with a result code that is negative on failure.
		private static ulong ReadRequestId(byte[] eventData) => BitConverter.ToUInt64(eventData, 0);

		private static bool ReadSucceeded(byte[] eventData) =>
			BitConverter.ToInt32(eventData, sizeof(ulong)) >= 0;

		private static void Answer(ulong requestId, bool succeeded)
		{
			if (!requests.TryGetValue(requestId, out Request request))
				return;

			request.succeeded = succeeded;
			request.answered = true;
		}

		/// <summary>
		/// Drains one batch of discovery results. Discovery reports them as it finds them and the
		/// runtime consumes what it hands over, so every batch has to be taken as it is announced,
		/// ahead of the completion event.
		/// </summary>
		private static unsafe void CollectDiscoveryResults(ulong requestId)
		{
			if (!requests.TryGetValue(requestId, out Request request) || request.discovered == null)
				return;

			if (!OVRPlugin.RetrieveSpaceDiscoveryResults(requestId, null, 0, out int count).IsSuccess() ||
			    count == 0)
				return;

			NativeArray<OVRPlugin.SpaceDiscoveryResult> batch = new(count, Allocator.Temp);

			try
			{
				OVRPlugin.Result result;

				// The batch can grow between being sized and being read, which the runtime reports
				// rather than truncating.
				while (true)
				{
					result = OVRPlugin.RetrieveSpaceDiscoveryResults(requestId,
						(OVRPlugin.SpaceDiscoveryResult*)batch.GetUnsafePtr(), batch.Length, out count);

					if (result != OVRPlugin.Result.Failure_InsufficientSize)
						break;

					batch.Dispose();
					batch = new NativeArray<OVRPlugin.SpaceDiscoveryResult>(count, Allocator.Temp);
				}

				if (!result.IsSuccess())
					return;

				for (int i = 0; i < count; i++)
					request.discovered.Add(new SpatialEntity(batch[i].Space, batch[i].Uuid));
			}
			finally
			{
				batch.Dispose();
			}
		}
	}
}
