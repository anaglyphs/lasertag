using System;
using Unity.Collections;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	internal struct SpaceTransitionCommand
	{
		public Guid operation;
		public Guid revision;
		public Guid referenceContext;
		public MapIdentity space;
		public ulong author;
		public ColocationManager.ColocationMethod target;
		public ReferenceTransitionIntent intent;
		public bool createsReferences;
		public bool committed;
	}

	internal struct SpaceTransitionEvidence
	{
		public Guid operation;
		public Guid revision;
		public Guid referenceContext;
		public int trackingGeneration;
	}

	internal struct SpaceMethodRequest
	{
		public Guid context;
		public ColocationManager.ColocationMethod method;
		public bool cancel;
	}

	internal enum SpaceReferenceAction : byte
	{
		Register = 0,
		Remove = 1,
		Size = 2,
		Pair = 3,
		ForgetPair = 5
	}

	internal struct SpaceReferenceRequest
	{
		public Guid context;
		public Guid setupOperation;
		public Guid operation;
		public SpaceReferenceAction action;
		public int tag;
		public int second;
		public int trackingGeneration;
		public float size;
		public Pose pose;
	}

	internal struct SpaceRequestRejection
	{
		public Guid context;
		public Guid operation;
		public ulong recipient;
	}

	/// <summary>The header commits a complete candidate revision after its ordered list writes.</summary>
	internal sealed class MapSpaceTransitionSync
	{
		private readonly MapSpaceSessionSync references = new("space.candidate");
		private readonly SyncVariable<SpaceTransitionCommand> command = new("space.transition");
		private readonly SyncEvent<SpaceTransitionEvidence> evidence = new("space.transition.evidence", EventRoute.ToAuthority);
		private readonly SyncEvent<SpaceRequestRejection> requestRejection = new("space.request.rejected", EventRoute.ViaAuthority);
		private readonly SyncEvent<SpaceMethodRequest> methodRequest = new("space.method.request", EventRoute.ToAuthority);
		private readonly SyncEvent<SpaceReferenceRequest> referenceRequest = new("space.reference.request", EventRoute.ToAuthority);
		public event Action<SpaceTransitionCommand, MapSpace> Received = delegate { };
		public event Action<ulong, SpaceTransitionEvidence> Validated = delegate { };
		public event Action<ulong, SpaceMethodRequest> MethodRequested = delegate { };
		public event Action<ulong, SpaceReferenceRequest> ReferenceRequested = delegate { };
		public event Action<ulong, SpaceRequestRejection> RequestRejected = delegate { };
		public SpaceTransitionCommand Current => command.Value;

		public void Register()
		{
			references.Register();
			command.Validate = (_, _) => false;
			command.Register();
			command.Changed += OnChanged;
			command.Synced += OnSynced;
			evidence.Register();
			evidence.Received += OnEvidence;
			requestRejection.Validate = (sender, _) => SyncBus.IsAuthority && sender == SyncBus.LocalClientId;
			requestRejection.Register();
			requestRejection.Received += OnRequestRejected;
			methodRequest.Register();
			methodRequest.Received += OnMethodRequested;
			referenceRequest.Register();
			referenceRequest.Received += OnReferenceRequested;
		}

		public void Unregister()
		{
			command.Changed -= OnChanged;
			command.Synced -= OnSynced;
			command.Unregister();
			references.Unregister();
			evidence.Received -= OnEvidence;
			evidence.Unregister();
			requestRejection.Received -= OnRequestRejected;
			requestRejection.Unregister();
			methodRequest.Received -= OnMethodRequested;
			methodRequest.Unregister();
			referenceRequest.Received -= OnReferenceRequested;
			referenceRequest.Unregister();
		}

		private void OnChanged(SpaceTransitionCommand _, SpaceTransitionCommand value)
		{
			if (SyncBus.Active && !SyncBus.IsAuthority)
				Received.Invoke(value, value.operation == Guid.Empty ? null : references.Read(value.space));
		}

		private void OnSynced() => OnChanged(default, command.Value);
		private void OnEvidence(ulong sender, SpaceTransitionEvidence value) => Validated.Invoke(sender, value);
		private void OnMethodRequested(ulong sender, SpaceMethodRequest value) => MethodRequested.Invoke(sender, value);
		private void OnReferenceRequested(ulong sender, SpaceReferenceRequest value) => ReferenceRequested.Invoke(sender, value);
		private void OnRequestRejected(ulong sender, SpaceRequestRejection value) => RequestRejected.Invoke(sender, value);
		public void Report(SpaceTransitionEvidence value) => evidence.Raise(value);
		public void RequestMethod(SpaceMethodRequest value) => methodRequest.Raise(value);
		public void RequestReference(SpaceReferenceRequest value) => referenceRequest.Raise(value);
		public void Reject(SpaceRequestRejection value) => requestRejection.Raise(value);

		public void Publish(SpaceTransitionCommand value, MapSpace candidate)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority)
				return;

			if (candidate != null)
			{
				references.Stage(candidate);
				value.space = new MapIdentity
				{
					spaceId = Guid.Parse(candidate.id),
					frameId = Guid.Parse(candidate.canonicalFrameId),
					spaceVersion = value.revision,
					tagSizeCm = candidate.tagSizeCm,
					firstTagId = candidate.firstTagId,
					secondTagId = candidate.secondTagId,
					preferredColocationMethod = candidate.preferredColocationMethod
				};
				value.space.spaceName.CopyFromTruncated(candidate.name ?? "");
			}
			command.Value = value;
		}
	}
}
