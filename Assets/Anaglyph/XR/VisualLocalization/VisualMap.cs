using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// A mapped view: its features, the camera that saw them relative to the pose-graph
	/// node it was captured under, and the landmark each feature is a sighting of.
	/// </summary>
	public sealed class VisualKeyframe
	{
		public FeatureSet features;
		public PinholeCamera intrinsics;
		public ulong node;
		public Pose cameraInNode;
		/// <summary>The camera in the world, through its node as the graph last had it (<see cref="VisualMap.RefreshPlacement"/>).</summary>
		public Pose camera;
		/// <summary>Per feature, the landmark id it sights, or -1.</summary>
		public int[] landmarks;
		/// <summary>Per feature, the scanned mesh's distance along its ray, or 0 where the ray hit nothing usable.</summary>
		public float[] meshDepth;
		public long timestampNs;
		/// <summary>The <see cref="VisualFrameLog"/> record of the frame it was made from; 0 when not logged.</summary>
		public int frame;
		internal int slot;
	}

	public readonly struct VisualMapQuality
	{
		public VisualMapQuality(int keyframes, int landmarks, int usable, int ranged)
		{
			this.keyframes = keyframes;
			this.landmarks = landmarks;
			this.usable = usable;
			this.ranged = ranged;
		}

		public readonly int keyframes;
		public readonly int landmarks;
		/// <summary>Landmarks a query may match: seen twice, or placed by the mesh.</summary>
		public readonly int usable;
		/// <summary>Usable landmarks whose depth is known to within a quarter of itself: the ones that pin translation.</summary>
		public readonly int ranged;

		public override string ToString() => $"{keyframes} keyframes, {usable} usable landmarks ({ranged} ranged) of {landmarks}";
	}

	/// <summary>
	/// Keyframes and the landmarks tracked across them, anchored to pose-graph nodes, with
	/// a retrieval index: each keyframe's strongest <see cref="RetrievalFeatures"/>
	/// descriptors in a flat native array, so a query is ranked against every keyframe by
	/// mutual nearest neighbors in one Burst job. The map holds a fixed number of slots;
	/// past it, the most redundant keyframe is replaced. Landmarks outlive the keyframes
	/// that sighted them until no keyframe feature points at them any more.
	/// </summary>
	public sealed class VisualMap : IDisposable
	{
		public const int RetrievalFeatures = 256;
		private const int D = FeatureSet.DescriptorSize;

		public readonly int capacity;
		private readonly List<VisualKeyframe> keyframes = new();
		private readonly Dictionary<int, Landmark> landmarks = new();
		private int nextLandmarkId;
		private NativeArray<float> retrieval;
		private NativeArray<int> retrievalCounts;

		public IReadOnlyList<VisualKeyframe> Keyframes => keyframes;
		public IEnumerable<Landmark> Landmarks => landmarks.Values;
		public int LandmarkCount => landmarks.Count;

		public event Action<VisualKeyframe> Evicted = delegate { };
		public event Action Cleared = delegate { };
		public int Count => keyframes.Count;
		public IKeyframeAnchoring Anchoring { get; set; }

		/// <summary>Bumped on every change, so consumers can tell a stale result.</summary>
		public int Revision { get; private set; }

		public VisualMap(int capacity = 192)
		{
			this.capacity = capacity;
			retrieval = new NativeArray<float>(capacity * RetrievalFeatures * D, Allocator.Persistent);
			retrievalCounts = new NativeArray<int>(capacity, Allocator.Persistent);
		}

		public bool TryGetLandmark(int id, out Landmark landmark) => landmarks.TryGetValue(id, out landmark);

		public VisualMapQuality Quality(bool singleViewMesh, float rangedFraction)
		{
			int usable = 0, ranged = 0;
			foreach (Landmark l in landmarks.Values)
			{
				if (!l.IsUsable(singleViewMesh)) continue;
				usable++;
				if (l.inverseDepth > 1e-4f && l.inverseDepthSigma <= rangedFraction * l.inverseDepth) ranged++;
			}

			return new VisualMapQuality(keyframes.Count, landmarks.Count, usable, ranged);
		}

		public void Clear()
		{
			keyframes.Clear();
			landmarks.Clear();
			Revision++;
			Cleared.Invoke();
		}

		/// <summary>Moves every keyframe's world camera to where its node now is.</summary>
		public void RefreshPlacement()
		{
			if (Anchoring == null) return;
			foreach (VisualKeyframe k in keyframes)
				if (Landmark.TryCamera(Anchoring, k.node, k.cameraInNode, out Pose camera))
					k.camera = camera;
		}

		/// <summary>Whether a keyframe's node is within <paramref name="drift"/> of <paramref name="node"/>: consecutive views of one stretch of tracking.</summary>
		public bool IsTrusted(VisualKeyframe k, ulong node, float drift) => Anchoring != null && Anchoring.Drift(node, k.node) <= drift;

		/// <summary>
		/// Whether a camera at <paramref name="camera"/> would see something no trusted
		/// keyframe already covers: farther than <paramref name="minDistance"/> from each,
		/// or turned more than <paramref name="minAngleDegrees"/> from those that are near.
		/// Keyframes beyond the drift window don't count: a revisit maps the place afresh
		/// for its own stretch of tracking, and is tied to the old one by a loop closure.
		/// </summary>
		public bool IsNovel(Pose camera, ulong node, float trustDrift, float minDistance, float minAngleDegrees)
		{
			foreach (VisualKeyframe k in keyframes)
				if (IsTrusted(k, node, trustDrift) &&
				    Vector3.Distance(k.camera.position, camera.position) < minDistance &&
				    Vector3.Angle(k.camera.forward, camera.forward) < minAngleDegrees)
					return false;
			return true;
		}

		public Landmark CreateLandmark(VisualKeyframe keyframe, int feature, float pixelSigma, float meshDepthSigma, float meshDepthFraction)
		{
			Landmark landmark = new()
			{
				id = nextLandmarkId++,
				node = keyframe.node,
				reference = keyframe.cameraInNode,
				bearing = keyframe.intrinsics.Ray(keyframe.features.keypoints[feature]),
			};

			float depth = keyframe.meshDepth != null ? keyframe.meshDepth[feature] : 0;
			if (depth > 0)
			{
				// depth sigma into inverse depth sigma: σρ = σd / d²
				float sigma = meshDepthSigma + meshDepthFraction * depth;
				landmark.meshInverseDepth = 1f / depth;
				landmark.meshInverseDepthSigma = sigma / (depth * depth);
			}

			landmark.inverseDepth = depth > 0 ? 1f / depth : 0;
			landmarks[landmark.id] = landmark;
			Link(keyframe, feature, landmark, pixelSigma);
			return landmark;
		}

		/// <summary>Adds the keyframe feature as a sighting of the landmark. False when the feature already sights another.</summary>
		public bool Link(VisualKeyframe keyframe, int feature, Landmark landmark, float pixelSigma)
		{
			int existing = keyframe.landmarks[feature];
			if (existing == landmark.id) return true;
			if (existing >= 0) return false;
			foreach (Observation o in landmark.observations)
				if (o.keyframe == keyframe) return false;

			keyframe.landmarks[feature] = landmark.id;
			landmark.references++;
			landmark.observations.Add(new Observation
			{
				keyframe = keyframe,
				feature = feature,
				node = keyframe.node,
				cameraInNode = keyframe.cameraInNode,
				ray = keyframe.intrinsics.Ray(keyframe.features.keypoints[feature]),
				angularSigma = pixelSigma / keyframe.intrinsics.fx,
			});
			return true;
		}

		/// <summary>Removes a sighting the landmark's fit rejected; the reference sighting is never one.</summary>
		public void Unlink(Observation observation)
		{
			VisualKeyframe k = observation.keyframe;
			if (k == null || !landmarks.TryGetValue(k.landmarks[observation.feature], out Landmark landmark)) return;
			k.landmarks[observation.feature] = -1;
			Release(landmark);
		}

		private void Release(Landmark landmark)
		{
			if (--landmark.references <= 0) landmarks.Remove(landmark.id);
		}

		public void Add(VisualKeyframe keyframe)
		{
			int slot = keyframes.Count;
			Revision++;
			if (slot >= capacity)
			{
				// replace the keyframe nearest the new one: the most redundant view
				float best = float.MaxValue;
				for (int i = 0; i < keyframes.Count; i++)
				{
					Pose c = keyframes[i].camera;
					float d = Vector3.Distance(c.position, keyframe.camera.position) +
					          Vector3.Angle(c.forward, keyframe.camera.forward) / 45f;
					if (d >= best) continue;
					best = d;
					slot = i;
				}

				Evict(keyframes[slot]);
				keyframes[slot] = keyframe;
			}
			else keyframes.Add(keyframe);

			keyframe.slot = slot;
			int count = math.min(RetrievalFeatures, keyframe.features.Count);
			NativeArray<float>.Copy(keyframe.features.descriptors, 0, retrieval, slot * RetrievalFeatures * D, count * D);
			retrievalCounts[slot] = count;
		}

		// its sightings stay in their landmarks as measurements; the landmarks it alone held go
		private void Evict(VisualKeyframe keyframe)
		{
			for (int f = 0; f < keyframe.landmarks.Length; f++)
			{
				if (!landmarks.TryGetValue(keyframe.landmarks[f], out Landmark landmark)) continue;
				for (int o = 0; o < landmark.observations.Count; o++)
				{
					if (landmark.observations[o].keyframe != keyframe) continue;
					Observation observation = landmark.observations[o];
					observation.keyframe = null;
					landmark.observations[o] = observation;
				}

				Release(landmark);
			}

			Evicted.Invoke(keyframe);
		}

		/// <summary>
		/// The keyframes most likely to show what <paramref name="query"/> shows, best
		/// first: ranked by mutual nearest neighbors among the strongest features, at
		/// least <paramref name="minMutual"/> of them, among those <paramref name="filter"/>
		/// lets through and, with a <paramref name="near"/> pose, within
		/// <paramref name="nearDistance"/> of it.
		/// </summary>
		public async Awaitable<List<(VisualKeyframe keyframe, int mutual)>> RetrieveAsync(FeatureSet query, int count, int minMutual,
			Func<VisualKeyframe, bool> filter = null, Pose? near = null, float nearDistance = 4f)
		{
			List<(VisualKeyframe, int)> result = new();
			if (keyframes.Count == 0 || query == null || query.Count == 0) return result;

			// the cheap tests first: only keyframes that could qualify are scored
			List<int> candidates = new(keyframes.Count);
			for (int i = 0; i < keyframes.Count; i++)
			{
				if (filter != null && !filter(keyframes[i])) continue;
				if (near.HasValue && Vector3.Distance(near.Value.position, keyframes[i].camera.position) > nearDistance) continue;
				candidates.Add(i);
			}

			if (candidates.Count == 0) return result;

			int queryCount = math.min(RetrievalFeatures, query.Count);
			// persistent: the job may outlive the four frames a TempJob allocation is allowed
			NativeArray<float> queryDesc = new(queryCount * D, Allocator.Persistent);
			NativeArray<int> slots = new(candidates.ToArray(), Allocator.Persistent);
			NativeArray<int> scores = new(candidates.Count, Allocator.Persistent);
			try
			{
				NativeArray<float>.Copy(query.descriptors, queryDesc, queryCount * D);
				JobHandle handle;
				using (ScheduleMarker.Auto())
					handle = new MutualNearestJob
					{
						query = queryDesc,
						queryCount = queryCount,
						map = retrieval,
						mapCounts = retrievalCounts,
						slots = slots,
						scores = scores,
					}.Schedule(candidates.Count, 1);

				// a few hundred million multiply-adds: worker threads, while the frame goes on
				JobHandle.ScheduleBatchedJobs();
				while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
				handle.Complete();

				List<int> order = new(candidates.Count);
				for (int c = 0; c < candidates.Count; c++)
					if (scores[c] >= minMutual)
						order.Add(c);
				order.Sort((a, b) => scores[b].CompareTo(scores[a]));
				for (int i = 0; i < order.Count && i < count; i++)
				{
					// a keyframe evicted meanwhile is no longer what was scored
					int slot = candidates[order[i]];
					if (slot < keyframes.Count) result.Add((keyframes[slot], scores[order[i]]));
				}

				return result;
			}
			finally
			{
				queryDesc.Dispose();
				slots.Dispose();
				scores.Dispose();
			}
		}

		private static readonly ProfilerMarker ScheduleMarker = new("VisualMap.Retrieve schedule");

		public void Dispose()
		{
			if (retrieval.IsCreated) retrieval.Dispose();
			if (retrievalCounts.IsCreated) retrievalCounts.Dispose();
		}

		// one keyframe per index: query × keyframe similarities, then mutual best pairs
		// whose cosine clears a floor
		[BurstCompile(FloatMode = FloatMode.Fast)]
		private struct MutualNearestJob : IJobParallelFor
		{
			private const float MinCosine = 0.8f;

			[ReadOnly] public NativeArray<float> query;
			public int queryCount;
			[ReadOnly] public NativeArray<float> map;
			[ReadOnly] public NativeArray<int> mapCounts;
			[ReadOnly] public NativeArray<int> slots;
			[WriteOnly] public NativeArray<int> scores;

			public void Execute(int index)
			{
				int k = slots[index];
				int m = mapCounts[k];
				int baseIndex = k * RetrievalFeatures * D;
				NativeArray<int> bestForMap = new(m, Allocator.Temp);
				NativeArray<float> bestForMapSim = new(m, Allocator.Temp);
				NativeArray<int> bestForQuery = new(queryCount, Allocator.Temp);
				for (int j = 0; j < m; j++) bestForMapSim[j] = -2;

				for (int i = 0; i < queryCount; i++)
				{
					float best = -2;
					int bestJ = -1;
					for (int j = 0; j < m; j++)
					{
						float dot = 0;
						int qi = i * D, mj = baseIndex + j * D;
						for (int c = 0; c < D; c++) dot += query[qi + c] * map[mj + c];
						if (dot > best)
						{
							best = dot;
							bestJ = j;
						}

						if (dot > bestForMapSim[j])
						{
							bestForMapSim[j] = dot;
							bestForMap[j] = i;
						}
					}

					bestForQuery[i] = best >= MinCosine ? bestJ : -1;
				}

				int mutual = 0;
				for (int i = 0; i < queryCount; i++)
					if (bestForQuery[i] >= 0 && bestForMap[bestForQuery[i]] == i) mutual++;
				scores[index] = mutual;
			}
		}
	}
}
