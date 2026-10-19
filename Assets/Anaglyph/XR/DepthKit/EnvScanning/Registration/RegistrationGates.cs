using System;

namespace Anaglyph.XR.DepthKit.EnvScanning.Registration
{
	/// <summary>
	/// When a registration may be attempted and when its result may be trusted. One set
	/// serves everything that acts on a registration (colocation, relocalization, loop
	/// closure, ScanRegistrationBench), so the scoring is tuned in one place. Sizes scale
	/// with the scanner: areas in chunk faces (chunk world size²), converted to surface
	/// samples by <see cref="TsdfRegistration.SurfaceSamples"/>; tolerances in voxels
	/// </summary>
	[Serializable]
	public sealed class RegistrationGates
	{
		// how firmly the scan to register must pin its weakest degree of freedom before
		// an attempt is made (TsdfRegistration.SourceConstraint, roughly surface facing that
		// way), in chunk faces. Measured in the sim room (3 m chunks, 0.1 m voxels): a lone
		// wall or floor scores 0.001-0.014, one standing view of the room 0.23-0.33, so this
		// waits until the headset has looked around a little, and never attempts on a
		// hallway, wall or floor alone
		public float minSourceConstraintChunkFaces = 0.33f;
		// the most of the scan's overlapping surface that may land where the target saw
		// open air (RegistrationResult.contradiction). This is the gate that rejects a view
		// of space the target never covered, dropped somewhere it half fits. Swept with
		// ScanRegistrationBench.SweepAsync: right poses 0.00-0.05 on sim scans and 0.02-0.06
		// on Quest scans (a relocalization scratch against its map), wrong ones from 0.17
		// (sim) and 0.41 (Quest). Two young scans of one room carve noisy air and can reach
		// 0.34 at the right pose; those are refused until the scans mature
		public float maxContradiction = 0.12f;
		// a floor only: agreement measures how closely surfaces line up, which sensor noise
		// and map warp set more than the pose does. Right poses score 0.61-0.71 on sim scans
		// but 0.39-0.55 on Quest scans, whose surfaces disagree by ~5 cm at the right pose;
		// wrong ones overlap both, so contradiction does the deciding
		public float minAgreement = 0.25f;
		// how much of the scan's surface must land in the target's observed space, in chunk
		// faces; absolute, so scanning more than the target never hurts. Well over what one
		// room corner covers, so a corner alone can't match every corner
		public float minOverlapChunkFaces = 1.4f;
		// the winner's lead over the best rival peak, as a fraction of its own score
		public float minMarginFraction = 0.15f;
		public float maxRefinedRmsVoxels = 1f;

		public float MinSourceConstraint(EnvScanner scanner) => TsdfRegistration.SurfaceSamplesPerChunkFaces(scanner, minSourceConstraintChunkFaces);
		public float MinOverlapSamples(EnvScanner scanner) => TsdfRegistration.SurfaceSamplesPerChunkFaces(scanner, minOverlapChunkFaces);
		public float MaxRefinedRms(EnvScanner scanner) => maxRefinedRmsVoxels * scanner.VoxSize;

		/// <summary>Whether the extracted source pins every degree of freedom firmly enough to attempt.</summary>
		public bool HasCoverage(SourceConstraint constraint, EnvScanner scanner) => constraint.Weakest >= MinSourceConstraint(scanner);

		/// <summary>The fit alone, before margin and refinement are weighed: whether a result is worth keeping over a wider search.</summary>
		public bool Fits(RegistrationResult result, EnvScanner scanner) =>
			result.success && result.contradiction <= maxContradiction && result.agreement >= minAgreement && result.overlapSamples >= MinOverlapSamples(scanner);

		/// <summary>Why <paramref name="result"/> can't be trusted, or null when it can.</summary>
		public string Rejection(RegistrationResult result, EnvScanner scanner) => Rejection(result, scanner, minMarginFraction);

		/// <summary>As <see cref="Rejection(RegistrationResult, EnvScanner)"/>, with its own margin.</summary>
		public string Rejection(RegistrationResult result, EnvScanner scanner, float marginFraction)
		{
			float minOverlap = MinOverlapSamples(scanner), maxRms = MaxRefinedRms(scanner);
			return !result.success ? result.failure :
				result.contradiction > maxContradiction ? $"contradiction {result.contradiction:F2} > {maxContradiction}" :
				result.agreement < minAgreement ? $"agreement {result.agreement:F2} < {minAgreement}" :
				result.overlapSamples < minOverlap ? $"overlap {result.overlapSamples} samples < {minOverlap:F0}" :
				result.margin < marginFraction * result.score ? $"margin {result.margin:F3} < {marginFraction:F2} × score {result.score:F3}" :
				result.refinedRms < 0 ? "refinement failed" :
				result.refinedRms > maxRms ? $"rms {result.refinedRms * 100:F1}cm > {maxRms * 100:F0}cm" : null;
		}
	}
}
