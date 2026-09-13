using System;
using Unity.Collections;
using Anaglyph.Netcode.SyncVariables;

namespace Anaglyph.LaserTag.Maps
{
	internal struct SpaceTransitionCommand
	{
		public Guid operation, revision, referenceContext;
		public MapIdentity space;
		public ulong author;
		public ColocationManager.ColocationMethod target;
		public ReferenceTransitionIntent intent;
		public bool createsReferences;
		public bool committed;
	}
	internal struct SpaceTransitionEvidence
	{ public Guid operation, revision, referenceContext; public int trackingGeneration; }

	/// <summary>The header commits a complete candidate revision after its ordered list writes.</summary>
	internal sealed class MapSpaceTransitionSync
	{
		private readonly MapSpaceSessionSync references = new("space.candidate");
		private readonly SyncVariable<SpaceTransitionCommand> command = new("space.transition");
		private readonly SyncEvent<SpaceTransitionEvidence> evidence = new("space.transition.evidence", EventRoute.ToAuthority);
		public event Action<SpaceTransitionCommand, MapSpace> Received = delegate { };
		public event Action<ulong, SpaceTransitionEvidence> Validated = delegate { };
		public SpaceTransitionCommand Current => command.Value;
		public void Register()
		{
			references.Register(); command.Validate = (_, _) => false; command.Register();
			command.Changed += OnChanged; command.Synced += OnSynced;
			evidence.Register(); evidence.Received += OnEvidence;
		}
		public void Unregister()
		{
			command.Changed -= OnChanged; command.Synced -= OnSynced; command.Unregister(); references.Unregister();
			evidence.Received -= OnEvidence; evidence.Unregister();
		}
		private void OnChanged(SpaceTransitionCommand _, SpaceTransitionCommand value)
		{ if (SyncBus.Active && !SyncBus.IsAuthority) Received.Invoke(value, value.operation == Guid.Empty ? null : references.Read(value.space)); }
		private void OnSynced() => OnChanged(default, command.Value);
		private void OnEvidence(ulong sender, SpaceTransitionEvidence value) => Validated.Invoke(sender, value);
		public void Report(SpaceTransitionEvidence value) => evidence.Raise(value);
		public void Publish(SpaceTransitionCommand value, MapSpace candidate)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			if (candidate != null)
			{
				references.Stage(candidate);
				value.space = new MapIdentity {
					spaceId = Guid.Parse(candidate.id), frameId = Guid.Parse(candidate.canonicalFrameId), spaceVersion = value.revision,
					tagSizeCm = candidate.tagSizeCm, firstTagId = candidate.firstTagId, secondTagId = candidate.secondTagId,
					preferredColocationMethod = candidate.preferredColocationMethod
				};
				value.space.spaceName.CopyFromTruncated(candidate.name ?? "");
			}
			command.Value = value;
		}
	}
}
