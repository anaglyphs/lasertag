using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Works out which saved maps belong to the physical space the headset is standing in, by
	/// asking the runtime which of each map's anchors localize here. A non-empty result is a
	/// strong hint rather than proof — anchors saved in one room occasionally localize in
	/// another — so the answer feeds auto-load and the map picker, not a hard gate.
	///
	/// A map with no anchors cannot be tested this way and always scores zero. That covers a tag
	/// map on a device that has never realized any of its tags: nothing about it is knowable from
	/// here until it sees one.
	/// </summary>
	internal sealed class MapDiscovery
	{
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly float probeTimeoutSeconds;
		private readonly Dictionary<string, int> results = new();

		public MapDiscovery(SpatialAnchorColocationConstraintProvider anchorColocationProvider, float probeTimeoutSeconds)
		{
			this.anchorColocationProvider = anchorColocationProvider;
			this.probeTimeoutSeconds = probeTimeoutSeconds;
		}

		public IReadOnlyDictionary<string, int> Results => results;
		public event Action ResultsChanged = delegate { };

		public bool IsAvailable => anchorColocationProvider && anchorColocationProvider.IsAvailable;

		public void Forget(string mapId)
		{
			if (results.Remove(mapId))
				ResultsChanged.Invoke();
		}

		/// <summary>
		/// Probes saved maps most-recently-used first and returns the first one that localized,
		/// or null if none did.
		///
		/// With <paramref name="stopAtFirstLocalized"/> the walk ends at that map, so the maps
		/// behind it keep whatever <see cref="Results"/> they already had — a full pass is what
		/// refreshes every entry.
		/// </summary>
		public async Awaitable<GameMap> ProbeAsync(bool stopAtFirstLocalized, CancellationToken ctkn)
		{
			if (!IsAvailable)
				return null;

			// Without a successful enumeration, absence from the device's saved set means "not
			// known" rather than "not there", and every anchor has to stay a candidate.
			bool savedAnchorsKnown = await anchorColocationProvider.RefreshSavedAnchorsAsync(ctkn);

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();

				// A fresh list per map: it outlives this frame inside the probe, so a reused
				// buffer would depend on when the provider happens to copy it.
				List<Guid> candidates = CollectCandidates(map, savedAnchorsKnown);

				if (candidates.Count == 0)
				{
					Record(map.id, 0);
					continue;
				}

				HashSet<Guid> localized = await anchorColocationProvider.ProbeAsync(
					candidates, probeTimeoutSeconds, ctkn);

				Record(map.id, localized.Count);

				if (localized.Count == 0)
					continue;

				if (stopAtFirstLocalized)
					return map;
			}

			return null;
		}

		private void Record(string mapId, int localizedCount)
		{
			results[mapId] = localizedCount;
			ResultsChanged.Invoke();
		}

		/// <summary>
		/// Which of a map's anchors are worth asking the runtime about. An anchor this device has
		/// no local save of cannot localize, and filtering it out spares the metadata fetch that
		/// would otherwise be spent per map learning that — a map authored on another headset
		/// costs a round trip before discovery moves on.
		///
		/// Only a filter where the device could actually be enumerated; see
		/// <c>savedAnchorsKnown</c> above.
		/// </summary>
		private List<Guid> CollectCandidates(GameMap map, bool savedAnchorsKnown)
		{
			List<Guid> candidates = new(map.anchors.Count);

			foreach (MapAnchorEntry entry in map.anchors)
			{
				if (!MapGuid.TryParse(entry.guid, out Guid guid))
					continue;

				if (savedAnchorsKnown && !anchorColocationProvider.IsAnchorSaved(guid))
					continue;

				candidates.Add(guid);
			}

			return candidates;
		}
	}
}
