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

	public static class SpaceRanking
	{
		/// <summary>Of the spaces scoring at least <paramref name="minimum"/>, the one with the highest priority, then the highest score; null when none does.</summary>
		public static string Best(IReadOnlyDictionary<string, float> scores, float minimum, Func<string, int> priority = null)
		{
			string best = null;
			int bestPriority = 0;
			float bestScore = 0;
			foreach ((string id, float score) in scores)
			{
				if (score < minimum) continue;
				int idPriority = priority?.Invoke(id) ?? 0;
				if (best != null && (idPriority < bestPriority || idPriority == bestPriority && score <= bestScore)) continue;
				best = id;
				bestPriority = idPriority;
				bestScore = score;
			}
			return best;
		}

		/// <summary>Of the <paramref name="among"/> highest-scoring spaces, the one with the highest priority, then the highest score; null when there are none.</summary>
		public static string BestOfTop(IReadOnlyDictionary<string, float> scores, int among, Func<string, int> priority = null)
		{
			List<KeyValuePair<string, float>> ranked = new(scores);
			ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
			if (ranked.Count > among) ranked.RemoveRange(Math.Max(among, 0), ranked.Count - Math.Max(among, 0));
			return Best(new Dictionary<string, float>(ranked), float.NegativeInfinity, priority);
		}
	}

	/// <summary>The rolling average of each space's score over the last few keyframes.</summary>
	public sealed class RecognitionVotes
	{
		private readonly Queue<Dictionary<string, float>> recent = new();
		private readonly int length;

		public RecognitionVotes(int length) => this.length = length;

		public void Clear() => recent.Clear();

		/// <summary>Adds a keyframe's scores: the <see cref="SpaceRanking.Best"/> space by average over a full window, or null.</summary>
		public string Add(Dictionary<string, float> scores, float minimum, Func<string, int> priority = null)
		{
			recent.Enqueue(scores);
			if (recent.Count > length) recent.Dequeue();
			if (recent.Count < length) return null;

			Dictionary<string, float> averages = new();
			foreach (Dictionary<string, float> keyframe in recent)
				foreach ((string id, float score) in keyframe)
				{
					averages.TryGetValue(id, out float sum);
					averages[id] = sum + score / length;
				}

			return SpaceRanking.Best(averages, minimum, priority);
		}
	}

	/// <summary>
	/// Which saved space the headset is standing in. The anchor runtime is asked first which
	/// saved anchors localize here; when none can say, the camera's view is ranked against every
	/// space with a saved visual map, by the words they share, with votes added up over a few
	/// frames. The words only rank: on headset maps the space the headset stands in comes first
	/// two times in three and among the first three nearly always, but no score separates it
	/// from the others. So the probe's answer is a guess, the one with the highest
	/// <see cref="Space.Priority"/> of the few best ranked, and relocalizing against that space's
	/// map once it is shown decides: a space whose map fails to relocalize is rejected, left
	/// out of probes until the headset changes rooms, and the next probe guesses again. After
	/// <see cref="MaxGuesses"/> rejections the room is none of the saved spaces. The per-space
	/// results feed the map picker.
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

		/// <summary>Average per-keyframe score a space needs to be recognized while walking: the sum of its best three views' shares of the keyframe's words. On headset maps the space the headset stands in reaches it in a quarter to a half of views, and another space in 1 to 2 in 100.</summary>
		public float MinScore = 0.75f;
		/// <summary>Frames a visual probe ranks the spaces over.</summary>
		public int ProbeFrames = 3;
		public float FrameInterval = 0.3f;
		public int MinFeatures = 100;
		/// <summary>Seconds a visual probe waits for its frames before giving up for now.</summary>
		public float MaxSeconds = 6f;
		/// <summary>Saved spaces tried in one room before it is taken for a new one: each wrong guess costs a map load and <see cref="UnfamiliarPlaceVerdict.Settings.secondsForAGuess"/>.</summary>
		public int MaxGuesses = 3;

		private readonly RecognitionVotes votes = new(RecognitionKeyframes);
		private VisualPlaceIndex recognitionIndex;
		private string recognitionShownId;
		private int recognitionGeneration;
		/// <summary>Keyframes in a row whose average score must reach <see cref="MinScore"/> for a space to be recognized while walking.</summary>
		private const int RecognitionKeyframes = 5;

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

		// ---- while walking ------------------------------------------------------

		/// <summary>
		/// Scores a keyframe the shown space just mapped against the other saved spaces, off the
		/// main thread: the space it has looked like for <see cref="RecognitionKeyframes"/>
		/// keyframes in a row, or null. Relocalizing against that space still has the last word.
		/// </summary>
		public async Awaitable<Space> RecognizeAsync(IReadOnlyList<Space> all, Space shown, VisualKeyframe keyframe)
		{
			VisualVocabulary vocabulary = visualMaps.Vocabulary;
			if (vocabulary == null) return null;
			if (recognitionIndex == null || recognitionShownId != shown.id || recognitionGeneration != generation) await BuildRecognitionIndex(all, shown, vocabulary);
			if (recognitionIndex.Count == 0) return null;

			int operation = generation;
			VisualPlaceIndex index = recognitionIndex;
			Dictionary<string, float> scores;
			await Awaitable.BackgroundThreadAsync();
			try
			{
				scores = index.ScoreSpaces(vocabulary.Bitset(keyframe.features.descriptors, keyframe.features.Count));
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			if (operation != generation || index != recognitionIndex) return null;
			string recognized = votes.Add(scores, MinScore, id => PriorityOf(all, id));
			if (recognized == null) return null;
			Space best = Find(all, recognized);
			if (best != null) Debug.Log($"[{nameof(SpaceDiscovery)}] recognized space {best.id} ({best.name}) while walking");
			return best;
		}

		/// <summary>Drops what recognition holds in memory: nothing is being watched for.</summary>
		public void StopRecognizing()
		{
			recognitionIndex = null;
			recognitionShownId = null;
			votes.Clear();
		}

		private async Awaitable BuildRecognitionIndex(IReadOnlyList<Space> all, Space shown, VisualVocabulary vocabulary)
		{
			List<string> ids = new();
			foreach (Space space in all)
				if (space != shown && !rejected.Contains(space.id) && visualMaps.HasMap(space)) ids.Add(space.id);

			VisualPlaceIndex index = new();
			await Awaitable.BackgroundThreadAsync();
			try
			{
				foreach (string id in ids)
				{
					VisualMapHeader header = visualMaps.ReadHeader(id);
					if (header == null || header.vocabulary != vocabulary.Id) continue;
					foreach (ulong[] bits in header.probes) index.Add(id, bits);
				}
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			votes.Clear();
			recognitionIndex = index;
			recognitionShownId = shown.id;
			recognitionGeneration = generation;
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
			if (candidates.Count == 0 || rejected.Count >= MaxGuesses) return new(ProbeOutcome.NoMatches);
			candidates.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : b.lastUsed.CompareTo(a.lastUsed));

			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null || !localizer.IsAvailable || !SpaceAlignment.TrackingReady) return new(ProbeOutcome.Indeterminate);

			// nothing to rank them by (no vocabulary yet): a guess at the first by priority, then most recent
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
				while (frames < ProbeFrames && Time.realtimeSinceStartup < deadline)
				{
					if (token.IsCancellationRequested) return new(ProbeOutcome.Canceled);
					VisualQuery query = await localizer.CaptureQueryAsync();
					if (token.IsCancellationRequested) return new(ProbeOutcome.Canceled);
					if (query?.features != null && query.features.Count >= MinFeatures)
					{
						localizer.ReportFeaturesSeen(query);
						ulong[] bits = vocabulary.Bitset(query.features.descriptors, query.features.Count);
						foreach ((string id, float score) in index.ScoreSpaces(bits))
						{
							votes.TryGetValue(id, out float sum);
							votes[id] = sum + score;
						}
						frames++;
					}
					if (frames < ProbeFrames) await Awaitable.WaitForSecondsAsync(FrameInterval, token);
				}

				if (frames < ProbeFrames) return new(ProbeOutcome.Indeterminate);
				string guess = SpaceRanking.BestOfTop(votes, MaxGuesses - rejected.Count, id => PriorityOf(candidates, id));
				// a map saved without probes can't be ranked: it gets its turn after those that can
				return Report(Find(candidates, guess) ?? (unscorable.Count > 0 ? unscorable[0] : null), candidates, frames, votes);
			}
			finally { localizer.Urgent = wasUrgent; }
		}

		private static Space Find(IReadOnlyList<Space> spaces, string id)
		{
			foreach (Space space in spaces) if (space.id == id) return space;
			return null;
		}

		private static int PriorityOf(IReadOnlyList<Space> spaces, string id) => Find(spaces, id)?.Priority ?? 0;

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
			if (colocation.VisualProvider) colocation.VisualProvider.Note($"Probe: {frames} frames, {(best != null ? best.name : "no saved space")} of {candidates.Count} ({string.Join(" ", parts)})");
			return best != null ? new(ProbeOutcome.Matches, best) : new(ProbeOutcome.NoMatches);
		}
	}
}
