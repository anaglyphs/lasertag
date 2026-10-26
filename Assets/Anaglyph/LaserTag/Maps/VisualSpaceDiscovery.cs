using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.XR.VisualLocalization;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Which saved space the camera is in, told from the view: every space's saved views are
	/// ranked against frames captured as the user looks around, by the words they share
	/// (<see cref="VisualPlaceIndex"/>), and the votes add up over a few frames. Cheap enough
	/// to run across every space; the match is confirmed by relocalizing against that
	/// space's map once it is activated, which is where a wrong guess is caught.
	/// </summary>
	internal sealed class VisualSpaceDiscovery
	{
		private readonly VisualMapPersistence maps;
		private readonly Func<bool> trackingReady;
		private readonly Dictionary<string, float> lastScores = new();
		private bool probing;

		/// <summary>Average per-frame score a space needs to be the one: the sum of its best three views' shares of the frame's words (a view of the same place scores ~0.5, one of another ~0.06).</summary>
		public float MinScore = 0.3f;
		/// <summary>How far ahead of the runner-up the leader must be.</summary>
		public float LeadRatio = 1.5f;
		public int MinFrames = 3, MaxFrames = 10;
		public float FrameInterval = 0.3f;
		public int MinFeatures = 100;
		/// <summary>Seconds a probe looks before giving up for now.</summary>
		public float MaxSeconds = 6f;

		public VisualSpaceDiscovery(VisualMapPersistence maps, Func<bool> trackingReady)
		{
			this.maps = maps;
			this.trackingReady = trackingReady;
		}

		/// <summary>Each candidate space's average score over the last probe's frames.</summary>
		public IReadOnlyDictionary<string, float> LastScores => lastScores;

		public async Awaitable<SpaceProbeResult> ProbeAsync(IReadOnlyList<MapSpace> spaces, CancellationToken token)
		{
			if (probing) return new(SpaceProbeOutcome.Indeterminate);
			List<MapSpace> candidates = new();
			foreach (MapSpace space in spaces)
				if (maps.HasMap(space)) candidates.Add(space);
			if (candidates.Count == 0) return new(SpaceProbeOutcome.NoMatches);

			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null || !localizer.IsAvailable || !trackingReady()) return new(SpaceProbeOutcome.Indeterminate);

			// one space, or nothing to rank them by: the most recent, and relocalizing decides
			VisualVocabulary vocabulary = maps.Vocabulary;
			if (candidates.Count == 1 || vocabulary == null) return new(SpaceProbeOutcome.Matches, candidates[0]);

			VisualPlaceIndex index = new();
			List<MapSpace> unscorable = new();
			foreach (MapSpace space in candidates)
			{
				VisualMapHeader header = maps.ReadHeader(space.id);
				if (header == null || header.vocabulary != vocabulary.Id || header.keyframes == 0)
				{
					unscorable.Add(space);
					continue;
				}

				foreach (ulong[] bits in header.probes) index.Add(space.id, bits);
			}

			if (index.Count == 0) return new(SpaceProbeOutcome.Matches, candidates[0]);

			probing = true;
			bool wasUrgent = localizer.Urgent;
			localizer.Urgent = true;
			try
			{
				Dictionary<string, float> votes = new();
				int frames = 0;
				float deadline = Time.realtimeSinceStartup + MaxSeconds;
				while (frames < MaxFrames && Time.realtimeSinceStartup < deadline)
				{
					if (token.IsCancellationRequested) return new(SpaceProbeOutcome.Canceled);
					VisualQuery query = await localizer.CaptureQueryAsync();
					if (token.IsCancellationRequested) return new(SpaceProbeOutcome.Canceled);
					if (query?.features != null && query.features.Count >= MinFeatures)
					{
						ulong[] bits = vocabulary.Bitset(query.features.descriptors, query.features.Count);
						foreach ((string id, float score) in index.ScoreSpaces(bits))
						{
							votes.TryGetValue(id, out float sum);
							votes[id] = sum + score;
						}

						frames++;
						if (frames >= MinFrames && Leader(votes, frames, out MapSpace best, candidates, true) != null)
							return Report(best, candidates, frames, votes);
					}

					await Awaitable.WaitForSecondsAsync(FrameInterval, token);
				}

				if (frames < MinFrames) return new(SpaceProbeOutcome.Indeterminate);
				if (Leader(votes, frames, out MapSpace leader, candidates, false) != null) return Report(leader, candidates, frames, votes);

				// nothing scored looks like here; a map saved without probes might still be
				if (unscorable.Count > 0) return Report(unscorable[0], candidates, frames, votes);
				Report(null, candidates, frames, votes);
				return new(SpaceProbeOutcome.NoMatches);
			}
			catch (OperationCanceledException) { return new(SpaceProbeOutcome.Canceled); }
			finally
			{
				localizer.Urgent = wasUrgent;
				probing = false;
			}
		}

		// the space ahead by enough, over the frames so far; with needLead, also ahead of the
		// runner-up by the ratio (an early decision asks for more)
		private MapSpace Leader(Dictionary<string, float> votes, int frames, out MapSpace best, List<MapSpace> candidates, bool needLead)
		{
			best = null;
			string leader = null;
			float first = 0, second = 0;
			foreach ((string id, float sum) in votes)
			{
				float average = sum / frames;
				if (average > first)
				{
					second = first;
					first = average;
					leader = id;
				}
				else if (average > second) second = average;
			}

			if (leader == null || first < MinScore) return null;
			if (needLead && second > 0 && first < LeadRatio * second) return null;
			best = candidates.Find(s => s.id == leader);
			return best;
		}

		private SpaceProbeResult Report(MapSpace best, List<MapSpace> candidates, int frames, Dictionary<string, float> votes)
		{
			lastScores.Clear();
			foreach (MapSpace space in candidates)
				lastScores[space.id] = votes.TryGetValue(space.id, out float sum) && frames > 0 ? sum / frames : 0;
			Debug.Log($"[{nameof(VisualSpaceDiscovery)}] {frames} frames: {(best != null ? $"space {best.id} ({best.name})" : "no saved space")} {Describe()}");
			return best != null ? new(SpaceProbeOutcome.Matches, best) : new(SpaceProbeOutcome.NoMatches);
		}

		private string Describe()
		{
			List<string> parts = new();
			foreach ((string id, float score) in lastScores) parts.Add($"{id.Substring(0, 8)}={score:F2}");
			return string.Join(" ", parts);
		}
	}
}
