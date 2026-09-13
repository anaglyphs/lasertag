using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public enum MapPresence { Unknown, Here, Elsewhere }
	public enum SpaceProbeOutcome { Matches, NoMatches, Indeterminate, Canceled }
	public readonly struct SpaceProbeResult
	{
		public readonly SpaceProbeOutcome outcome;
		public readonly MapSpace best;
		public SpaceProbeResult(SpaceProbeOutcome outcome, MapSpace best = null) { this.outcome = outcome; this.best = best; }
	}
	internal sealed class MapSpaceDiscovery
	{
		private readonly MapSpaceStore store;
		private readonly SpatialAnchorColocationConstraintProvider provider;
		private readonly float timeout;
		private readonly Func<bool> trackingReady;
		private readonly Dictionary<string, int> results = new();
		private bool probing;
		private int generation;
		public MapSpaceDiscovery(MapSpaceStore store, SpatialAnchorColocationConstraintProvider provider, float timeout, Func<bool> trackingReady)
		{ this.store = store; this.provider = provider; this.timeout = timeout; this.trackingReady = trackingReady; }
		public event Action ResultsChanged = delegate { };
		public IReadOnlyDictionary<string, int> Results => results;
		public bool IsAvailable => provider != null && provider.IsAvailable;
		public MapPresence GetPresence(string spaceId) => spaceId != null && results.TryGetValue(spaceId, out int count)
			? count > 0 ? MapPresence.Here : MapPresence.Elsewhere : MapPresence.Unknown;
		public void Invalidate() { generation++; results.Clear(); ResultsChanged.Invoke(); }
		public void Forget(string id) { generation++; if (results.Remove(id)) ResultsChanged.Invoke(); }
		public async Awaitable<SpaceProbeResult> ProbeAsync(CancellationToken token)
		{
			if (!IsAvailable || !store.IsAvailable || probing || !trackingReady()) return new(SpaceProbeOutcome.Indeterminate);
			int operation = generation;
			List<MapSpace> catalog = store.Spaces;
			HashSet<Guid> requested = new();
			foreach (var space in catalog)
				foreach (var anchor in space.AllAnchors()) if (Guid.TryParse(anchor.guid, out var guid)) requested.Add(guid);
			if (requested.Count == 0) return new(SpaceProbeOutcome.NoMatches);
			probing = true;
			try
			{
				var localized = await provider.RefreshLocalizableAsync(requested, timeout, token);
				if (token.IsCancellationRequested || operation != generation) return new(SpaceProbeOutcome.Canceled);
				if (localized == null || !trackingReady()) return new(SpaceProbeOutcome.Indeterminate);
				var result = Classify(catalog, localized, results);
				ResultsChanged.Invoke(); return result;
			}
			catch (OperationCanceledException) { return new(SpaceProbeOutcome.Canceled); }
			catch (Exception exception) { Debug.LogException(exception); return new(SpaceProbeOutcome.Indeterminate); }
			finally { probing = false; }
		}
		public static SpaceProbeResult Classify(IReadOnlyList<MapSpace> catalog, HashSet<Guid> localized, IDictionary<string, int> results)
		{
			if (localized == null) return new(SpaceProbeOutcome.Indeterminate);
			results.Clear(); MapSpace best = null;
			foreach (var space in catalog)
			{
				HashSet<Guid> ids = new();
				foreach (var anchor in space.AllAnchors()) if (Guid.TryParse(anchor.guid, out var id)) ids.Add(id);
				if (ids.Count == 0) continue;
				int count = 0; foreach (var id in ids) if (localized.Contains(id)) count++;
				results[space.id] = count;
				if (count > 0 && (best == null || space.lastUsed > best.lastUsed || space.lastUsed == best.lastUsed && string.CompareOrdinal(space.id, best.id) < 0)) best = space;
			}
			return new(best != null ? SpaceProbeOutcome.Matches : SpaceProbeOutcome.NoMatches, best);
		}

	}
}
