using System;
using System.Collections.Generic;
using System.IO;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// A space's visual map on disk: <c>&lt;spaceId&gt;.vmap</c> beside its document, in its
	/// storage frame, and one vocabulary for the whole catalog (<c>vocabulary.vvoc</c>) that
	/// every map's place probes are made with. Saving snapshots the live map on the main
	/// thread and writes on a worker; loading replaces the live map as one rigid piece and
	/// has the rig relocalize against it, so the world frame comes back where the space
	/// left it and the map's objects land where they were.
	/// </summary>
	internal sealed class VisualMapPersistence
	{
		public const string VocabularyFile = "vocabulary.vvoc";
		private const int VocabularySample = 30000;

		private readonly string directory;
		private readonly Func<bool> authority;
		private VisualVocabulary vocabulary;
		private bool vocabularyLooked;
		private int savedRevision = -1;
		private string savedSpace;
		private float lastSave = float.NegativeInfinity;
		private bool saving;
		private int loadGeneration;
		// what was loaded for the current space: a live map smaller than that is a fresh one
		// after a load that failed, and must not overwrite the file the loaded one came from
		private string loadedSpace;
		private int loadedKeyframes;
		// each keyframe's place probe once made: a keyframe never changes, so a synchronous
		// save (quitting, pausing) reuses them instead of quantizing the whole map again
		private readonly Dictionary<VisualKeyframe, ulong[]> probeCache = new();

		/// <summary>Seconds between unforced saves of a map that keeps growing.</summary>
		public float SaveInterval = 60f;
		/// <summary>Seconds a loaded map gets to relocalize the rig before it is dropped for a fresh one.</summary>
		public float RelocalizeTimeout = 30f;
		/// <summary>Told after each map written: the space's id and the save's UTC ticks.</summary>
		public Action<string, long> Saved;

		public VisualMapPersistence(string directory, Func<bool> authority)
		{
			this.directory = directory;
			this.authority = authority;
		}

		public static string MapPath(string directory, string spaceId) => Path.Combine(directory, spaceId + VisualMapFile.Extension);
		public string MapPath(string spaceId) => MapPath(directory, spaceId);
		public bool HasMap(MapSpace space) => space != null && MapSpace.ValidId(space.id) && File.Exists(MapPath(space.id));

		/// <summary>Removes a space's map file (and the temp file a save may have left).</summary>
		public static void DeleteFiles(string directory, string spaceId)
		{
			foreach (string path in new[] { MapPath(directory, spaceId), MapPath(directory, spaceId) + ".tmp" })
			{
				try { File.Delete(path); }
				catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.LogException(e); }
			}
		}

		/// <summary>The catalog's vocabulary, read once; null until the first map saved builds it.</summary>
		public VisualVocabulary Vocabulary
		{
			get
			{
				if (vocabularyLooked) return vocabulary;
				vocabularyLooked = true;
				string path = Path.Combine(directory, VocabularyFile);
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
			string path = MapPath(spaceId);
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
		/// Saves the live map under the space when it is this headset's to save: the
		/// authority's, aligned, in the space's frame. Unforced saves are skipped while the
		/// map is unchanged or saved within <see cref="SaveInterval"/>; a synchronous save
		/// writes before returning (quitting, pausing), without building a vocabulary.
		/// </summary>
		public void Save(MapSpace space, bool force, bool synchronous)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (space == null || !MapSpace.ValidId(space.id) || localizer == null || provider == null || !authority()) return;
			if (provider.CurrentPhase != CanonColocationProvider.Phase.Aligned) return;
			VisualMap map = localizer.Map;
			if (map.Count == 0 || saving) return;
			if (space.id == loadedSpace && map.Count < loadedKeyframes) return;
			bool unchanged = map.Revision == savedRevision && space.id == savedSpace;
			if (!force && (unchanged || Time.realtimeSinceStartup - lastSave < SaveInterval)) return;
			if (unchanged && !force) return;

			MapSpaceFrame frame = space.Frame;
			VisualMapData data = VisualMapFile.Snapshot(map, frame.ToStorage, frame.StorageFromCanonical.rotation, false);
			data.frameId = space.storageFrameId;
			data.savedTicks = DateTime.UtcNow.Ticks;
			// the snapshot's keyframes in the map's order: their probes, where already made
			List<VisualKeyframe> sources = new(map.Keyframes);
			for (int i = 0; i < sources.Count && i < data.keyframes.Count; i++)
				if (probeCache.TryGetValue(sources[i], out ulong[] probe)) data.keyframes[i].probe = probe;
			int revision = map.Revision;
			lastSave = Time.realtimeSinceStartup;
			if (synchronous)
			{
				try
				{
					WriteMap(data, MapPath(space.id), Vocabulary, false);
					Remember(sources, data);
					savedRevision = revision;
					savedSpace = space.id;
					Saved?.Invoke(space.id, data.savedTicks);
				}
				catch (Exception e) { Debug.LogException(e); }
			}
			else WriteAsync(data, MapPath(space.id), revision, space.id, sources);
		}

		private async void WriteAsync(VisualMapData data, string path, int revision, string spaceId, List<VisualKeyframe> sources)
		{
			saving = true;
			try
			{
				VisualVocabulary v = Vocabulary;
				await Awaitable.BackgroundThreadAsync();
				try
				{
					v = WriteMap(data, path, v, true);
				}
				finally
				{
					await Awaitable.MainThreadAsync();
				}

				if (v != null) vocabulary = v;
				Remember(sources, data);
				savedRevision = revision;
				savedSpace = spaceId;
				Saved?.Invoke(spaceId, data.savedTicks);
			}
			catch (Exception e) { Debug.LogException(e); }
			finally { saving = false; }
		}

		private void Remember(List<VisualKeyframe> sources, VisualMapData data)
		{
			probeCache.Clear();
			for (int i = 0; i < sources.Count && i < data.keyframes.Count; i++)
				if (data.keyframes[i].probe != null) probeCache[sources[i]] = data.keyframes[i].probe;
		}

		// any thread: builds the vocabulary first if there is none and it may, probes the
		// keyframes with it, and writes the file through a temp name
		private VisualVocabulary WriteMap(VisualMapData data, string path, VisualVocabulary v, bool mayBuildVocabulary)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			if (v == null && mayBuildVocabulary)
			{
				v = BuildVocabulary(data);
				if (v != null)
				{
					string vocabularyPath = Path.Combine(directory, VocabularyFile);
					using FileStream f = new(vocabularyPath + ".tmp", FileMode.Create, FileAccess.Write);
					v.Write(f);
					f.Close();
					Replace(vocabularyPath + ".tmp", vocabularyPath);
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

			using (FileStream f = new(path + ".tmp", FileMode.Create, FileAccess.Write)) VisualMapFile.Write(data, f);
			Replace(path + ".tmp", path);
			long size = new FileInfo(path).Length;
			Debug.Log($"[{nameof(VisualMapPersistence)}] saved {data.keyframes.Count} keyframes, {data.landmarks.Count} landmarks ({size / 1024} KB){(v == null ? ", no vocabulary yet: no place probes" : "")}");
			return v;
		}

		private static void Replace(string temp, string path)
		{
			if (File.Exists(path)) File.Delete(path);
			File.Move(temp, path);
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
		/// live localizer as one rigid piece, and has the rig relocalize against it. Gives
		/// up after <see cref="RelocalizeTimeout"/>, leaving a fresh map.
		/// </summary>
		public async void LoadAndRelocalize(MapSpace space)
		{
			int generation = ++loadGeneration;
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (space == null || !HasMap(space) || localizer == null || provider == null || !authority()) return;
			string path = MapPath(space.id);

			VisualMapData data = null;
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
				return;
			}

			if (generation != loadGeneration || !localizer || !provider) return;
			if (data == null)
			{
				Debug.LogWarning($"[{nameof(VisualMapPersistence)}] {path} is not a map: ignored");
				return;
			}

			if (data.frameId != space.storageFrameId)
			{
				Debug.Log($"[{nameof(VisualMapPersistence)}] the saved map is in another frame than the space: ignored");
				return;
			}

			if (!authority() || provider.CurrentPhase != CanonColocationProvider.Phase.Aligned) return;

			// whatever was mapped since the space came up is in a frame about to be replaced
			MapSpaceFrame frame = space.Frame;
			localizer.Map.Restore(data, frame.ToCanonical, frame.CanonicalFromStorage.rotation, localizer.PixelSigma);
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
			if (!provider.RelocalizeAgainstMap()) return;

			// startup clears the scan and rebases the frame while the session comes up, and the
			// provider drops its map with them: put the loaded map back while it is still wanted
			int restores = 0;
			float deadline = Time.realtimeSinceStartup + RelocalizeTimeout;
			while (Time.realtimeSinceStartup < deadline)
			{
				await Awaitable.WaitForSecondsAsync(0.5f);
				if (generation != loadGeneration || !provider) return;
				if (provider.CurrentPhase != CanonColocationProvider.Phase.Relocalizing) return;
				if (localizer.Map.Count >= loadedKeyframes / 2 || restores >= 3) continue;
				restores++;
				localizer.Map.Restore(data, frame.ToCanonical, frame.CanonicalFromStorage.rotation, localizer.PixelSigma);
				savedRevision = localizer.Map.Revision;
				Debug.Log($"[{nameof(VisualMapPersistence)}] the loaded map was cleared while relocalizing: restored ({restores})");
			}

			if (generation != loadGeneration || provider.CurrentPhase != CanonColocationProvider.Phase.Relocalizing) return;
			Debug.Log($"[{nameof(VisualMapPersistence)}] the saved map didn't relocalize in {RelocalizeTimeout:F0} s: starting a fresh one ({provider.LastAttemptDescription})");
			provider.AbandonRelocalization();
			localizer.Map.Clear();
		}

		/// <summary>Forgets what was saved: the next unforced save writes.</summary>
		public void Invalidate() => savedRevision = -1;

		/// <summary>
		/// Saves the current space's map every <see cref="SaveInterval"/> while it grows: the
		/// documents are saved on edits, but a map grows by walking. Runs until cancelled.
		/// </summary>
		public async void RunPeriodicSaves(Func<MapSpace> current, System.Threading.CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					await Awaitable.WaitForSecondsAsync(10f, token);
					Save(current(), false, false);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}
	}
}
