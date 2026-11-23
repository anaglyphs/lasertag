using System.Collections.Generic;
using Anaglyph.XR.VisualLocalization;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Anaglyph.LaserTag.Tests
{
	public class GravityPnPTests
	{
		private static readonly PinholeCamera Intrinsics = new(440, 440, 319.5f, 239.5f);
		private static GravityPnP.Settings Settings => GravityPnP.Settings.Default;

		// a camera in the query world looking at landmarks the map frame holds, turned and
		// shifted; each landmark seen from a map-frame viewpoint and ranged to depthSigma
		private static List<Correspondence> Scene(float yaw, float3 translation, quaternion cameraRotation, float3 cameraCenter,
			int count, float outlierShare, float noisePixels, uint seed, float2 depthRange, float depthSigma, bool ranged = true)
		{
			Random random = new(seed);
			float3 viewpoint = GravityPnP.RotateY(cameraCenter, yaw) + translation + new float3(0.6f, 0.1f, -0.4f);
			List<Correspondence> result = new();
			while (result.Count < count)
			{
				float2 pixel = random.NextFloat2(new float2(0, 0), new float2(639, 479));
				float depth = random.NextFloat(depthRange.x, depthRange.y);
				float3 ray = Intrinsics.Ray(pixel);
				float3 queryPoint = cameraCenter + math.mul(cameraRotation, ray * (depth / ray.z));
				float3 mapPoint = GravityPnP.RotateY(queryPoint, yaw) + translation;

				if (random.NextFloat() < outlierShare) mapPoint += random.NextFloat3Direction() * random.NextFloat(0.5f, 3f) * depth / 3;
				float2 observed = pixel + random.NextFloat2Direction() * random.NextFloat(0, noisePixels);

				Correspondence c = Correspondence.ToPoint(observed, Intrinsics.Ray(observed), mapPoint, viewpoint, depthSigma);
				if (!ranged)
				{
					// too far to range: a direction, ρ ≈ 0 to within what a 1 m baseline resolves
					// at 1.5 px (σρ ≈ σθ / baseline ≈ 0.0035, i.e. nearer than ~100 m would show)
					c.inverseDepth = 0;
					c.inverseDepthSigma = 0.005f;
				}

				result.Add(c);
			}

			return result;
		}

		private static float YawError(float a, float b) => math.degrees(math.abs(math.atan2(math.sin(a - b), math.cos(a - b))));

		[Test]
		public void RecoversYawAndTranslationThroughOutliersAndNoise()
		{
			float yaw = math.radians(137f);
			float3 translation = new(1.3f, 0.12f, -2.4f);
			quaternion cameraRotation = math.mul(quaternion.RotateY(math.radians(-25f)), quaternion.RotateX(math.radians(12f)));
			float3 cameraCenter = new(0.4f, 1.6f, -0.3f);

			List<Correspondence> correspondences = Scene(yaw, translation, cameraRotation, cameraCenter, 300, 0.4f, 0.7f, 7, new float2(1.5f, 6f), 0.01f);
			GravityPnPResult result = GravityPnP.Solve(correspondences, Intrinsics, cameraRotation, cameraCenter, Settings);

			Assert.IsTrue(result.success, result.ToString());
			Assert.That(YawError(result.yaw, yaw), Is.LessThan(0.1f), result.ToString());
			Assert.That(math.distance(result.translation, translation), Is.LessThan(0.01f), result.ToString());
			Assert.That(result.inliers, Is.GreaterThan(150), result.ToString());
			Assert.That(result.rmsPixels, Is.LessThan(1f), result.ToString());
			Assert.That(result.translationSigma, Is.LessThan(0.02f), result.ToString());
			Assert.That(math.degrees(result.yawSigma), Is.LessThan(0.2f), result.ToString());
		}

		[Test]
		public void ExactDataIsExact()
		{
			float yaw = math.radians(-71f);
			float3 translation = new(-0.6f, -0.05f, 0.9f);
			quaternion cameraRotation = quaternion.RotateY(math.radians(40f));
			float3 cameraCenter = new(2f, 1.5f, 1f);

			List<Correspondence> correspondences = Scene(yaw, translation, cameraRotation, cameraCenter, 50, 0, 0, 3, new float2(1.5f, 6f), 0.001f);
			GravityPnPResult result = GravityPnP.Solve(correspondences, Intrinsics, cameraRotation, cameraCenter, Settings);

			Assert.IsTrue(result.success, result.ToString());
			Assert.That(math.distance(result.translation, translation), Is.LessThan(1e-3f), result.ToString());
			Assert.That(result.inliers, Is.EqualTo(50), result.ToString());
		}

		private static VisualQuery QueryAt(Pose trackingSpace, Pose cameraInSpace, long timestampNs) => new()
		{
			trackingSpace = trackingSpace,
			cameraInTrackingSpace = cameraInSpace,
			camera = new Pose(trackingSpace.position + trackingSpace.rotation * cameraInSpace.position, trackingSpace.rotation * cameraInSpace.rotation),
			timestampNs = timestampNs,
		};

		[Test]
		public void FramesTooThinAloneSolveTogetherThroughThePool()
		{
			float yaw = math.radians(63f);
			float3 translation = new(-3.1f, 0.2f, 5.4f);
			Pose space = new(new Vector3(2f, 0, -1f), Quaternion.Euler(0, 35f, 0));
			VisualQuery first = QueryAt(space, new Pose(new Vector3(0, 1.6f, 0), Quaternion.Euler(5f, -20f, 0)), 1_000_000_000);
			VisualQuery second = QueryAt(space, new Pose(new Vector3(0.8f, 1.6f, 0.3f), Quaternion.Euler(-3f, 30f, 0)), 2_000_000_000);
			VisualQuery late = QueryAt(space, new Pose(new Vector3(1.5f, 1.6f, 0.5f), Quaternion.Euler(0, 10f, 0)), 20_000_000_000);

			List<Correspondence> Seen(VisualQuery q, uint seed) =>
				Scene(yaw, translation, q.camera.rotation, q.camera.position, 5, 0, 0, seed, new float2(1.5f, 6f), 0.001f);

			List<Correspondence> firstMatches = Seen(first, 5), secondMatches = Seen(second, 9);
			Assert.IsFalse(GravityPnP.Solve(secondMatches, Intrinsics, second.camera.rotation, second.camera.position, Settings).success);

			QueryPool pool = new();
			pool.Add(first, firstMatches);
			(Pose[] cameras, List<Correspondence> matches) = pool.With(second, secondMatches);
			Assert.That(cameras.Length, Is.EqualTo(2));
			GravityPnPResult result = GravityPnP.Solve(matches, Intrinsics, cameras, Settings);

			Assert.IsTrue(result.success, result.ToString());
			Assert.That(result.inliers, Is.EqualTo(10), result.ToString());
			Assert.That(YawError(result.yaw, yaw), Is.LessThan(0.1f), result.ToString());
			Assert.That(math.distance(result.translation, translation), Is.LessThan(5e-3f), result.ToString());

			pool.Add(second, secondMatches);
			Assert.That(pool.With(late, Seen(late, 4)).cameras.Length, Is.EqualTo(1), "frames past the window are dropped");
		}

		[Test]
		public void ACameraFarFromWhereLandmarksWereRangedIsLessCertain()
		{
			float3 cameraCenter = new(0, 1.6f, 0);
			List<Correspondence> seen = Scene(0, 0, quaternion.identity, cameraCenter, 80, 0, 0.5f, 5, new float2(6f, 10f), 1f);

			// the same landmarks, ranged from beside the camera and from 6 m to its side
			GravityPnPResult Solved(float3 rangedFrom)
			{
				List<Correspondence> moved = new();
				foreach (Correspondence c in seen) moved.Add(Correspondence.ToPoint(c.pixel, c.ray, c.Point, rangedFrom, 1f));
				GravityPnPResult result = GravityPnP.Solve(moved, Intrinsics, quaternion.identity, cameraCenter, Settings);
				Assert.IsTrue(result.success, result.ToString());
				GravityPnP.AddDepthScaleUncertainty(ref result, moved, Settings);
				return result;
			}

			GravityPnPResult near = Solved(cameraCenter + new float3(0.2f, 0, 0)), far = Solved(cameraCenter + new float3(6f, 0, 0));
			Assert.That(near.translationSigma, Is.LessThan(0.08f), near.ToString());
			Assert.That(far.translationSigma, Is.GreaterThan(0.55f), far.ToString());
			Assert.That(math.degrees(far.yawSigma), Is.LessThan(1f), far.ToString());
		}

		[Test]
		public void YawHoldsFromAcrossTheRoomWhenTheLandmarksAreRangedTooNear()
		{
			float yaw = math.radians(40f);
			float3 translation = new(2f, 0.1f, -1f);
			quaternion cameraRotation = quaternion.RotateY(math.radians(-15f));
			float3 cameraCenter = new(0.5f, 1.6f, 0.2f);
			List<Correspondence> seen = Scene(yaw, translation, cameraRotation, cameraCenter, 150, 0.1f, 0.5f, 21, new float2(5f, 9f), 0.01f);

			// ranged from 6 m to the camera's side, every depth past 3 m halved back toward it: no camera sees that scene rigidly
			float3 rangedFrom = GravityPnP.RotateY(cameraCenter + math.mul(cameraRotation, new float3(6f, 0, 0)), yaw) + translation;
			List<Correspondence> wrong = new();
			foreach (Correspondence c in seen)
			{
				float3 ray = c.Point - rangedFrom;
				float depth = math.length(ray), claimed = 3f + 0.5f * (depth - 3f);
				wrong.Add(Correspondence.ToPoint(c.pixel, c.ray, rangedFrom + ray / depth * claimed, rangedFrom, 0.15f * claimed));
			}

			GravityPnPResult result = GravityPnP.Solve(wrong, Intrinsics, cameraRotation, cameraCenter, Settings);
			Assert.IsTrue(result.success, result.ToString());
			Assert.That(YawError(result.yaw, yaw), Is.LessThan(1f), result.ToString());
			Assert.That(result.inliers, Is.GreaterThan(90), result.ToString());

			GravityPnP.AddDepthScaleUncertainty(ref result, wrong, Settings);
			float3 truth = GravityPnP.RotateY(cameraCenter, yaw) + translation;
			Assert.That(math.distance(result.cameraCenter, truth), Is.LessThan(3 * result.translationSigma), result.ToString());
		}

		[Test]
		public void TwoStandingPointsRangeWhatTheMapRangedTooNear()
		{
			float yaw = math.radians(40f);
			float3 translation = new(2f, 0.1f, -1f);
			quaternion cameraRotation = quaternion.RotateY(math.radians(-15f));
			float3 first = new(0.5f, 1.6f, 0.2f), second = first + math.mul(cameraRotation, new float3(2f, 0, 0.5f));
			List<Correspondence> seen = Scene(yaw, translation, cameraRotation, first, 150, 0, 0.3f, 21, new float2(5f, 9f), 0.01f);

			// the map as the test before has it, every depth past 3 m halved back toward where it was ranged from
			float3 rangedFrom = GravityPnP.RotateY(first + math.mul(cameraRotation, new float3(6f, 0, 0)), yaw) + translation;
			List<Correspondence> alone = new(), together = new();
			for (int i = 0; i < seen.Count; i++)
			{
				float3 point = seen[i].Point, ray = point - rangedFrom;
				float depth = math.length(ray), claimed = 3f + 0.5f * (depth - 3f);
				Correspondence sighting = Correspondence.ToPoint(seen[i].pixel, seen[i].ray, rangedFrom + ray / depth * claimed, rangedFrom, 0.15f * claimed);
				sighting.landmark = i + 1;
				alone.Add(sighting);
				together.Add(sighting);

				// the same thing where the second standing point really sees it
				float3 inQueryWorld = GravityPnP.RotateY(point - translation, -yaw);
				if (!Intrinsics.Project(math.mul(math.conjugate(cameraRotation), inQueryWorld - second), out float2 pixel)) continue;
				if (pixel.x < 0 || pixel.y < 0 || pixel.x > 639 || pixel.y > 479) continue;
				sighting.pixel = pixel;
				sighting.ray = Intrinsics.Ray(pixel);
				sighting.camera = 1;
				together.Add(sighting);
			}

			float3 truth = GravityPnP.RotateY(first, yaw) + translation;
			GravityPnPResult one = GravityPnP.Solve(alone, Intrinsics, cameraRotation, first, Settings);
			GravityPnPResult two = GravityPnP.Solve(together, Intrinsics, new[] { new Pose(first, cameraRotation), new Pose(second, cameraRotation) }, Settings);
			Assert.IsTrue(one.success && two.success, $"{one} | {two}");
			Assert.That(math.distance(one.cameraCenter, truth), Is.GreaterThan(1f), one.ToString());
			Assert.That(math.distance(two.cameraCenter, truth), Is.LessThan(0.4f), two.ToString());
			Assert.That(YawError(two.yaw, yaw), Is.LessThan(1f), two.ToString());
		}

		[Test]
		public void FailsWithoutConsensus()
		{
			Random random = new(11);
			List<Correspondence> junk = new();
			for (int i = 0; i < 100; i++)
			{
				float2 pixel = random.NextFloat2(0, new float2(639, 479));
				junk.Add(Correspondence.ToPoint(pixel, Intrinsics.Ray(pixel), random.NextFloat3(-5, 5), new float3(0, 1.6f, 0), 0.01f));
			}

			GravityPnPResult result = GravityPnP.Solve(junk, Intrinsics, quaternion.identity, new float3(0, 1.6f, 0), Settings);
			Assert.That(!result.success || result.inliers < 20, result.ToString());
		}

		// a skyline: no depth at all, yet yaw is pinned and translation is honestly unknown.
		// (Nearer far points, a building a block away, pin yaw only as well as the camera's
		// position is known: a meter at 300 m is 0.2°, which is why loop closures pass the
		// graph's position prior)
		[Test]
		public void FarLandmarksPinYawButNotTranslation()
		{
			float yaw = math.radians(23f);
			float3 translation = new(0.8f, 0, -1.1f);
			quaternion cameraRotation = math.mul(quaternion.RotateY(math.radians(10f)), quaternion.RotateX(math.radians(-5f)));
			float3 cameraCenter = new(0, 1.6f, 0);

			List<Correspondence> correspondences = Scene(yaw, translation, cameraRotation, cameraCenter, 200, 0.3f, 0.7f, 5, new float2(3000f, 8000f), 0, ranged: false);
			GravityPnPResult result = GravityPnP.Solve(correspondences, Intrinsics, cameraRotation, cameraCenter, Settings);

			Assert.IsTrue(result.success, result.ToString());
			Assert.That(YawError(result.yaw, yaw), Is.LessThan(0.1f), result.ToString());
			Assert.That(math.degrees(result.yawSigma), Is.LessThan(0.2f), result.ToString());
			Assert.That(result.translationSigma, Is.GreaterThan(1f), result.ToString());
			Assert.That(result.rangedInliers, Is.EqualTo(0), result.ToString());
		}

		// near and far together: near ones fix translation, far ones join in on yaw
		[Test]
		public void MixedNearAndFar()
		{
			float yaw = math.radians(-160f);
			float3 translation = new(-3f, 0.05f, 2f);
			quaternion cameraRotation = quaternion.RotateY(math.radians(75f));
			float3 cameraCenter = new(1f, 1.7f, 4f);

			List<Correspondence> correspondences = Scene(yaw, translation, cameraRotation, cameraCenter, 40, 0.2f, 0.7f, 9, new float2(1.5f, 4f), 0.01f);
			correspondences.AddRange(Scene(yaw, translation, cameraRotation, cameraCenter, 150, 0.2f, 0.7f, 10, new float2(100f, 300f), 0, ranged: false));
			GravityPnPResult result = GravityPnP.Solve(correspondences, Intrinsics, cameraRotation, cameraCenter, Settings);

			Assert.IsTrue(result.success, result.ToString());
			Assert.That(YawError(result.yaw, yaw), Is.LessThan(0.1f), result.ToString());
			Assert.That(math.distance(result.translation, translation), Is.LessThan(0.03f), result.ToString());
			Assert.That(result.translationSigma, Is.LessThan(0.04f), result.ToString());
		}
	}

	public class LandmarkTriangulationTests
	{
		private sealed class StubAnchoring : IKeyframeAnchoring
		{
			public readonly Dictionary<ulong, Pose> nodes = new();
			public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
			{
				node = 0;
				cameraInNode = default;
				return false;
			}

			public bool TryGetNodeWorld(ulong node, out Pose world) => nodes.TryGetValue(node, out world);
			public float Drift(ulong a, ulong b) => 0;
		}

		private const float PixelSigma = 1.5f, F = 440f;

		// sightings of a world point from cameras along a line, each its own node
		private static Landmark Track(float3 point, float baseline, int cameras, StubAnchoring anchoring, float pixelNoise, uint seed)
		{
			Random random = new(seed);
			Landmark landmark = null;
			for (int k = 0; k < cameras; k++)
			{
				ulong node = (ulong)(k + 1);
				anchoring.nodes[node] = new Pose(new Vector3(k * baseline / math.max(1, cameras - 1), 1.6f, 0), Quaternion.Euler(0, 15f * k, 0));
				Pose cameraInNode = new(new Vector3(0, 0.05f, 0.08f), Quaternion.Euler(-4f, -15f * k, 0));
				Landmark.TryCamera(anchoring, node, cameraInNode, out Pose camera);

				float3 ray = math.normalize(math.mul(math.conjugate(camera.rotation), point - (float3)camera.position));
				float3 noise = random.NextFloat3Direction() * random.NextFloat(0, pixelNoise / F);
				ray = math.normalize(ray + noise - math.dot(noise, ray) * ray);

				if (landmark == null)
					landmark = new Landmark { node = node, reference = cameraInNode, bearing = ray, inverseDepth = 0 };

				landmark.observations.Add(new Observation { node = node, cameraInNode = cameraInNode, ray = ray, angularSigma = PixelSigma / F });
			}

			return landmark;
		}

		[Test]
		public void TriangulatesANearPoint()
		{
			StubAnchoring anchoring = new();
			float3 point = new(1.2f, 1.1f, 3.5f);
			Landmark landmark = Track(point, 1f, 4, anchoring, 0.5f, 3);
			Assert.IsNull(landmark.Estimate(anchoring));

			landmark.TryPlace(anchoring, out float3 origin, out _);
			float depth = math.distance(origin, point);
			Assert.That(math.abs(1f / landmark.inverseDepth - depth), Is.LessThan(0.03f), $"ρ={landmark.inverseDepth} σ={landmark.inverseDepthSigma}");
			Assert.That(landmark.inverseDepthSigma, Is.LessThan(0.25f * landmark.inverseDepth), $"ρ={landmark.inverseDepth} σ={landmark.inverseDepthSigma}");
		}

		// both views from one spot, the second turned: no baseline to triangulate with
		private static Landmark SeenTurningOnTheSpot(float3 point, StubAnchoring anchoring)
		{
			anchoring.nodes[1] = new Pose(new Vector3(0, 1.6f, 0), Quaternion.identity);
			Pose first = new(Vector3.zero, Quaternion.identity), second = new(Vector3.zero, Quaternion.Euler(0, 20f, 0));
			float3 RayFrom(Pose cameraInNode)
			{
				Landmark.TryCamera(anchoring, 1, cameraInNode, out Pose camera);
				return math.normalize(math.mul(math.conjugate(camera.rotation), point - (float3)camera.position));
			}

			Landmark landmark = new() { node = 1, reference = first, bearing = RayFrom(first) };
			landmark.observations.Add(new Observation { node = 1, cameraInNode = first, ray = RayFrom(first), angularSigma = PixelSigma / F });
			landmark.observations.Add(new Observation { node = 1, cameraInNode = second, ray = RayFrom(second), angularSigma = PixelSigma / F });
			return landmark;
		}

		[Test]
		public void ADepthSensorRangesWhatTurningOnTheSpotCannot()
		{
			StubAnchoring anchoring = new();
			float3 point = new(0.5f, 1.4f, 4f);
			Landmark unsensed = SeenTurningOnTheSpot(point, anchoring);
			unsensed.Estimate(anchoring);
			Assert.That(unsensed.inverseDepthSigma, Is.GreaterThan(0.25f * unsensed.inverseDepth), "no baseline, no range");

			Landmark landmark = SeenTurningOnTheSpot(point, anchoring);
			landmark.TryPlace(anchoring, out float3 origin, out _);
			float depth = math.distance(origin, point);
			landmark.SetSensedRange(new SensedRange { meters = depth, sigma = 0.1f });
			Assert.IsNull(landmark.Estimate(anchoring));
			Assert.That(1f / landmark.inverseDepth, Is.EqualTo(depth).Within(0.05f));
			Assert.That(landmark.inverseDepthSigma * depth * depth, Is.EqualTo(0.1f).Within(0.01f), "as sure as the sensor is");
		}

		[Test]
		public void ADepthReadingTheSightingsContradictIsDropped()
		{
			StubAnchoring anchoring = new();
			float3 point = new(1.2f, 1.1f, 2f);
			Landmark landmark = Track(point, 1f, 4, anchoring, 0.5f, 3);
			landmark.TryPlace(anchoring, out float3 origin, out _);
			float depth = math.distance(origin, point);
			// the ray grazed an edge and read the wall behind
			landmark.SetSensedRange(new SensedRange { meters = depth + 4f, sigma = 0.15f });
			Assert.IsNull(landmark.Estimate(anchoring), "no sighting is given up for it");
			Assert.That(1f / landmark.inverseDepth, Is.EqualTo(depth).Within(0.05f));
			Assert.That(landmark.sensedInverseDepthSigma, Is.EqualTo(0f));
		}

		[Test]
		public void FarPointStaysADirection()
		{
			StubAnchoring anchoring = new();
			Landmark landmark = Track(new float3(80f, 20f, 300f), 1f, 3, anchoring, 0.5f, 4);
			landmark.Estimate(anchoring);

			// depth unknowable from a 1 m baseline: ρ near zero, and uncertain relative to itself
			Assert.That(landmark.inverseDepth, Is.LessThan(0.02f), $"ρ={landmark.inverseDepth} σ={landmark.inverseDepthSigma}");
			Assert.That(landmark.inverseDepthSigma, Is.GreaterThan(0.25f * landmark.inverseDepth), $"ρ={landmark.inverseDepth} σ={landmark.inverseDepthSigma}");
		}

		[Test]
		public void LongerBaselineSharpensAFarPoint()
		{
			StubAnchoring near = new(), far = new();
			float3 point = new(1f, 2f, 20f);
			Landmark shortWalk = Track(point, 0.5f, 4, near, 0.3f, 4);
			Landmark longWalk = Track(point, 4f, 4, far, 0.3f, 4);
			shortWalk.Estimate(near);
			longWalk.Estimate(far);
			Assert.That(longWalk.inverseDepthSigma, Is.LessThan(0.25f * shortWalk.inverseDepthSigma));
		}

		[Test]
		public void AFitAppliedLaterKeepsWhatHappenedToItsSightingMeanwhile()
		{
			StubAnchoring anchoring = new();
			Landmark landmark = Track(new float3(0.5f, 1.3f, 2.5f), 1.2f, 5, anchoring, 0.3f, 5);
			Observation wrong = landmark.observations[3];
			wrong.ray = math.normalize(wrong.ray + new float3(0.05f, -0.03f, 0));
			landmark.observations[3] = wrong;

			Landmark.Fitted fit = landmark.Fit(landmark.observations.ToArray(), anchoring);
			Assert.AreEqual(5, landmark.observations.Count);

			List<Observation> removed = landmark.Apply(fit);
			Assert.AreEqual(4, landmark.observations.Count);
			Assert.AreEqual(1, removed.Count);
			Assert.That(math.all(removed[0].ray == wrong.ray));
		}

		[Test]
		public void DropsASightingOfSomethingElse()
		{
			StubAnchoring anchoring = new();
			Landmark landmark = Track(new float3(0.5f, 1.3f, 2.5f), 1.2f, 5, anchoring, 0.3f, 5);
			Observation wrong = landmark.observations[3];
			wrong.ray = math.normalize(wrong.ray + new float3(0.05f, -0.03f, 0));
			landmark.observations[3] = wrong;

			List<Observation> dropped = landmark.Estimate(anchoring);
			Assert.IsNotNull(dropped);
			Assert.That(dropped.Count, Is.EqualTo(1));
			Assert.That(math.all(dropped[0].ray == wrong.ray));
		}
	}
}

namespace Anaglyph.LaserTag.Tests
{
	public class VisualMapCoverageTests
	{
		private static int sequence;

		// a first view of its place unless told otherwise; the localizer marks revisits of a
		// place it already has views of, and those are the first to go when the map is full
		private static Anaglyph.XR.VisualLocalization.VisualKeyframe At(float x, float z, float headingDegrees, bool original = true)
		{
			var features = new Anaglyph.XR.VisualLocalization.FeatureSet(4);
			int[] links = { -1, -1, -1, -1 };
			return new Anaglyph.XR.VisualLocalization.VisualKeyframe
			{
				features = features,
				landmarks = links,
				camera = new UnityEngine.Pose(new UnityEngine.Vector3(x, 1.6f, z), UnityEngine.Quaternion.Euler(0, headingDegrees, 0)),
				timestampNs = ++sequence,
				original = original,
			};
		}

		[NUnit.Framework.Test]
		public void EvictsFromTheMostCrowdedPlaceAndHeading()
		{
			using var map = new Anaglyph.XR.VisualLocalization.VisualMap(4);
			var a1 = At(0.2f, 0.2f, 10); var a2 = At(0.5f, 0.4f, 15, original: false); var a3 = At(0.7f, 0.6f, 5, original: false);
			var b = At(5.5f, 5.5f, 10);
			map.Add(a1); map.Add(a2); map.Add(a3); map.Add(b);

			map.Add(At(9.5f, 9.5f, 200));

			NUnit.Framework.Assert.That(map.Keyframes, NUnit.Framework.Has.Member(b), "the lone view of a place stays");
			NUnit.Framework.Assert.That(map.Keyframes, NUnit.Framework.Has.No.Member(a3), "the newest of the crowded bucket goes");
			NUnit.Framework.Assert.That(map.CoverageLevel, NUnit.Framework.Is.EqualTo(0));
		}

		[NUnit.Framework.Test]
		public void CoarsensWhenEveryBucketHasOneView()
		{
			using var map = new Anaglyph.XR.VisualLocalization.VisualMap(3);
			map.Add(At(0.5f, 0.5f, 0)); map.Add(At(1.5f, 0.5f, 0)); map.Add(At(10.5f, 10.5f, 0));

			map.Add(At(20.5f, 20.5f, 0));

			NUnit.Framework.Assert.That(map.CoverageLevel, NUnit.Framework.Is.GreaterThan(0));
			NUnit.Framework.Assert.That(map.Count, NUnit.Framework.Is.EqualTo(3));
		}
	}
}
