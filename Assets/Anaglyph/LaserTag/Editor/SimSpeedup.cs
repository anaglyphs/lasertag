#if UNITY_EDITOR
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Sets up a play session whose walker snaps between samples: pose jumps count as walking,
	/// the walker rather than the head's turn rate decides which frames are sharp, and everything
	/// paced in real seconds runs that many times faster, so it happens as often per meter.
	/// </summary>
	public static class SimSpeedup
	{
		private const float NeverBlurred = 1e6f;
		private static VisualLocalizer configured;
		private static float blurLimit, calmLimit;

		public static void Apply(float speedup, SimWalker walker)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (localizer == null || provider == null) return;

			if (configured != localizer)
			{
				configured = localizer;
				ConfigureLocalizer(localizer, speedup);
				ConfigureProvider(provider, speedup);
				provider.MapSettleSeconds /= speedup;
				localizer.Urgent = localizer.Urgent;
			}

			walker.SnapBetweenSamples(speedup, blurLimit, calmLimit);
			ConfigureJumps(walker.LongestSnap * 1.1f, speedup);
		}

		private static void ConfigureLocalizer(VisualLocalizer localizer, float speedup)
		{
			SerializedObject fields = new(localizer);
			SerializedProperty blur = fields.FindProperty("mapperSettings.maxAngularSpeed"), calm = fields.FindProperty("mapperSettings.calmAngularSpeed");
			blurLimit = blur.floatValue;
			calmLimit = calm.floatValue;
			blur.floatValue = calm.floatValue = NeverBlurred;

			// the walker decides when mapping gets a new frame
			fields.FindProperty("mapperSettings.mappingInterval").floatValue = 0;
			fields.FindProperty("mapperSettings.notNovelRetry").floatValue = 0;

			// spreading work over frames only protects a headset's frame rate. Mapping's holds the walker up, so it gets a frame
			fields.FindProperty("inferenceSettings.framesPerRun").intValue = 1;
			fields.FindProperty("inferenceSettings.urgentFramesPerRun").intValue = 1;
			fields.FindProperty("inferenceSettings.retrievalCandidatesPerFrame").intValue = VisualMap.MaxRetrievalCandidates;
			fields.FindProperty("inferenceSettings.urgentRetrievalCandidatesPerFrame").intValue = VisualMap.MaxRetrievalCandidates;
			fields.FindProperty("mapperSettings.trackRowsPerFrame").intValue = XFeatExtractor.MaxKeypoints;

			foreach (string frames in new[] { "matcherFramesPerRun", "urgentMatcherFramesPerRun" })
			{
				SerializedProperty property = fields.FindProperty("inferenceSettings." + frames);
				property.intValue = Mathf.Max(1, Mathf.RoundToInt(property.intValue / speedup));
			}

			fields.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void ConfigureProvider(VisualColocationConstraintProvider provider, float speedup)
		{
			SerializedObject fields = new(provider);
			foreach (string seconds in new[]
			         {
				         "queryInterval", "loopInterval", "blurredRetry", "loopCooldown", "loopCaptureAfterQuiet", "loopFrameWindow",
				         "watchSettings.holdSeconds", "watchSettings.checkSeconds", "watchSettings.confirmedCheckSeconds",
				         "graphSettings.passSeconds", "graphSettings.solveInterval", "graphSettings.solveCostMultiple",
			         })
				fields.FindProperty(seconds).floatValue /= speedup;

			fields.FindProperty("graphSettings.pairsPerSecond").floatValue *= speedup;
			fields.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void ConfigureJumps(float longestWalkInOneFrame, float speedup)
		{
			if (HeadPoseHistory.Instance != null) HeadPoseHistory.Instance.JumpThreshold = Mathf.Max(HeadPoseHistory.Instance.JumpThreshold, longestWalkInOneFrame);

			SimTrackingDrift drift = Object.FindAnyObjectByType<SimTrackingDrift>();
			if (drift == null) return;
			SerializedObject fields = new(drift);
			SerializedProperty jump = fields.FindProperty("jumpThreshold");
			if (jump.floatValue >= longestWalkInOneFrame) return;
			jump.floatValue = longestWalkInOneFrame;
			fields.FindProperty("stillMetersPerSecond").floatValue *= speedup;
			fields.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
#endif
