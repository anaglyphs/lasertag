using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// A space's visual map on disk: <c>&lt;spaceId&gt;.vmap</c> beside its file, in the space's
	/// frame, and one vocabulary for the whole catalog (<c>vocabulary.vvoc</c>) that every
	/// map's place probes are made with. Saving snapshots the live map on the main thread and
	/// writes on a worker; loading replaces the live map as one rigid piece and has the rig
	/// relocalize against it, so the world frame comes back where the space left it and the
	/// map's objects land where they were. The caller decides whose map it is to save.
	/// </summary>
	public sealed class VisualMapPersistence
	{
		private const int VocabularySample = 30000;

		private readonly SpaceStore store;
		private VisualVocabulary vocabulary;
		private bool vocabularyLooked;
		private int savedRevision = -1;
		private string savedSpace;
		private float lastSave = float.NegativeInfinity;
		// writes in flight gate only unforced saves: a forced one (leaving a space, pausing)
		// always gets through, and the sequence numbers decide which write is the file
		private int writesInFlight;
		private int writeSequence;
		private int writtenSequence;
		private int recordedSequence;
		private readonly object commitLock = new();
		// what was loaded for the current space: a live map smaller than that is a fresh one
		// after a load that failed, and must not overwrite the file the loaded one came from
		private string loadedSpace;
		private int loadedKeyframes;
		// a space whose saved map has not relocalized the rig yet: the live map may be of
		// another room entirely, and saving it would replace the space's map with that room
		private string unverifiedSpace;
		// each keyframe's place probe once made: a keyframe never changes, so a synchronous
		// save (quitting, pausing) reuses them instead of quantizing the whole map again
		private readonly Dictionary<VisualKeyframe, ulong[]> probeCache = new();

		/// <summary>Seconds between unforced saves of a map that keeps growing.</summary>
		public float SaveInterval = 60f;
		/// <summary>Seconds a loaded map gets to relocalize the rig before it is dropped for a fresh one.</summary>
		public float RelocalizeTimeout = 30f;
		/// <summary>Seconds a space just shown waits for the rig to take an alignment before its map is loaded.</summary>
		public float AlignTimeout = 10f;

		public VisualMapPersistence(SpaceStore store) => this.store = store;

		public bool HasMap(Space space) => space != null && store.HasVisualMap(space.id);

		/// <summary>The catalog's vocabulary, read once; null until the first map saved builds it.</summary>
		public VisualVocabulary Vocabulary
		{
			get
			{
				if (vocabularyLooked) return vocabulary;
				vocabularyLooked = true;
				string path = store.VocabularyPath;
				try
				{
					if (File.Exists(path))
					{
						using FileStream f = new(path, FileMode.Open, FileAccess.Read);
						vocabulary = VisualVocabulary.Read(f);
						if (vocabulary == null) Debug.LogWarning($"[{nameof(VisualMapPersistence)}] {path} is not a vocabulary: ignored");
					}
				}
				catch (Exception e) { Debug.LogException(e); }
				return vocabulary;
			}
		}

		/// <summary>A space's map header (its place probes), or null without one.</summary>
		public VisualMapHeader ReadHeader(string spaceId)
		{
			string path = store.VisualMapPath(spaceId);
			if (!File.Exists(path)) return null;
			try
			{
				using FileStream f = new(path, FileMode.Open, FileAccess.Read);
				return VisualMapFile.ReadHeader(f);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return null;
			}
		}

		// ---- saving ------------------------------------------------------------

		/// <summary>
		/// Saves the live map under the space while the rig is aligned in the space's frame.
		/// Unforced saves are skipped while the map is unchanged, saved within
		/// <see cref="SaveInterval"/>, or still being written; a forced save always writes, and
		/// a synchronous one writes before returning (quitting, pausing), without building a
		/// vocabulary. Each write has its own temp file and sequence number, so an older write
		/// finishing after a newer one neither replaces the file nor rolls the record back.
		/// </summary>
		public void Save(Space space, bool force, bool synchronous)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (space == null || localizer == null || provider == null) return;
			if (provider.CurrentPhase != CanonColocationProvider.Phase.Aligned) return;
			VisualMap map = localizer.Map;
			if (map.Count == 0 || writesInFlight > 0 && !force) return;
			if (space.id == loadedSpace && map.Count < loadedKeyframes || space.id == unverifiedSpace) return;
			bool unchanged = map.Revision == savedRevision && space.id == savedSpace;
			if (unchanged || !force && Time.realtimeSinceStartup - lastSave < SaveInterval) return;

			VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, false);
			data.frameId = space.frameId;
			data.savedTicks = DateTime.UtcNow.Ticks;
			// the snapshot's keyframes in the map's order: their probes, where already made
			List<VisualKeyframe> sources = new(map.Keyframes);
			for (int i = 0; i < sources.Count && i < data.keyframes.Count; i++)
				if (probeCache.TryGetValue(sources[i], out ulong[] probe)) data.keyframes[i].probe = probe;
			int revision = map.Revision;
			int sequence = ++writeSequence;
			bool mayBuildVocabulary = writesInFlight == 0;
			lastSave = Time.realtimeSinceStartup;
			string path = store.VisualMapPath(space.id);
			if (synchronous)
			{
				writesInFlight++;
				try
				{
					WriteMap(data, path, sequence, Vocabulary, false, out bool landed);
					Record(landed, sequence, sources, data, revision, space.id);
				}
				catch (Exception e) { Debug.LogException(e); }
				finally { writesInFlight--; }
			}
			else WriteAsync(data, path, sequence, revision, space.id, sources, mayBuildVocabulary);
		}

		private async void WriteAsync(VisualMapData data, string path, int sequence, int revision, string spaceId,
			List<VisualKeyframe> sources, bool mayBuildVocabulary)
		{
			writesInFlight++;
			try
			{
				VisualVocabulary v = Vocabulary;
				bool hadVocabulary = v != null;
				bool landed;
				await Awaitable.BackgroundThreadAsync();
				try
				{
					v = WriteMap(data, path, sequence, v, mayBuildVocabulary, out landed);
				}
				finally
				{
					await Awaitable.MainThreadAsync();
				}
				if (v != null) vocabulary = v;
				if (landed) Record(true, sequence, sources, data, revision, spaceId);
				// a vocabulary was built for a file that lost the race: the newer file has no probes yet
				else if (!hadVocabulary && v != null) savedRevision = -1;
			}
			catch (Exception e) { Debug.LogException(e); }
			finally { writesInFlight--; }
		}

		// the record follows the newest write that is the file; an older one landing later leaves it
		private void Record(bool landed, int sequence, List<VisualKeyframe> sources, VisualMapData data, int revision, string spaceId)
		{
			if (!landed || sequence < recordedSequence) return;
			recordedSequence = sequence;
			Remember(sources, data);
			savedRevision = revision;
			savedSpace = spaceId;
		}

		private void Remember(List<VisualKeyframe> sources, VisualMapData data)
		{
			probeCache.Clear();
			for (int i = 0; i < sources.Count && i < data.keyframes.Count; i++)
				if (data.keyframes[i].probe != null) probeCache[sources[i]] = data.keyframes[i].probe;
		}

		// any thread: builds the vocabulary first if there is none and it may, probes the
		// keyframes with it, and writes the file through its own temp name
		private VisualVocabulary WriteMap(VisualMapData data, string path, int sequence, VisualVocabulary v, bool mayBuildVocabulary, out bool landed)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			if (v == null && mayBuildVocabulary)
			{
				v = BuildVocabulary(data);
				if (v != null)
				{
					string vocabularyPath = store.VocabularyPath;
					lock (commitLock)
					{
						using (FileStream f = new(vocabularyPath + ".tmp", FileMode.Create, FileAccess.Write)) v.Write(f);
						SpaceStore.ReplaceFile(vocabularyPath + ".tmp", vocabularyPath);
					}
					Debug.Log($"[{nameof(VisualMapPersistence)}] built the catalog's vocabulary from {data.keyframes.Count} keyframes");
				}
			}

			if (v != null)
			{
				data.vocabulary = v.Id;
				foreach (VisualMapData.Keyframe k in data.keyframes)
					if (k.probe == null) k.probe = v.Bitset(k.descriptors, k.count);
			}
			else foreach (VisualMapData.Keyframe k in data.keyframes) k.probe = null;

			string temp = $"{path}.{sequence}.tmp";
			using (FileStream f = new(temp, FileMode.Create, FileAccess.Write)) VisualMapFile.Write(data, f);
			landed = Commit(temp, path, sequence);
			if (landed)
			{
				long size = new FileInfo(path).Length;
				Debug.Log($"[{nameof(VisualMapPersistence)}] saved {data.keyframes.Count} keyframes, {data.landmarks.Count} landmarks ({size / 1024} KB){(v == null ? ", no vocabulary yet: no place probes" : "")}");
			}
			return v;
		}

		/// <summary>Makes the temp file the map unless a newer write already did; then the temp is discarded.</summary>
		private bool Commit(string temp, string path, int sequence)
		{
			lock (commitLock)
			{
				if (sequence < writtenSequence)
				{
					File.Delete(temp);
					return false;
				}
				SpaceStore.ReplaceFile(temp, path);
				writtenSequence = sequence;
				return true;
			}
		}

		// a sample of the map's descriptors, spread over its keyframes
		private static VisualVocabulary BuildVocabulary(VisualMapData data)
		{
			const int d = FeatureSet.DescriptorSize;
			int total = 0;
			foreach (VisualMapData.Keyframe k in data.keyframes) total += k.count;
			if (total < VisualVocabulary.Words) return null;
			List<half[]> sample = new(math.min(total, VocabularySample));
			Unity.Mathematics.Random random = new(7);
			float keep = math.min(1f, (float)VocabularySample / total);
			foreach (VisualMapData.Keyframe k in data.keyframes)
				for (int i = 0; i < k.count; i++)
				{
					if (random.NextFloat() > keep) continue;
					half[] row = new half[d];
					Array.Copy(k.descriptors, i * d, row, 0, d);
					sample.Add(row);
				}
			return VisualVocabulary.Build(sample);
		}

		// ---- loading -----------------------------------------------------------

		/// <summary>
		/// Loads the space's saved map, if it has one in the space's current frame, under the
		/// live localizer as one rigid piece, and has the rig relocalize against it. Gives up
		/// after <see cref="RelocalizeTimeout"/>, leaving a fresh map, and calls
		/// <paramref name="failed"/>: the headset is likely not in the space at all. Until the
		/// map relocalizes, the space's map file is not written. Cancelled when the space stops
		/// being the shown one.
		/// </summary>
		public async void LoadAndRelocalize(Space space, CancellationToken token, Action<Space> failed = null)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			unverifiedSpace = space != null && HasMap(space) ? space.id : null;
			if (unverifiedSpace == null || localizer == null || provider == null) return;
			string path = store.VisualMapPath(space.id);

			VisualMapData data;
			try
			{
				await Awaitable.BackgroundThreadAsync();
				try
				{
					using FileStream f = new(path, FileMode.Open, FileAccess.Read);
					data = VisualMapFile.Read(f);
				}
				finally
				{
					await Awaitable.MainThreadAsync();
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				Verified(space);
				return;
			}

			if (token.IsCancellationRequested || !localizer || !provider) return;
			// an unreadable map, or one of another frame, has nothing to protect: a fresh one replaces it
			if (data == null)
			{
				Debug.LogWarning($"[{nameof(VisualMapPersistence)}] {path} is not a map: ignored");
				Verified(space);
				return;
			}
			if (data.frameId != space.frameId)
			{
				Debug.Log($"[{nameof(VisualMapPersistence)}] the saved map is in another frame than the space: ignored");
				Verified(space);
				return;
			}
			try
			{
				float alignDeadline = Time.realtimeSinceStartup + AlignTimeout;
				while (provider && provider.CurrentPhase != CanonColocationProvider.Phase.Aligned && Time.realtimeSinceStartup < alignDeadline)
					await Awaitable.WaitForSecondsAsync(0.25f, token);
			}
			catch (OperationCanceledException) { return; }
			if (!localizer || !provider) return;
			if (provider.CurrentPhase != CanonColocationProvider.Phase.Aligned)
			{
				Debug.Log($"[{nameof(VisualMapPersistence)}] the rig took no alignment in {AlignTimeout:F0} s ({provider.CurrentPhase}): the saved map for space {space.id} is not loaded, and not replaced");
				return;
			}

			// whatever was mapped since the space came up is in a frame about to be replaced
			localizer.Map.Restore(data, pose => pose, Quaternion.identity, localizer.PixelSigma);
			savedRevision = localizer.Map.Revision;
			savedSpace = space.id;
			loadedSpace = space.id;
			loadedKeyframes = localizer.Map.Count;
			probeCache.Clear();
			if (data.vocabulary != 0 && data.vocabulary == Vocabulary?.Id)
			{
				// restored in the file's order, less the views too sparse to restore
				IReadOnlyList<VisualKeyframe> restored = localizer.Map.Keyframes;
				int r = 0;
				foreach (VisualMapData.Keyframe k in data.keyframes)
				{
					if (k.count < LighterGlueMatcher.MinKeypoints) continue;
					if (r >= restored.Count) break;
					if (k.probe != null) probeCache[restored[r]] = k.probe;
					r++;
				}
			}
			Debug.Log($"[{nameof(VisualMapPersistence)}] loaded {localizer.Map.Count} keyframes for space {space.id}: {localizer.Quality}; relocalizing");
			if (!provider.RelocalizeAgainstMap())
			{
				Debug.Log($"[{nameof(VisualMapPersistence)}] couldn't relocalize against the saved map for space {space.id}: it is not replaced");
				return;
			}

			// startup clears the scan and rebases the frame while the session comes up, and the
			// provider drops its map with them: put the loaded map back while it is still wanted
			int restores = 0;
			float deadline = Time.realtimeSinceStartup + RelocalizeTimeout;
			try
			{
				while (Time.realtimeSinceStartup < deadline)
				{
					await Awaitable.WaitForSecondsAsync(0.5f, token);
					if (!provider) return;
					if (provider.CurrentPhase != CanonColocationProvider.Phase.Relocalizing)
					{
						if (provider.CurrentPhase == CanonColocationProvider.Phase.Aligned) Verified(space);
						return;
					}
					if (localizer.Map.Count >= loadedKeyframes / 2 || restores >= 3) continue;
					restores++;
					localizer.Map.Restore(data, pose => pose, Quaternion.identity, localizer.PixelSigma);
					savedRevision = localizer.Map.Revision;
					Debug.Log($"[{nameof(VisualMapPersistence)}] the loaded map was cleared while relocalizing: restored ({restores})");
				}
			}
			catch (OperationCanceledException) { return; }

			if (provider.CurrentPhase != CanonColocationProvider.Phase.Relocalizing) return;
			Debug.Log($"[{nameof(VisualMapPersistence)}] the saved map didn't relocalize in {RelocalizeTimeout:F0} s: starting a fresh one ({provider.LastAttemptDescription})");
			provider.AbandonRelocalization();
			localizer.Map.Clear();
			failed?.Invoke(space);
		}

		private void Verified(Space space)
		{
			if (unverifiedSpace == space.id) unverifiedSpace = null;
		}

		/// <summary>Forgets what was saved: the next unforced save writes.</summary>
		public void Invalidate() => savedRevision = -1;
	}
}
