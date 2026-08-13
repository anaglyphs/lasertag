using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Where a map stands relative to the space the headset is in. <see cref="Unknown"/> is a
	/// third answer, not a soft no: a map nothing has been able to test — never probed, no
	/// anchors to test with, or no runtime that can answer — must not be treated as belonging
	/// somewhere else.
	/// </summary>
	public enum MapPresence
	{
		Unknown,
		Here,
		Elsewhere,
	}

	/// <summary>
	/// Works out which saved maps belong to the physical space the headset is standing in, by
	/// asking the runtime once which of this device's anchors localize here and scoring each map
	/// against that set. A non-empty result is a strong hint rather than proof — anchors saved in
	/// one room occasionally localize in another — so the answer feeds auto-load and the map
	/// picker, not a hard gate.
	///
	/// A map with no anchors cannot be tested this way at all, and comes back
	/// <see cref="MapPresence.Unknown"/> rather than scored. That covers a tag map on a device
	/// that has never realized any of its tags: nothing about it is knowable from here until it
	/// sees one, and a zero would read as a positive answer that it is somewhere else.
	/// </summary>
	internal sealed class MapDiscovery
	{
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly float probeTimeoutSeconds;
		private readonly Dictionary<string, int> results = new();
		private bool probeInFlight;

		public MapDiscovery(SpatialAnchorColocationConstraintProvider anchorColocationProvider, float probeTimeoutSeconds)
		{
			this.anchorColocationProvider = anchorColocationProvider;
			this.probeTimeoutSeconds = probeTimeoutSeconds;
		}

		public IReadOnlyDictionary<string, int> Results => results;
		public event Action ResultsChanged = delegate { };

		public bool IsAvailable => anchorColocationProvider && anchorColocationProvider.IsAvailable;

		/// <summary>
		/// What the last probe says about this map. Only a map that was actually tested — one with
		/// anchors, scored by a pass that completed — gets an answer other than
		/// <see cref="MapPresence.Unknown"/>.
		/// </summary>
		public MapPresence GetPresence(string mapId)
		{
			if (mapId == null || !results.TryGetValue(mapId, out int localized))
				return MapPresence.Unknown;

			return localized > 0 ? MapPresence.Here : MapPresence.Elsewhere;
		}

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
		/// The runtime answers one pass at a time, so a probe started while another is running does
		/// nothing rather than throwing away the answer the first one is about to produce.
		/// </summary>
		public async Awaitable<GameMap> ProbeAsync(bool stopAtFirstLocalized, CancellationToken ctkn)
		{
			if (!IsAvailable || probeInFlight)
				return null;

			probeInFlight = true;

			HashSet<Guid> localized;
			try
			{
				localized = await anchorColocationProvider.RefreshLocalizableAsync(
					probeTimeoutSeconds, ctkn);
			}
			finally
			{
				probeInFlight = false;
			}

			if (localized == null)
				return null;

			ctkn.ThrowIfCancellationRequested();

			GameMap best = null;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				// Nothing to test with means nothing was learned. Recording a zero would read as
				// "not in this room" and hide the map everywhere the score is consulted.
				if (map.anchors.Count == 0)
				{
					results.Remove(map.id);
					continue;
				}

				int localizedCount = CountLocalized(map, localized);
				results[map.id] = localizedCount;

				if (best == null && stopAtFirstLocalized && localizedCount > 0)
					best = map;
			}

			LogResults();
			ResultsChanged.Invoke();
			return best;
		}

		/// <summary>
		/// The probe decides which map the device loads on its own, so what it found has to be
		/// readable from a device log when it picks the wrong one.
		/// </summary>
		private void LogResults()
		{
			StringBuilder report = new("Map probe — anchors localizing in this space:");

			foreach (GameMap map in MapStore.GetByLastUsed())
				report.Append($"\n  {map.name}: ")
					.Append(results.TryGetValue(map.id, out int localized)
						? $"{localized}/{map.anchors.Count}"
						: "untestable (no anchors)");

			Debug.Log(report.ToString());
		}

		private static int CountLocalized(GameMap map, HashSet<Guid> localized)
		{
			int count = 0;

			foreach (MapAnchorEntry entry in map.anchors)
				if (MapGuid.TryParse(entry.guid, out Guid guid) && localized.Contains(guid))
					count++;

			return count;
		}
	}
}
