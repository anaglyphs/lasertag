using System.Collections.Generic;
using System.IO;
using Anaglyph.XR.VisualLocalization;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class VisualMapSnapshotTests
	{
		private const int D = FeatureSet.DescriptorSize;
		private const int FeatureCount = VisualMap.RetrievalFeatures + 40;
		// past the features retrieval ranks by, so only its sighting keeps it in a snapshot
		private const int SightedWeakFeature = VisualMap.RetrievalFeatures + 3;
		private const int UnsightedWeakFeature = VisualMap.RetrievalFeatures + 4;

		private sealed class AnchoredAtOrigin : IKeyframeAnchoring
		{
			public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
			{
				node = 1;
				cameraInNode = cameraInTrackingSpace;
				return true;
			}

			public bool TryGetNodeWorld(ulong node, out Pose world)
			{
				world = Pose.identity;
				return true;
			}

			public float Drift(ulong a, ulong b) => 0;
		}

		private static FeatureSet Features(int seed)
		{
			Unity.Mathematics.Random random = new((uint)seed + 1);
			FeatureSet features = new(FeatureCount);
			for (int i = 0; i < FeatureCount; i++)
			{
				features.keypoints[i] = new float2(random.NextInt(0, 500), random.NextInt(0, 500));
				features.scores[i] = (float)(FeatureCount - i) / FeatureCount;
				float3 direction = random.NextFloat3Direction();
				for (int c = 0; c < D; c++)
					features.descriptors[i * D + c] = (half)(direction[c % 3] * (c % 2 == 0 ? 0.25f : -0.25f));
			}

			return features;
		}

		private static VisualKeyframe Keyframe(int seed, VisualMap map)
		{
			FeatureSet features = Features(seed);
			VisualKeyframe keyframe = new()
			{
				features = features,
				intrinsics = new PinholeCamera(300, 300, 272, 272),
				node = 1,
				cameraInNode = new Pose(new Vector3(seed, 0, 0), Quaternion.identity),
				camera = new Pose(new Vector3(seed, 0, 0), Quaternion.identity),
				landmarks = new int[features.Count],
				timestampNs = seed,
				owner = 7,
				original = true,
			};
			System.Array.Fill(keyframe.landmarks, -1);
			map.Add(keyframe);
			return keyframe;
		}

		private static VisualMap MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe b, out Landmark shared)
		{
			VisualMap map = new(8) { Anchoring = new AnchoredAtOrigin(), LocalOwner = 7 };
			a = Keyframe(1, map);
			b = Keyframe(2, map);
			shared = map.CreateLandmark(a, SightedWeakFeature, 1.5f);
			map.Link(b, 5, shared, 1.5f);
			shared.inverseDepth = 0.25f;
			shared.inverseDepthSigma = 0.01f;
			return map;
		}

		private static Pose Raw(ulong node, Pose inNode) => inNode;
		private static ulong Wire(ulong node) => node + 100;

		[Test]
		public void Snapshot_KeepsSightedFeaturesAndStrongest_SharingTheViewsArrays()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe _, out Landmark shared);
			try
			{
				VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);

				VisualMapData.Keyframe first = data.keyframes[0];
				Assert.AreEqual(VisualMap.RetrievalFeatures + 1, first.count);
				Assert.AreSame(a.features.descriptors, first.descriptors);
				Assert.AreEqual(SightedWeakFeature, first.Row(VisualMap.RetrievalFeatures));
				Assert.AreEqual(shared.ownerId, first.landmarks[VisualMap.RetrievalFeatures]);
				Assert.AreEqual(-1, first.landmarks[0]);
				Assert.AreEqual(1, data.landmarks.Count);
				Assert.AreEqual(101ul, data.landmarks[0].node);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Snapshot_LeavesOutALandmarkNotYetUsable()
		{
			VisualMap map = new(8) { Anchoring = new AnchoredAtOrigin(), LocalOwner = 7 };
			try
			{
				VisualKeyframe a = Keyframe(1, map);
				map.CreateLandmark(a, UnsightedWeakFeature, 1.5f);

				VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);

				Assert.AreEqual(VisualMap.RetrievalFeatures, data.keyframes[0].count);
				Assert.AreEqual(0, data.landmarks.Count);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Snapshot_ReusesAnEstablishedLandmark_ButNotOneStillBeingFitted()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe _, out VisualKeyframe _, out Landmark shared);
			try
			{
				VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);
				shared.inverseDepth = 0.5f;
				Assert.AreEqual(0.5f, VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire).landmarks[0].inverseDepth);

				map.Establish(shared);
				VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);
				shared.inverseDepth = 0.75f;
				Assert.AreEqual(0.5f, VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire).landmarks[0].inverseDepth);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void File_RoundTripsASnapshotsChosenFeatures()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe _, out Landmark shared);
			try
			{
				VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);
				data.frameId = "frame";

				using MemoryStream stream = new();
				VisualMapFile.Write(data, stream);
				stream.Position = 0;
				VisualMapData read = VisualMapFile.Read(stream);

				Assert.IsNotNull(read);
				VisualMapData.Keyframe first = read.keyframes[0];
				Assert.AreEqual(VisualMap.RetrievalFeatures + 1, first.count);
				Assert.AreEqual(shared.ownerId, first.landmarks[VisualMap.RetrievalFeatures]);
				float2 expected = a.features.keypoints[SightedWeakFeature];
				Assert.AreEqual(expected.x, first.keypoints[VisualMap.RetrievalFeatures].x, 0.02f);
				Assert.AreEqual(expected.y, first.keypoints[VisualMap.RetrievalFeatures].y, 0.02f);

				// int8 and renormalized in the file: pointing the same way, signs included
				float dot = 0;
				float lengthSquared = 0;
				for (int c = 0; c < D; c++)
				{
					float original = a.features.descriptors[SightedWeakFeature * D + c];
					dot += original * first.descriptors[VisualMap.RetrievalFeatures * D + c];
					lengthSquared += original * original;
				}

				Assert.AreEqual(1f, dot / math.sqrt(lengthSquared), 0.01f);
				Assert.AreEqual(0.25f, read.landmarks[0].inverseDepth);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Codec_RoundTripsASnapshotsChosenFeatures()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe _, out Landmark shared);
			try
			{
				VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, new[] { a }, Raw, Wire);

				VisualMapData read = VisualMapCodec.Decode(VisualMapCodec.Encode(data));

				Assert.IsNotNull(read);
				VisualMapData.Keyframe first = read.keyframes[0];
				Assert.AreEqual(VisualMap.RetrievalFeatures + 1, first.count);
				Assert.AreEqual(shared.ownerId, first.landmarks[VisualMap.RetrievalFeatures]);
				float2 expected = a.features.keypoints[SightedWeakFeature];
				Assert.AreEqual(expected.x, first.keypoints[VisualMap.RetrievalFeatures].x, 0.02f);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Establish_DropsSightingsOnlyWhenTheMapDoes()
		{
			VisualMap keeping = MapWithSharedLandmark(out VisualKeyframe _, out VisualKeyframe _, out Landmark kept);
			VisualMap dropping = MapWithSharedLandmark(out VisualKeyframe _, out VisualKeyframe _, out Landmark dropped);
			VisualMap refitting = MapWithSharedLandmark(out VisualKeyframe _, out VisualKeyframe _, out Landmark refit);
			try
			{
				dropping.dropEstablishedSightings = true;
				refitting.dropEstablishedSightings = true;
				refitting.freezeEstablished = false;

				keeping.Establish(kept);
				dropping.Establish(dropped);
				refitting.Establish(refit);

				Assert.AreEqual(2, kept.observations.Count);
				Assert.AreEqual(0, dropped.observations.Count);
				Assert.IsTrue(dropped.IsUsable);
				Assert.AreEqual(2, refit.observations.Count);
			}
			finally
			{
				keeping.Dispose();
				dropping.Dispose();
				refitting.Dispose();
			}
		}

		[Test]
		public void Merge_BuildsNoSightingsWhenTheMapDrops_ButCountsReferences()
		{
			VisualMap source = MapWithSharedLandmark(out VisualKeyframe _, out VisualKeyframe _, out Landmark _);
			VisualMap keeping = new(8) { Anchoring = new AnchoredAtOrigin() };
			VisualMap dropping = new(8) { Anchoring = new AnchoredAtOrigin(), dropEstablishedSightings = true };
			try
			{
				VisualMapData snapshot = VisualMapFile.Snapshot(source, pose => pose, Quaternion.identity, null, Raw, Wire);
				using MemoryStream stream = new();
				VisualMapFile.Write(snapshot, stream);
				stream.Position = 0;
				VisualMapData read = VisualMapFile.Read(stream);

				keeping.Merge(read, pose => pose, Quaternion.identity, 1.5f, new Dictionary<int, int>(), false);
				dropping.Merge(read, pose => pose, Quaternion.identity, 1.5f, new Dictionary<int, int>(), false);

				Assert.AreEqual(2, First(keeping).observations.Count);
				Assert.AreEqual(0, First(dropping).observations.Count);
				Assert.AreEqual(2, First(dropping).references);
				Assert.IsTrue(First(dropping).IsUsable);
			}
			finally
			{
				source.Dispose();
				keeping.Dispose();
				dropping.Dispose();
			}
		}

		private static Landmark First(VisualMap map)
		{
			foreach (Landmark landmark in map.Landmarks) return landmark;
			return null;
		}

		[Test]
		public void RemoveOwner_TakesItsViewsLandmarksAndSightings_KeepingEveryoneElses()
		{
			VisualMap map = new(8) { Anchoring = new AnchoredAtOrigin(), LocalOwner = 7 };
			try
			{
				VisualKeyframe kept = Keyframe(1, map);
				VisualKeyframe wrong = Keyframe(2, map);
				wrong.owner = 9;
				VisualKeyframe keptLast = Keyframe(3, map);
				Landmark keptLandmark = map.CreateLandmark(kept, SightedWeakFeature, 1.5f);
				map.Link(wrong, 5, keptLandmark, 1.5f);
				map.LocalOwner = 9;
				Landmark wrongLandmark = map.CreateLandmark(wrong, UnsightedWeakFeature, 1.5f);
				map.Link(kept, 6, wrongLandmark, 1.5f);

				Assert.That(map.RemoveOwner(9), Is.EqualTo(1));

				Assert.That(map.Keyframes, Is.EquivalentTo(new[] { kept, keptLast }));
				Assert.That(map.TryGetLandmark(wrongLandmark.id, out _), Is.False);
				Assert.That(kept.landmarks[6], Is.EqualTo(-1));
				Assert.That(map.TryGetLandmark(keptLandmark.id, out _), Is.True);
				Assert.That(keptLandmark.observations.Exists(o => o.keyframe == wrong), Is.False);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Eviction_GivesUpARevisitFromTheMostCrowdedBucket_AcrossRepeatedAdds()
		{
			VisualMap map = new(4) { Anchoring = new AnchoredAtOrigin(), LocalOwner = 7 };
			try
			{
				VisualKeyframe lonely = Keyframe(50, map);
				VisualKeyframe firstView = Keyframe(0, map);
				firstView.camera = new Pose(Vector3.zero, Quaternion.identity);
				VisualKeyframe revisit = Keyframe(0, map);
				revisit.camera = firstView.camera;
				revisit.original = false;
				VisualKeyframe other = Keyframe(0, map);
				other.camera = firstView.camera;

				VisualKeyframe next = new()
				{
					features = Features(9), node = 1, landmarks = new int[FeatureCount], camera = firstView.camera, original = false,
					intrinsics = new PinholeCamera(300, 300, 272, 272),
				};
				System.Array.Fill(next.landmarks, -1);
				map.Add(next);

				CollectionAssert.DoesNotContain(map.Keyframes, revisit);
				CollectionAssert.Contains(map.Keyframes, firstView);
				CollectionAssert.Contains(map.Keyframes, lonely);

				// the buckets reused: the view just added is the only revisit left to give up
				VisualKeyframe after = new()
				{
					features = Features(10), node = 1, landmarks = new int[FeatureCount], camera = firstView.camera, original = true,
					intrinsics = new PinholeCamera(300, 300, 272, 272),
				};
				System.Array.Fill(after.landmarks, -1);
				map.Add(after);

				CollectionAssert.DoesNotContain(map.Keyframes, next);
				Assert.AreEqual(4, map.Count);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Link_AnEstablishedLandmarkTakesASightingOnlyWhenRefining()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe b, out Landmark shared);
			try
			{
				map.Establish(shared);
				int before = shared.observations.Count;
				VisualKeyframe sighting = Keyframe(3, map), refining = Keyframe(4, map);

				Assert.IsFalse(map.Link(sighting, 6, shared, 1.5f));
				Assert.AreEqual(before, shared.observations.Count);
				Assert.AreEqual(shared.id, sighting.landmarks[6]);

				Assert.IsTrue(map.Link(refining, 6, shared, 1.5f, true));
				Assert.AreEqual(before + 1, shared.observations.Count);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void Establish_KeepsSightingsWhileRefining_UntilToldToStop()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe b, out Landmark shared);
			try
			{
				map.dropEstablishedSightings = true;
				map.refineEstablished = true;
				map.Establish(shared);
				Assert.AreEqual(2, shared.observations.Count);

				map.StopRefining(shared);
				Assert.AreEqual(0, shared.observations.Count);
				Assert.IsTrue(shared.IsUsable);
			}
			finally
			{
				map.Dispose();
			}
		}

		[Test]
		public void NoteDepthChanged_TheNextSnapshotCarriesTheNewDepth()
		{
			VisualMap map = MapWithSharedLandmark(out VisualKeyframe a, out VisualKeyframe b, out Landmark shared);
			try
			{
				map.Establish(shared);
				VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);
				Assert.AreEqual(0.25f, shared.sentInverseDepth);

				int revision = map.DepthRevision;
				Landmark told = null;
				map.LandmarkDepthChanged += changed => told = changed;
				map.SetDepth(shared.id, 0.1f, 0.002f);
				Assert.AreEqual(shared, told);
				Assert.AreEqual(revision + 1, map.DepthRevision);

				VisualMapData data = VisualMapFile.Snapshot(map, pose => pose, Quaternion.identity, null, Raw, Wire);
				Assert.AreEqual(0.1f, data.landmarks[0].inverseDepth);
				Assert.AreEqual(0.1f, shared.sentInverseDepth);
			}
			finally
			{
				map.Dispose();
			}
		}
	}
}
