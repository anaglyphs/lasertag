using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Works out which saved maps belong to the physical space the headset is standing in, by
	/// asking the runtime once which of this device's anchors localize here and scoring each map
	/// against that set. A non-empty result is a strong hint rather than proof — anchors saved in
	/// one room occasionally localize in another — so the answer feeds auto-load and the map
	/// picker, not a hard gate.
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
		/// Scores every saved map against the anchors that localize here, and returns the most
		/// recently used map that scored, or null if none did.
		///
		/// One pass answers for all maps at once: the runtime is asked which of this device's
		/// anchors it can find, and each map is scored by how many of its own anchors that set
		/// contains. <paramref name="stopAtFirstLocalized"/> only decides whether a map comes back
		/// to be auto-loaded — <see cref="Results"/> is refreshed for every map either way.
		///
		/// Nothing is recorded where the runtime could not answer: no score is not a score of zero.
		/// </summary>
		public async Awaitable<GameMap> ProbeAsync(bool stopAtFirstLocalized, CancellationToken ctkn)
		{
			if (!IsAvailable)
				return null;

			HashSet<Guid> localized = await anchorColocationProvider.RefreshLocalizableAsync(
				probeTimeoutSeconds, ctkn);

			if (localized == null)
				return null;

			ctkn.ThrowIfCancellationRequested();

			GameMap best = null;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				Record(map.id, CountLocalized(map, localized));

				if (best == null && stopAtFirstLocalized && results[map.id] > 0)
					best = map;
			}

			return best;
		}

		private static int CountLocalized(GameMap map, HashSet<Guid> localized)
		{
			int count = 0;

			foreach (MapAnchorEntry entry in map.anchors)
				if (MapGuid.TryParse(entry.guid, out Guid guid) && localized.Contains(guid))
					count++;

			return count;
		}

		private void Record(string mapId, int localizedCount)
		{
			results[mapId] = localizedCount;
			ResultsChanged.Invoke();
		}
	}
}
