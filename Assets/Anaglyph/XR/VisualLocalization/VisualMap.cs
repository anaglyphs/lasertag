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
		public long timestampNs;
		/// <summary>The <see cref="VisualFrameLog"/> record of the frame it was made from; 0 when not logged.</summary>
		public int frame;
		/// <summary>
		/// The first view of its place and heading, as opposed to a revisit's. Never given up
		/// while any revisit view is left to give up instead: later views add to the map, they
		/// don't replace what it first saw.
		/// </summary>
		public bool original;
		/// <summary>
		/// The first view of its place and heading, at the coarser scale a revisit is measured
		/// at: the fixed reference revisits localize against. A saved or received map's are all so.
		/// </summary>
		public bool locked;
		/// <summary>The headset whose tracking made it, for telling views apart across headsets; 0 for a saved map's.</summary>
		public uint owner;
		/// <summary>Sent to the other headsets, or come from one: they hold it too.</summary>
		public bool shared;
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
		/// <summary>Keyframes scored per retrieval when a camera narrows the search: ~4M multiply-adds each.</summary>
		public const int MaxRetrievalCandidates = 24;
		/// <summary>Keyframes scored per frame unless the caller says otherwise: each is a few million multiply-adds on a worker thread.</summary>
		public const int DefaultCandidatesPerFrame = 2;
		// the retrieval slices reading the descriptor table now, which writes and disposal wait on
		private JobHandle retrievalInFlight;
		// keyframes added while a slice was reading the table: written once it is done, so
		// adding one never waits on a slice from this thread
		private readonly List<(int slot, half[] descriptors, int count)> pendingWrites = new();
		private const int D = FeatureSet.DescriptorSize;

		public readonly int capacity;
		private readonly List<VisualKeyframe> keyframes = new();
		private readonly Dictionary<int, Landmark> landmarks = new();
		private int nextLandmarkId;
		private NativeArray<half> retrieval;
		private NativeArray<int> retrievalCounts;

		public IReadOnlyList<VisualKeyframe> Keyframes => keyframes;
		public IEnumerable<Landmark> Landmarks => landmarks.Values;
		public int LandmarkCount => landmarks.Count;
		/// <summary>Every landmark id so far is below this.</summary>
		public int LandmarkIdLimit => nextLandmarkId;

		public event Action<VisualKeyframe> Evicted = delegate { };
		public event Action Cleared = delegate { };
		public int Count => keyframes.Count;
		public IKeyframeAnchoring Anchoring { get; set; }
		/// <summary>This headset's name for what it maps, on every landmark and keyframe it makes.</summary>
		public uint LocalOwner { get; set; }

		/// <summary>Bumped on every change, so consumers can tell a stale result.</summary>
		public int Revision { get; private set; }

		public VisualMap(int capacity = 192)
		{
			this.capacity = capacity;
			retrieval = new NativeArray<half>(capacity * RetrievalFeatures * D, Allocator.Persistent);
			retrievalCounts = new NativeArray<int>(capacity, Allocator.Persistent);
		}

		public bool TryGetLandmark(int id, out Landmark landmark) => landmarks.TryGetValue(id, out landmark);

		public VisualMapQuality Quality(float rangedFraction)
		{
			int usable = 0, ranged = 0;
			foreach (Landmark l in landmarks.Values)
			{
				if (!l.IsUsable) continue;
				usable++;
				if (l.inverseDepth > 1e-4f && l.inverseDepthSigma <= rangedFraction * l.inverseDepth) ranged++;
			}

			return new VisualMapQuality(keyframes.Count, landmarks.Count, usable, ranged);
		}

		public void Clear()
		{
			keyframes.Clear();
			landmarks.Clear();
			// their slots are about to be handed out afresh
			pendingWrites.Clear();
			CoverageLevel = 0;
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

		/// <summary>
		/// Whether no view walked away from has seen this place and heading: within
		/// <paramref name="radius"/> and turned less than <paramref name="angleDegrees"/>.
		/// </summary>
		public bool IsFirstView(Pose camera, ulong node, float minWalkDrift, float radius, float angleDegrees)
		{
			if (Anchoring == null) return true;
			foreach (VisualKeyframe k in keyframes)
				if (Vector3.Distance(k.camera.position, camera.position) < radius &&
				    Vector3.Angle(k.camera.forward, camera.forward) < angleDegrees &&
				    Anchoring.WalkDrift(node, k.node) >= minWalkDrift)
					return false;
			return true;
		}

		public Landmark CreateLandmark(VisualKeyframe keyframe, int feature, float pixelSigma)
		{
			Landmark landmark = new()
			{
				id = nextLandmarkId,
				owner = LocalOwner,
				ownerId = nextLandmarkId++,
				node = keyframe.node,
				reference = keyframe.cameraInNode,
				bearing = keyframe.intrinsics.Ray(keyframe.features.keypoints[feature]),
			};

			landmarks[landmark.id] = landmark;
			Link(keyframe, feature, landmark, pixelSigma);
			return landmark;
		}

		/// <summary>Adds the keyframe feature as a sighting of the landmark. False when the feature already sights another.</summary>
		public bool Link(VisualKeyframe keyframe, int feature, Landmark landmark, float pixelSigma)
		{
			int existing = keyframe.landmarks[feature];
			if (existing == landmark.id) return !(landmark.established && freezeEstablished);
			if (existing >= 0) return false;

			// an established landmark keeps the observations it was founded on: the keyframe
			// sights it, which lets localization use it from there, but adds nothing to its fit
			if (landmark.established && freezeEstablished)
			{
				keyframe.landmarks[feature] = landmark.id;
				landmark.references++;
				return false;
			}

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

		/// <param name="trustDrift">The drift window keyframes count as one stretch of tracking within.</param>
		public void Add(VisualKeyframe keyframe)
		{
			int slot = keyframes.Count;
			Revision++;
			if (slot >= capacity)
			{
				slot = CoverageVictim();
				Evict(keyframes[slot]);
				keyframes[slot] = keyframe;
			}
			else keyframes.Add(keyframe);

			keyframe.slot = slot;
			int count = math.min(RetrievalFeatures, keyframe.features.Count);
			pendingWrites.Add((slot, keyframe.features.descriptors, count));
			FlushWrites();
		}

		// the keyframes added meanwhile into the table, once no slice reads it: an urgent
		// retrieval's slice holds every worker for milliseconds, which this thread mustn't wait out
		private void FlushWrites()
		{
			if (pendingWrites.Count == 0 || !retrievalInFlight.IsCompleted) return;
			retrievalInFlight.Complete();
			foreach ((int slot, half[] descriptors, int count) in pendingWrites)
			{
				NativeArray<half>.Copy(descriptors, 0, retrieval, slot * RetrievalFeatures * D, count * D);
				retrievalCounts[slot] = count;
			}

			pendingWrites.Clear();
		}

		/// <summary>
		/// Adds views walked elsewhere or earlier to the map as they are, another headset's or a
		/// saved map's, under the knots they name: frames of their own, which this headset's
		/// tracking never triangulates with, only localizes against. Their landmark ids are the sender's,
		/// mapped through <paramref name="ids"/> (the sender's → this map's) and allocated here
		/// the first time; a landmark held already takes the placement sent, and the new views'
		/// sightings join it.
		/// </summary>
		/// <returns>The views added.</returns>
		/// <param name="shared">Whether every other headset holds these views too.</param>
		public List<VisualKeyframe> Merge(VisualMapData data, Func<Pose, Pose> toWorld, Quaternion toWorldRotation, float pixelSigma, Dictionary<int, int> ids, bool shared)
		{
			// the sender's landmark ids as this map's, allocated the first time one is seen
			HashSet<int> arrived = new();
			foreach (VisualMapData.Landmark l in data.landmarks)
			{
				if (!ids.TryGetValue(l.id, out int id))
				{
					id = nextLandmarkId++;
					ids[l.id] = id;
				}

				Vector3 origin = toWorld(new Pose(l.origin, Quaternion.identity)).position;
				if (!landmarks.TryGetValue(id, out Landmark landmark))
				{
					landmark = new Landmark { id = id, owner = data.owner, ownerId = l.id, node = l.node, established = true };
					landmarks[id] = landmark;
				}

				landmark.hasWire = false;
				landmark.reference = new Pose(origin, Quaternion.identity);
				landmark.bearing = toWorldRotation * (Vector3)l.direction;
				landmark.inverseDepth = l.inverseDepth;
				landmark.inverseDepthSigma = l.inverseDepthSigma;
				arrived.Add(id);
			}

			List<VisualKeyframe> restored = new();
			foreach (VisualMapData.Keyframe k in data.keyframes)
			{
				if (k.node == 0 || k.count < LighterGlueMatcher.MinKeypoints) continue;
				int[] sighted = new int[k.count];
				for (int i = 0; i < k.count; i++)
					sighted[i] = k.landmarks[i] >= 0 && ids.TryGetValue(k.landmarks[i], out int id) ? id : -1;
				FeatureSet features = new(k.count);
				Array.Copy(k.keypoints, features.keypoints, k.count);
				Array.Copy(k.scores, features.scores, k.count);
				Array.Copy(k.descriptors, features.descriptors, k.count * D);
				Pose camera = toWorld(k.camera);
				VisualKeyframe keyframe = new()
				{
					features = features,
					intrinsics = k.intrinsics,
					node = k.node,
					cameraInNode = camera,
					camera = camera,
					landmarks = sighted,
					timestampNs = k.timestampNs,
					original = k.original,
					locked = k.locked,
					owner = data.owner,
					shared = shared,
				};
				Add(keyframe);
				restored.Add(keyframe);
			}

			// the sightings, from the keyframes' side
			foreach (VisualKeyframe k in restored)
				for (int i = 0; i < k.features.Count; i++)
				{
					int id = k.landmarks[i];
					if (id < 0) continue;
					if (!landmarks.TryGetValue(id, out Landmark landmark))
					{
						k.landmarks[i] = -1;
						continue;
					}

					landmark.references++;
					if (DropsEstablishedSightings) continue;
					landmark.observations.Add(new Observation
					{
						keyframe = k,
						feature = i,
						node = k.node,
						cameraInNode = k.cameraInNode,
						ray = k.intrinsics.Ray(k.features.keypoints[i]),
						angularSigma = pixelSigma / k.intrinsics.fx,
					});
				}

			// a landmark no saved view sights is nothing
			List<int> orphans = new();
			foreach (Landmark l in landmarks.Values)
				if (l.references == 0 && arrived.Contains(l.id)) orphans.Add(l.id);
			foreach (int id in orphans) landmarks.Remove(id);
			Revision++;
			return restored;
		}

		// ---- coverage --------------------------------------------------------

		/// <summary>Established landmarks keep the observations they were founded on (see <see cref="Landmark.established"/>).</summary>
		public bool freezeEstablished = true;

		/// <summary>Whether established landmarks give up their sightings, which only diagnostics still read: with frozen landmarks, nothing refits them.</summary>
		public bool dropEstablishedSightings;

		private bool DropsEstablishedSightings => freezeEstablished && dropEstablishedSightings;

		/// <summary>Fixes a landmark's fit from now on.</summary>
		public void Establish(Landmark landmark)
		{
			landmark.established = true;
			if (!DropsEstablishedSightings) return;
			landmark.observations.Clear();
			landmark.observations.TrimExcess();
		}

		/// <summary>Side of a coverage cell at the finest level, meters.</summary>
		public float coverageCell = 1f;
		/// <summary>Heading bins at the finest level.</summary>
		public int coverageHeadings = 8;

		/// <summary>
		/// How many times coverage has been coarsened: each level doubles the cell side and
		/// halves the heading bins. It only grows, so a map that outgrew its capacity once
		/// doesn't refine again and churn.
		/// </summary>
		public int CoverageLevel { get; private set; }

		public float CoverageCellSize => coverageCell * (1 << CoverageLevel);
		public int CoverageHeadingBins => math.max(1, coverageHeadings >> CoverageLevel);

		/// <summary>
		/// The keyframe to give up when the map is full. Revisit views go first, from the most
		/// crowded bucket of place (horizontal cell) and heading that has one: a place's first
		/// views are never replaced by later ones. Only once no revisit view is left does a
		/// first view go, again from the most crowded bucket, so every place and direction ever
		/// seen keeps a view for as long as the capacity allows. Within a bucket, the keyframe
		/// that sights the fewest usable landmarks goes, the newer on a tie. When every bucket is
		/// down to a single keyframe the explored area has outgrown the capacity, and the
		/// buckets coarsen rather than whole places losing their last view.
		/// </summary>
		private int CoverageVictim()
		{
			// a revisit view from the most crowded bucket that has one, while any is left
			RefillBuckets();
			List<int> crowded = null;
			foreach (List<int> bucket in buckets.Values)
				if ((crowded == null || bucket.Count > crowded.Count) && HasRevisit(bucket))
					crowded = bucket;

			if (crowded != null)
			{
				for (int i = crowded.Count - 1; i >= 0; i--)
					if (keyframes[crowded[i]].original) crowded.RemoveAt(i);
				return LeastUseful(crowded);
			}

			// only first views left: the explored area has outgrown the capacity
			while (true)
			{
				foreach (List<int> bucket in buckets.Values)
					if (bucket.Count > 1 && (crowded == null || bucket.Count > crowded.Count))
						crowded = bucket;

				if (crowded != null) return LeastUseful(crowded);
				if (CoverageCellSize >= 64f && CoverageHeadingBins == 1) return LeastUseful(new List<int>(System.Linq.Enumerable.Range(0, keyframes.Count)));

				CoverageLevel++;
				Debug.Log($"[{nameof(VisualMap)}] every place and heading is down to one keyframe: coverage coarsened to {CoverageCellSize:F0} m cells, {CoverageHeadingBins} headings");
				RefillBuckets();
			}
		}

		private bool HasRevisit(List<int> bucket)
		{
			foreach (int i in bucket)
				if (!keyframes[i].original) return true;
			return false;
		}

		// the buckets of the last eviction, their lists reused: one comes every keyframe once the map is full
		private readonly Dictionary<int3, List<int>> buckets = new();
		private readonly Stack<List<int>> spareBuckets = new();

		private void RefillBuckets()
		{
			foreach (List<int> list in buckets.Values)
			{
				list.Clear();
				spareBuckets.Push(list);
			}

			buckets.Clear();
			float cell = CoverageCellSize;
			int headings = CoverageHeadingBins;
			for (int i = 0; i < keyframes.Count; i++)
			{
				Pose camera = keyframes[i].camera;
				Vector3 forward = camera.forward;
				float heading = math.atan2(forward.x, forward.z);
				int bin = (int)math.floor((heading + math.PI) / (2 * math.PI) * headings) % headings;
				int3 key = new((int)math.floor(camera.position.x / cell), (int)math.floor(camera.position.z / cell), bin);
				if (!buckets.TryGetValue(key, out List<int> list))
					buckets[key] = list = spareBuckets.Count > 0 ? spareBuckets.Pop() : new List<int>();
				list.Add(i);
			}
		}

		private int LeastUseful(List<int> candidates)
		{
			int victim = candidates[0], fewest = int.MaxValue;
			foreach (int i in candidates)
			{
				VisualKeyframe k = keyframes[i];
				int usable = 0;
				foreach (int id in k.landmarks)
					if (landmarks.TryGetValue(id, out Landmark l) && l.IsUsable)
						usable++;

				if (usable > fewest || (usable == fewest && k.timestampNs <= keyframes[victim].timestampNs)) continue;
				fewest = usable;
				victim = i;
			}

			return victim;
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
		/// <paramref name="nearDistance"/> of it. <paramref name="candidatesPerFrame"/> keyframes
		/// are scored a frame: more when something waits on the answer. With
		/// <paramref name="scoreOnlyToChoose"/>, a caller that measures what it gets by itself
		/// has no scoring done when there is nothing to choose between: all come back at 0.
		/// </summary>
		public async Awaitable<List<(VisualKeyframe keyframe, int mutual)>> RetrieveAsync(FeatureSet query, int count, int minMutual,
			Func<VisualKeyframe, bool> filter = null, Pose? near = null, float nearDistance = 4f, int maxCandidates = MaxRetrievalCandidates,
			int candidatesPerFrame = DefaultCandidatesPerFrame, bool scoreOnlyToChoose = false)
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
			if (scoreOnlyToChoose && candidates.Count <= count)
			{
				foreach (int i in candidates) result.Add((keyframes[i], 0));
				return result;
			}

			// every candidate costs a brute-force match of a few hundred descriptors against as
			// many: with a camera to go by, only the views most likely to share its features are
			// scored, nearest and most alike in heading first
			if (near.HasValue && maxCandidates > 0 && candidates.Count > maxCandidates)
			{
				Pose at = near.Value;
				float Likeness(int i) => Vector3.Distance(at.position, keyframes[i].camera.position) +
				                        Vector3.Angle(at.forward, keyframes[i].camera.forward) / 30f;
				candidates.Sort((a, b) => Likeness(a).CompareTo(Likeness(b)));
				candidates.RemoveRange(maxCandidates, candidates.Count - maxCandidates);
			}

			int queryCount = math.min(RetrievalFeatures, query.Count);
			// persistent: the job may outlive the four frames a TempJob allocation is allowed
			NativeArray<half> queryDesc = new(queryCount * D, Allocator.Persistent);
			NativeArray<int> slots = new(candidates.ToArray(), Allocator.Persistent);
			NativeArray<int> scores = new(candidates.Count, Allocator.Persistent);
			try
			{
				NativeArray<half>.Copy(query.descriptors, queryDesc, queryCount * D);
				// a few keyframes a frame, each a few million multiply-adds: one job holding a
				// worker for tens of milliseconds holds up the engine's own jobs, and the frame
				int perFrame = math.max(1, candidatesPerFrame);
				for (int first = 0; first < candidates.Count; first += perFrame)
				{
					if (!retrieval.IsCreated) return result; // the map was disposed between slices
					FlushWrites();
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
							offset = first,
						}.Schedule(math.min(perFrame, candidates.Count - first), 1);

					// a mapping retrieval and a localizing one may overlap: writes wait for both
					retrievalInFlight = JobHandle.CombineDependencies(retrievalInFlight, handle);
					JobHandle.ScheduleBatchedJobs();
					while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
					handle.Complete();
					FlushWrites();
				}

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
			retrievalInFlight.Complete();
			if (retrieval.IsCreated) retrieval.Dispose();
			if (retrievalCounts.IsCreated) retrievalCounts.Dispose();
		}

		// one keyframe per index: query × keyframe similarities, then mutual best pairs
		// whose cosine clears a floor
		[BurstCompile(FloatMode = FloatMode.Fast)]
		private struct MutualNearestJob : IJobParallelFor
		{
			private const float MinCosine = 0.8f;

			[ReadOnly] public NativeArray<half> query;
			public int queryCount;
			[ReadOnly] public NativeArray<half> map;
			[ReadOnly] public NativeArray<int> mapCounts;
			[ReadOnly] public NativeArray<int> slots;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> scores;
			public int offset;

			public void Execute(int job)
			{
				int index = job + offset;
				int k = slots[index];
				int m = mapCounts[k];
				int baseIndex = k * RetrievalFeatures * D;
				NativeArray<int> bestForMap = new(m, Allocator.Temp);
				NativeArray<float> bestForMapSim = new(m, Allocator.Temp);
				NativeArray<int> bestForQuery = new(queryCount, Allocator.Temp);
				for (int j = 0; j < m; j++) bestForMapSim[j] = -2;
				NativeArray<float4> row = new(D / 4, Allocator.Temp);

				// the keyframe's descriptors widened once, not once per query row: a conversion
				// per multiply-add before
				const int W = D / 4;
				NativeArray<float4> rows = new(m * W, Allocator.Temp);
				for (int j = 0; j < m; j++)
				{
					int mj = baseIndex + j * D;
					for (int c = 0; c < D; c += 4) rows[j * W + c / 4] = new float4(map[mj + c], map[mj + c + 1], map[mj + c + 2], map[mj + c + 3]);
				}

				for (int i = 0; i < queryCount; i++)
				{
					int qi = i * D;
					for (int c = 0; c < D; c += 4) row[c / 4] = new float4(query[qi + c], query[qi + c + 1], query[qi + c + 2], query[qi + c + 3]);

					float best = -2;
					int bestJ = -1;
					for (int j = 0; j < m; j++)
					{
						int rj = j * W;
						float4 acc = 0;
						for (int c = 0; c < W; c++) acc += row[c] * rows[rj + c];
						float dot = math.csum(acc);
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
