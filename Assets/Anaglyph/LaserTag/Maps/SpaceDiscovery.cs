using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Anaglyph.XR.VisualLocalization;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public enum MapPresence { Unknown, Here, Elsewhere }
	public enum ProbeOutcome { Matches, NoMatches, Indeterminate, Canceled }

	public readonly struct ProbeResult
	{
		public readonly ProbeOutcome outcome;
		public readonly Space best;
		public ProbeResult(ProbeOutcome outcome, Space best = null) { this.outcome = outcome; this.best = best; }
	}

	/// <summary>
	/// Which saved space the headset is standing in. The anchor runtime is asked first which
	/// saved anchors localize here; when none can say, the camera's view is ranked against every
	/// space with a saved visual map, by the words they share, with votes added up over a few
	/// frames; a space is only chosen by view when it scores like here, even when it is the only
	/// candidate. A visual match is confirmed by relocalizing against that space's map once it
	/// is shown, which is where a wrong guess is caught: a space whose map fails to relocalize
	/// is rejected, left out of probes until the headset changes rooms. The per-space results
	/// feed the map picker.
	/// </summary>
	public sealed class SpaceDiscovery
	{
		private readonly ColocationManager colocation;
		private readonly VisualMapPersistence visualMaps;
		private readonly float anchorTimeout;
		private readonly Dictionary<string, int> results = new();
		private readonly Dictionary<string, float> lastScores = new();
		private readonly HashSet<string> rejected = new();
		private bool probing;
		private int generation;

		/// <summary>Average per-frame score a space needs to be the one: the sum of its best three views' shares of the frame's words (a view of the same place scores ~0.5, one of another ~0.06).</summary>
		public float MinScore = 0.3f;
		/// <summary>How far ahead of the runner-up the leader must be for an early decision.</summary>
		public float LeadRatio = 1.5f;
		public int MinFrames = 3, MaxFrames = 10;
		public float FrameInterval = 0.3f;
		public int MinFeatures = 100;
		/// <summary>Seconds a visual probe looks before giving up for now.</summary>
		public float MaxSeconds = 6f;

		public SpaceDiscovery(ColocationManager colocation, VisualMapPersistence visualMaps, float anchorTimeout)
		{
			this.colocation = colocation;
			this.visualMaps = visualMaps;
			this.anchorTimeout = anchorTimeout;
		}

		private SpatialAnchorColocationConstraintProvider Anchors => colocation.AnchorProvider;
		public bool AnchorsAvailable => Anchors.IsAvailable;
		public bool SupportsEnvironmentProbing => Anchors.SupportsEnvironmentProbing;
		/// <summary>Each candidate space's average score over the last visual probe's frames.</summary>
		public IReadOnlyDictionary<string, float> LastVisualScores => lastScores;

		public MapPresence GetPresence(string spaceId) => spaceId == null ? MapPresence.Unknown
			: rejected.Contains(spaceId) ? MapPresence.Elsewhere
			: results.TryGetValue(spaceId, out int count) ? count > 0 ? MapPresence.Here : MapPresence.Elsewhere : MapPresence.Unknown;

		/// <summary>The headset moved rooms or lost its frame: nothing found so far applies.</summary>
		public void Invalidate() { generation++; results.Clear(); rejected.Clear(); }
		public void Forget(string id) { generation++; results.Remove(id); rejected.Remove(id); }
		/// <summary>The space's saved map failed to relocalize here: it is elsewhere, and probes leave it out until <see cref="Invalidate"/>.</summary>
		public void Reject(string id) { generation++; results.Remove(id); rejected.Add(id); }

		public async Awaitable<ProbeResult> ProbeAsync(IReadOnlyList<Space> all, CancellationToken token)
		{
			if (probing || !SpaceAlignment.TrackingReady) return new(ProbeOutcome.Indeterminate);
			probing = true;
			int operation = generation;
			List<Space> spaces = new();
			foreach (Space space in all) if (!rejected.Contains(space.id)) spaces.Add(space);
			try
			{
				ProbeResult byAnchors = await ProbeAnchorsAsync(spaces, token);
				if (byAnchors.outcome is ProbeOutcome.Matches or ProbeOutcome.Canceled || operation != generation) return byAnchors;
				ProbeResult byView = await ProbeVisuallyAsync(spaces, token);
				if (operation != generation) return new(ProbeOutcome.Canceled);
				if (byView.outcome == ProbeOutcome.Matches) results[byView.best.id] = 1;
				// only two answers of "nothing here" make an empty room; a runtime that could not
				// answer leaves the question open, and the probe runs again shortly
				if (byView.outcome == ProbeOutcome.NoMatches && byAnchors.outcome != ProbeOutcome.NoMatches) return new(ProbeOutcome.Indeterminate);
				return byView;
			}
			catch (OperationCanceledException) { return new(ProbeOutcome.Canceled); }
			catch (Exception e)
			{
				Debug.LogException(e);
				return new(ProbeOutcome.Indeterminate);
			}
			finally { probing = false; }
		}

		// ---- anchors -----------------------------------------------------------

		private async Awaitable<ProbeResult> ProbeAnchorsAsync(IReadOnlyList<Space> spaces, CancellationToken token)
		{
			HashSet<Guid> requested = new();
			foreach (Space space in spaces)
				foreach (AnchorRef anchor in space.anchors)
					if (Guid.TryParse(anchor.guid, out Guid guid)) requested.Add(guid);
			if (requested.Count == 0) return new(ProbeOutcome.NoMatches);
			if (!AnchorsAvailable) return new(ProbeOutcome.Indeterminate);
			int operation = generation;
			HashSet<Guid> localized = await Anchors.RefreshLocalizableAsync(requested, anchorTimeout, token);
			if (token.IsCancellationRequested || operation != generation) return new(ProbeOutcome.Canceled);
			if (localized == null || !SpaceAlignment.TrackingReady) return new(ProbeOutcome.Indeterminate);
			return Classify(spaces, localized);
		}

		private ProbeResult Classify(IReadOnlyList<Space> spaces, HashSet<Guid> localized)
		{
			results.Clear();
			Space best = null;
			foreach (Space space in spaces)
			{
				if (space.anchors.Count == 0) continue;
				int count = 0;
				foreach (AnchorRef anchor in space.anchors)
					if (Guid.TryParse(anchor.guid, out Guid guid) && localized.Contains(guid)) count++;
				results[space.id] = count;
				if (count > 0 && (best == null || space.lastUsed > best.lastUsed ||
					space.lastUsed == best.lastUsed && string.CompareOrdinal(space.id, best.id) < 0)) best = space;
			}
			return new(best != null ? ProbeOutcome.Matches : ProbeOutcome.NoMatches, best);
		}

		// ---- the camera's view -------------------------------------------------

		private async Awaitable<ProbeResult> ProbeVisuallyAsync(IReadOnlyList<Space> spaces, CancellationToken token)
		{
			List<Space> candidates = new();
			foreach (Space space in spaces)
				if (visualMaps.HasMap(space)) candidates.Add(space);
			if (candidates.Count == 0) return new(ProbeOutcome.NoMatches);

			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null || !localizer.IsAvailable || !SpaceAlignment.TrackingReady) return new(ProbeOutcome.Indeterminate);

			// nothing to rank them by (no vocabulary yet): a guess at the most recent, and
			// relocalizing decides. A single candidate is scored like any other: an unfamiliar
			// room must not take the only space there is
			VisualVocabulary vocabulary = visualMaps.Vocabulary;
			if (vocabulary == null) return new(ProbeOutcome.Matches, candidates[0]);

			VisualPlaceIndex index = new();
			List<Space> unscorable = new();
			foreach (Space space in candidates)
			{
				VisualMapHeader header = visualMaps.ReadHeader(space.id);
				if (header == null || header.vocabulary != vocabulary.Id || header.keyframes == 0)
				{
					unscorable.Add(space);
					continue;
				}
				foreach (ulong[] bits in header.probes) index.Add(space.id, bits);
			}
			if (index.Count == 0) return new(ProbeOutcome.Matches, candidates[0]);

			bool wasUrgent = localizer.Urgent;
			localizer.Urgent = true;
			try
			{
				Dictionary<string, float> votes = new();
				int frames = 0;
				float deadline = Time.realtimeSinceStartup + MaxSeconds;
				while (frames < MaxFrames && Time.realtimeSinceStartup < deadline)
				{
					if (token.IsCancellationRequested) return new(ProbeOutcome.Canceled);
					VisualQuery query = await localizer.CaptureQueryAsync();
					if (token.IsCancellationRequested) return new(ProbeOutcome.Canceled);
					if (query?.features != null && query.features.Count >= MinFeatures)
					{
						ulong[] bits = vocabulary.Bitset(query.features.descriptors, query.features.Count);
						foreach ((string id, float score) in index.ScoreSpaces(bits))
						{
							votes.TryGetValue(id, out float sum);
							votes[id] = sum + score;
						}
						frames++;
						if (frames >= MinFrames && Leader(votes, frames, candidates, needLead: true) is Space early)
							return Report(early, candidates, frames, votes);
					}
					await Awaitable.WaitForSecondsAsync(FrameInterval, token);
				}

				if (frames < MinFrames) return new(ProbeOutcome.Indeterminate);
				if (Leader(votes, frames, candidates, needLead: false) is Space leader) return Report(leader, candidates, frames, votes);
				// nothing scored looks like here; a map saved without probes might still be
				if (unscorable.Count > 0) return Report(unscorable[0], candidates, frames, votes);
				Report(null, candidates, frames, votes);
				return new(ProbeOutcome.NoMatches);
			}
			finally { localizer.Urgent = wasUrgent; }
		}

		// the space ahead by enough over the frames so far; with needLead, also ahead of the
		// runner-up by the ratio (an early decision asks for more)
		private Space Leader(Dictionary<string, float> votes, int frames, List<Space> candidates, bool needLead)
		{
			string leader = null;
			float first = 0, second = 0;
			foreach ((string id, float sum) in votes)
			{
				float average = sum / frames;
				if (average > first) { second = first; first = average; leader = id; }
				else if (average > second) second = average;
			}
			if (leader == null || first < MinScore) return null;
			if (needLead && second > 0 && first < LeadRatio * second) return null;
			return candidates.Find(s => s.id == leader);
		}

		private ProbeResult Report(Space best, List<Space> candidates, int frames, Dictionary<string, float> votes)
		{
			lastScores.Clear();
			List<string> parts = new();
			foreach (Space space in candidates)
			{
				float score = votes.TryGetValue(space.id, out float sum) && frames > 0 ? sum / frames : 0;
				lastScores[space.id] = score;
				parts.Add($"{space.id.Substring(0, 8)}={score:F2}");
			}
			Debug.Log($"[{nameof(SpaceDiscovery)}] {frames} frames: {(best != null ? $"space {best.id} ({best.name})" : "no saved space")} {string.Join(" ", parts)}");
			return best != null ? new(ProbeOutcome.Matches, best) : new(ProbeOutcome.NoMatches);
		}
	}
}
