#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Maps saved on devices, matched against each other in play mode by the localizer as it
	/// runs: each map in turn is the map, and the saved views of every other are the queries, as
	/// a headset in that place would have captured them. One CSV row per query says what the
	/// solve found, for whoever tunes recognition or the gate with real views. A saved view keeps
	/// fewer features than a live frame, so live frames match a little better than these.
	/// With a cover distance, each map is first thinned to the views <see cref="VisualMapSync"/>
	/// would share with a joiner: one per place and heading. With pool frames, the queries are
	/// each map's first views in the order they were captured, solved with the few before them as
	/// a joiner's are.
	/// </summary>
	public static class DeviceMapReplay
	{
		public static bool Running { get; private set; }
		public static string Progress { get; private set; } = "";

		/// <summary>
		/// Replays every map in <paramref name="mapPaths"/> against the others' views, <paramref name="queriesPerPair"/> spread over each.
		/// <paramref name="pairs"/>, when given, names the only ones to run as "map&lt;queries" file names. Returns why not, or null.
		/// </summary>
		public static string Run(string[] mapPaths, int queriesPerPair, string csvPath, string[] pairs = null, float coverMeters = 0, float coverDegrees = 0, int poolFrames = 0)
		{
			if (!Application.isPlaying) return "not in play mode";
			if (Running) return "already running";
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (localizer == null || provider == null || !localizer.IsAvailable) return "no localizer";

			// the provider's own queries would call the place unfamiliar and clear the map under the replay
			SerializedObject fields = new(provider);
			fields.FindProperty("queriesEnabled").boolValue = false;
			fields.ApplyModifiedPropertiesWithoutUndo();
			Maps.VisualMapPersistence.SkipSaveForTests = true;
			Replay(mapPaths, queriesPerPair, csvPath, pairs != null ? new HashSet<string>(pairs) : null, coverMeters, coverDegrees, poolFrames, localizer, provider);
			return null;
		}

		private static async void Replay(string[] mapPaths, int queriesPerPair, string csvPath, HashSet<string> pairs, float coverMeters, float coverDegrees, int poolFrames,
			VisualLocalizer localizer, VisualColocationConstraintProvider provider)
		{
			Running = true;
			try
			{
				List<VisualMapData> maps = new();
				foreach (string path in mapPaths)
				{
					using FileStream f = new(path, FileMode.Open, FileAccess.Read);
					maps.Add(VisualMapFile.Read(f));
				}

				using StreamWriter csv = new(csvPath, false, Encoding.UTF8);
				csv.WriteLine("map,queries,view,timestampNs,features,cameraX,cameraY,cameraZ,success,failure,keyframesMatched,bestMutual,correspondences,inliers,rangedInliers,rmsPixels,normalizedRms,yawSigmaDegrees,translationSigma,yawDegrees,tx,ty,tz,reach");
				for (int m = 0; m < maps.Count; m++)
				{
					string mapName = Path.GetFileNameWithoutExtension(mapPaths[m]);
					if (maps[m] == null || pairs != null && !AnyPairWith(pairs, mapName)) continue;
					if (!provider.RestoreMap(coverMeters > 0 ? Thinned(maps[m], coverMeters, coverDegrees) : maps[m])) continue;
					for (int q = 0; q < maps.Count; q++)
					{
						if (q == m || maps[q] == null || maps[q].keyframes.Count == 0) continue;
						if (pairs != null && !pairs.Contains($"{mapName}<{Path.GetFileNameWithoutExtension(mapPaths[q])}")) continue;
						Progress = $"map {m + 1} of {maps.Count}, queries {q + 1}";
						List<VisualMapData.Keyframe> views = new(maps[q].keyframes);
						QueryPool pool = poolFrames > 0 ? new QueryPool { maxFrames = poolFrames } : null;
						if (pool != null) views.Sort((a, b) => a.timestampNs.CompareTo(b.timestampNs));
						int count = Mathf.Min(queriesPerPair, views.Count);
						for (int i = 0; i < count; i++)
						{
							int view = pool != null || count == 1 ? i : (int)((long)i * (views.Count - 1) / (count - 1));
							VisualQuery query = QueryOf(views[view]);
							VisualLocalizationResult result = await localizer.LocalizeAsync(query, new LocalizeOptions { urgent = true, maxViews = 5, pool = pool });
							if (!Application.isPlaying || !localizer) return;
							csv.WriteLine(Row(mapName, Path.GetFileNameWithoutExtension(mapPaths[q]), view, query, result));
						}
					}

					csv.Flush();
				}

				Progress = "done";
			}
			catch (Exception e)
			{
				Progress = $"failed: {e.Message}";
				Debug.LogException(e);
			}
			finally
			{
				Running = false;
			}
		}

		private static bool AnyPairWith(HashSet<string> pairs, string map)
		{
			foreach (string pair in pairs) if (pair.StartsWith(map + "<")) return true;
			return false;
		}

		// the first view from each place and heading, in the order they were captured, and the landmarks those sight
		private static VisualMapData Thinned(VisualMapData map, float coverMeters, float coverDegrees)
		{
			VisualMapData thinned = new() { frameId = map.frameId, savedTicks = map.savedTicks, vocabulary = map.vocabulary, owner = map.owner, graph = map.graph };
			List<VisualMapData.Keyframe> ordered = new(map.keyframes);
			ordered.Sort((a, b) => a.timestampNs.CompareTo(b.timestampNs));
			float cosine = Mathf.Cos(Mathf.Deg2Rad * coverDegrees);
			HashSet<(uint owner, int id)> sighted = new();
			foreach (VisualMapData.Keyframe view in ordered)
			{
				bool covered = false;
				foreach (VisualMapData.Keyframe kept in thinned.keyframes)
					covered |= Vector3.Distance(kept.camera.position, view.camera.position) < coverMeters && Vector3.Dot(kept.camera.forward, view.camera.forward) > cosine;
				if (covered) continue;
				thinned.keyframes.Add(view);
				for (int i = 0; i < view.count; i++)
					if (view.landmarks[i] >= 0) sighted.Add((view.owner, view.landmarks[i]));
			}

			foreach (VisualMapData.Landmark landmark in map.landmarks)
				if (sighted.Contains((landmark.owner, landmark.id))) thinned.landmarks.Add(landmark);
			Debug.Log($"[{nameof(DeviceMapReplay)}] thinned to {thinned.keyframes.Count} of {map.keyframes.Count} views, {thinned.landmarks.Count} of {map.landmarks.Count} landmarks");
			return thinned;
		}

		private static VisualQuery QueryOf(VisualMapData.Keyframe view)
		{
			FeatureSet features = new(view.count);
			for (int i = 0; i < view.count; i++)
			{
				int row = view.Row(i);
				features.keypoints[i] = view.keypoints[row];
				features.scores[i] = view.scores[row];
				Array.Copy(view.descriptors, row * FeatureSet.DescriptorSize, features.descriptors, i * FeatureSet.DescriptorSize, FeatureSet.DescriptorSize);
			}

			return new VisualQuery
			{
				features = features,
				intrinsics = view.intrinsics,
				camera = view.camera,
				cameraInTrackingSpace = view.camera,
				trackingSpace = Pose.identity,
				timestampNs = view.timestampNs,
			};
		}

		private static string Row(string map, string queries, int view, VisualQuery query, in VisualLocalizationResult result)
		{
			GravityPnPResult pose = result.pose;
			float reach = MeasurementGate.Reach(pose.cameraCenter, result.inlierPoints);
			string failure = (result.failure ?? pose.failure ?? "").Replace(',', ';');
			Vector3 camera = query.camera.position;
			return string.Join(",", map, queries, view, query.timestampNs, query.features.Count,
				F(camera.x), F(camera.y), F(camera.z), result.Success ? 1 : 0, failure, result.keyframesMatched, result.bestMutual,
				pose.correspondences, pose.inliers, pose.rangedInliers, F(pose.rmsPixels), F(pose.normalizedRms),
				F(Mathf.Rad2Deg * pose.yawSigma), F(pose.translationSigma), F(Mathf.Rad2Deg * pose.yaw),
				F(pose.translation.x), F(pose.translation.y), F(pose.translation.z), F(reach));
		}

		private static string F(float value) => float.IsFinite(value) ? value.ToString("0.####", CultureInfo.InvariantCulture) : "";
	}
}
#endif
