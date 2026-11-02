using System;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Colocate: aligning against the authority's map. Relocalize: against this headset's own.
	/// Loop: a revisit of this headset's map (the authority's views merged into it count as
	/// cross-links), which measures drift and becomes a site.
	/// </summary>
	public enum MeasurementPurpose { Colocate, Relocalize, Loop }

	/// <summary>
	/// Whether a solved localization is good enough to act on. Every result is gated on how
	/// well it is pinned down (the solver's yaw and translation sigmas), not on how many points
	/// matched: a map seen from one spot pins yaw but not translation until someone walks
	/// around. Two results that agree are needed before one is applied, unless one is certain
	/// enough alone.
	/// </summary>
	public sealed class MeasurementGate
	{
		[Serializable]
		public struct Thresholds
		{
			public int minInliers;
			[Tooltip("Inlier residuals' RMS in their own sigmas (pixel noise plus landmark depth uncertainty)")]
			public float maxNormalizedRms;
			public float maxYawSigmaDegrees;
			[Tooltip("Ranged inliers a measurement needs. With only far landmarks, yaw and translation trade off and the solver's sigmas don't show it: on device, 9 ranged of 99 inliers gave a yaw 3.6° off at a claimed ±0.3°, and two frames of the same view agreed on it")]
			public int minRangedInliers;
			public float maxTranslationSigma;

			[Header("Single-frame acceptance")]
			public float confidentYawSigmaDegrees;
			public float confidentTranslationSigma;
			public int confidentInliers;
			[Tooltip("Inliers as a share of the correspondences: repeated structure tends to give confident fits from a minority")]
			public float confidentInlierShare;
		}

		[Serializable]
		public sealed class Settings
		{
			[Tooltip("Colocating and relocalizing")]
			public Thresholds alignment = new()
			{
				minInliers = 30, maxNormalizedRms = 2f, maxYawSigmaDegrees = 0.5f, maxTranslationSigma = 0.04f,
				confidentYawSigmaDegrees = 0.15f, confidentTranslationSigma = 0.01f, confidentInliers = 150, confidentInlierShare = 0.6f,
			};

			[Tooltip("Revisits. One frame is enough with fewer inliers: the search is already confined to old views near where the graph puts the camera, and a moving player rarely gives a second frame of the same views in time")]
			public Thresholds drift = new()
			{
				minInliers = 30, maxNormalizedRms = 2f, maxYawSigmaDegrees = 0.5f, minRangedInliers = 20, maxTranslationSigma = 0.04f,
				confidentYawSigmaDegrees = 0.25f, confidentTranslationSigma = 0.02f, confidentInliers = 80, confidentInlierShare = 0.75f,
			};

			[Tooltip("A result this certain is applied without waiting for a second frame")]
			public bool acceptConfidentSingleFrames = true;

			[Header("Agreement")]
			public float agreeTranslation = 0.05f;
			public float agreeYawDegrees = 1f;
			[Tooltip("Loop closures measured this far apart along the path (m) can still agree; between them, tracking drifts on and the correction with it")]
			public float agreePath = 30f;
			[Tooltip("Two revisit results may lean on different old segments, as long as those are this close (m of expected drift): the correction agreeing is the real test")]
			public float anchorTolerance = 0.07f;

			[Header("Plausibility")]
			[Tooltip("A revisit corrects the drift walked since the last measurement: one moving the camera by more than this plus the drift model's allowance found the wrong place, such as the far end of a symmetric hallway")]
			public float plausibleMeters = 0.5f;
			public float plausibleDegrees = 5f;
		}

		/// <summary>One solved query, as the gate weighs it.</summary>
		public sealed class Frame
		{
			public MeasurementPurpose purpose;
			public int generation, epoch;
			/// <summary>The track segment the query was captured on, and the one the map's views it matched hang off.</summary>
			public ulong node, anchor;
			/// <summary>The query camera, where its segment's correction placed it.</summary>
			public float3 camera;
			public Pose4 correction;
			public GravityPnPResult solve;
			/// <summary>The map points the solve stood on; may be null.</summary>
			public Vector3[] features;
		}

		/// <summary>The correction two results agree on, about the features they stood on.</summary>
		public struct Agreed
		{
			public Pose4 correction;
			public float3 focus;
			public Vector3[] features;
			/// <summary>At the focus: the two together pin translation as well as the better of them.</summary>
			public float sigmaTranslation;
			/// <summary>One frame was certain enough to stand in for both.</summary>
			public bool alone;
		}

		private readonly Settings settings;
		private readonly Func<ulong, ulong, float> drift, walkedBetween;

		// the first of two results that must agree
		private Frame candidate;

		/// <param name="settings">Read live, so they can be tuned while running.</param>
		/// <param name="drift">How far tracking may have drifted between two track segments, meters.</param>
		/// <param name="walkedBetween">How far the head walked between two track segments, meters.</param>
		public MeasurementGate(Settings settings, Func<ulong, ulong, float> drift, Func<ulong, ulong, float> walkedBetween)
		{
			this.settings = settings;
			this.drift = drift;
			this.walkedBetween = walkedBetween;
		}

		/// <summary>How fast tracking drifts: what the allowances for a walk come from.</summary>
		public DriftModel Model { get; set; } = DriftModel.Default;

		/// <summary>Forgets the result waiting on a second frame.</summary>
		public void Clear() => candidate = null;

		private ref readonly Thresholds For(MeasurementPurpose purpose)
		{
			if (purpose == MeasurementPurpose.Loop) return ref settings.drift;
			return ref settings.alignment;
		}

		/// <summary>Why a solve is too loose to use, or null.</summary>
		public string Rejection(in GravityPnPResult pose, MeasurementPurpose purpose)
		{
			ref readonly Thresholds t = ref For(purpose);
			if (!pose.success) return pose.failure ?? "no pose";
			if (pose.inliers < t.minInliers) return $"{pose.inliers} inliers < {t.minInliers}";
			if (pose.normalizedRms > t.maxNormalizedRms) return $"residuals {pose.normalizedRms:F2}σ > {t.maxNormalizedRms}σ";
			if (math.degrees(pose.yawSigma) > t.maxYawSigmaDegrees) return $"yaw ±{math.degrees(pose.yawSigma):F2}° > {t.maxYawSigmaDegrees}°";
			if (pose.rangedInliers < t.minRangedInliers)
				return $"{pose.rangedInliers} ranged inliers < {t.minRangedInliers}: far landmarks alone leave yaw and translation trading off";
			if (pose.translationSigma > t.maxTranslationSigma)
				return $"translation ±{pose.translationSigma * 100:F1}cm > {t.maxTranslationSigma * 100:F0}cm: the map needs ranged landmarks here (walk around)";
			return null;
		}

		/// <summary>
		/// Why a drift measurement can't be the drift <paramref name="walked"/> since the
		/// correction it is relative to was measured, or null: tracking drifts a little per
		/// meter, so a step much larger found the wrong place.
		/// </summary>
		public string Implausible(Pose4 step, float3 camera, float walked)
		{
			float maxMove = settings.plausibleMeters + Model.PositionAllowance(walked);
			float moved = math.distance(step.Apply(camera), camera);
			float turned = math.degrees(math.abs(Pose4.WrapAngle(step.yaw)));
			float maxTurn = settings.plausibleDegrees + math.degrees(Model.YawAllowance(walked));
			if (moved > maxMove) return $"moves the camera {moved:F2} m after {walked:F0} m walked (≤ {maxMove:F2} m)";
			if (turned > maxTurn) return $"turns the camera {turned:F1}° after {walked:F0} m walked (≤ {maxTurn:F1}°)";
			return null;
		}

		/// <summary>
		/// Whether this result is enough to act on: two independent frames must agree, unless
		/// one is certain enough on its own. One that doesn't agree becomes the candidate, and
		/// the caller should ask for the confirming frame at once, while the old views are still
		/// in sight. A frame that fails the gate says nothing against the candidate, and doesn't
		/// come here: it stays for the next good one.
		/// </summary>
		public bool TryAgree(Frame frame, out Agreed agreed)
		{
			agreed = default;
			bool confident = Confident(frame.solve, frame.purpose);
			if (!confident && !AgreesWithCandidate(frame))
			{
				candidate = frame;
				return false;
			}

			// a confident frame stands in for both
			agreed = Combine(confident ? frame : candidate, frame);
			agreed.alone = confident;
			candidate = null;
			return true;
		}

		// certain enough to act on from one frame
		private bool Confident(in GravityPnPResult pose, MeasurementPurpose purpose)
		{
			ref readonly Thresholds t = ref For(purpose);
			return settings.acceptConfidentSingleFrames &&
			       math.degrees(pose.yawSigma) <= t.confidentYawSigmaDegrees &&
			       pose.translationSigma <= t.confidentTranslationSigma &&
			       pose.inliers >= t.confidentInliers &&
			       pose.inliers >= t.confidentInlierShare * pose.correspondences;
		}

		private bool AgreesWithCandidate(Frame frame)
		{
			if (candidate == null || candidate.purpose != frame.purpose || candidate.generation != frame.generation || candidate.epoch != frame.epoch) return false;

			// a moving player's two loop measurements come seconds and meters apart, and tracking
			// drifts on between them: they may differ by as much as it could have drifted
			float walked = 0;
			if (frame.purpose == MeasurementPurpose.Loop)
			{
				// the old segments matched may differ (a revisit's), but must be one region of the old map
				walked = walkedBetween(candidate.node, frame.node);
				if (!(walked <= settings.agreePath)) return false;
				if (drift(candidate.anchor, frame.anchor) > settings.anchorTolerance) return false;
			}

			float yawDifference = math.abs(Pose4.WrapAngle(frame.correction.yaw - candidate.correction.yaw));
			if (yawDifference > math.radians(settings.agreeYawDegrees) + Model.YawAllowance(walked)) return false;

			// where the two put the camera: their translations alone differ by the yaw
			// difference times the camera's distance from the world origin
			float apart = math.distance(candidate.correction.Apply(frame.camera), frame.correction.Apply(frame.camera));
			float candidateSigma = candidate.solve.translationSigma, sigma = frame.solve.translationSigma;
			float tolerance = math.max(settings.agreeTranslation, 3 * math.sqrt(candidateSigma * candidateSigma + sigma * sigma));
			return apart <= tolerance + Model.PositionAllowance(walked);
		}

		/// <summary>
		/// The two results' correction, measured where the map has the features the frames
		/// stood on, not at the camera: the solves pin the correction best there (the yaw's
		/// error at the camera rides its lever arm to them), and a revisit of the same features
		/// lands there again wherever the head stands. Averaged about it too, not the world
		/// origin, which may be tens of meters away and turns a hundredth of a degree into
		/// centimeters there.
		/// </summary>
		private static Agreed Combine(Frame first, Frame frame)
		{
			Vector3[] features = first == frame || first.features == null ? frame.features
				: frame.features == null ? first.features
				: System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(first.features, frame.features));

			float3 focus = Focus(features, frame.camera);
			float firstSigma = first.solve.TranslationSigmaAt(focus), sigma = frame.solve.TranslationSigmaAt(focus);
			float yawMean = first.correction.yaw + Pose4.WrapAngle(frame.correction.yaw - first.correction.yaw) * 0.5f;
			// weighted by how well each pinned translation
			float firstWeight = 1f / math.max(firstSigma * firstSigma, 1e-8f);
			float weight = 1f / math.max(sigma * sigma, 1e-8f);
			float3 focusMean = (first.correction.Apply(focus) * firstWeight + frame.correction.Apply(focus) * weight) / (firstWeight + weight);
			return new Agreed
			{
				correction = new Pose4(focusMean - Pose4.RotateY(focus, yawMean), yawMean),
				focus = focus,
				features = features,
				sigmaTranslation = math.min(sigma, firstSigma),
			};
		}

		// the middle of the map points a measurement stood on; the camera when it has none
		private static float3 Focus(Vector3[] features, float3 camera)
		{
			if (features == null || features.Length == 0) return camera;
			float3 sum = 0;
			foreach (Vector3 f in features) sum += (float3)f;
			return sum / features.Length;
		}
	}
}
