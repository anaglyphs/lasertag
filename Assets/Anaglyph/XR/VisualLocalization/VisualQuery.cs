using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>Features of one captured frame and where its camera was, in the capturing headset's world.</summary>
	public sealed class VisualQuery
	{
		public FeatureSet features;
		public PinholeCamera intrinsics;
		/// <summary>The camera in the capturing headset's world.</summary>
		public Pose camera;
		public Pose cameraInTrackingSpace;
		/// <summary>The tracking space's world pose at capture, which a correction found for this query applies to.</summary>
		public Pose trackingSpace;
		/// <summary>The pose-graph node the frame was captured under, and the camera relative to it; node 0 when not anchored.</summary>
		public ulong node;
		public Pose cameraInNode;
		public long timestampNs;
		/// <summary>Its <see cref="VisualFrameLog"/> record; 0 when not logged.</summary>
		public int frame;
	}

	public struct LocalizeOptions
	{
		/// <summary>Only keyframes within <see cref="nearDistance"/> of this camera are tried.</summary>
		public Pose? near;
		public float nearDistance;
		/// <summary>
		/// Keyframes whose node is within the trust window of this node are skipped: a loop
		/// closure is against views from another stretch of tracking only. 0 for none.
		/// </summary>
		public ulong untrustedFrom;
		/// <summary>
		/// With <see cref="untrustedFrom"/>, keyframes whose walk-only drift from that node
		/// (<see cref="IKeyframeAnchoring.WalkDrift"/>) is below this are skipped too: a
		/// correction step alone puts the views of a moment ago beyond the trust window, and
		/// matching them only measures the correction they were placed with. 0 for none.
		/// </summary>
		public float minLoopWalkDrift;
		/// <summary>Only <see cref="VisualKeyframe.locked"/> views are tried: a revisit's reference doesn't follow later laps.</summary>
		public bool lockedOnly;
		/// <summary>Keyframes facing more than this many degrees away from the query camera are skipped; 0 for any.</summary>
		public float maxViewAngle;
		/// <summary>
		/// How well the query camera's position in the map frame is known beforehand,
		/// meters; 0 for the solver's default (loose). A far landmark pins yaw only as well
		/// as the camera's position: a tight prior lets it.
		/// </summary>
		public float centerPriorSigma;
		/// <summary>
		/// Features per side LighterGlue attends over, strongest first; 0 for all. Its cost
		/// grows with the square of the count, so a background query halving it costs a quarter.
		/// </summary>
		public int maxMatchFeatures;
		/// <summary>Keyframes retrieval ranks below this many shared features are not worth a LighterGlue run; 0 for the retrieval floor.</summary>
		public int minMutualToMatch;
		/// <summary>
		/// Something waits on the answer (a joiner's query, served by the authority): retrieval
		/// and matching run at the urgent pace whatever the localizer's own state.
		/// </summary>
		public bool urgent;
		/// <summary>
		/// Match by projecting the landmarks of the views near <see cref="near"/> through it
		/// into the query image (<see cref="GuidedMatcher"/>) rather than with LighterGlue:
		/// for a query whose camera prior is good to within <see cref="guidedTranslation"/>
		/// and a few degrees. Needs <see cref="near"/>.
		/// </summary>
		public bool guided;
		/// <summary>Meters the prior's position may be off, widening each landmark's window by its depth; 0 for the localizer's default.</summary>
		public float guidedTranslation;
	}

	public struct VisualLocalizationResult
	{
		public GravityPnPResult pose;
		public int keyframesMatched;
		public int bestMutual;
		/// <summary>The node the matched landmarks are anchored to (those of other nodes too far from it in drift are left out).</summary>
		public ulong anchorNode;
		/// <summary>The map points of the ranged inliers (within 30 m), where the map has them: what the solve stood on.</summary>
		public Vector3[] inlierPoints;
		public string failure;

		public bool Success => pose.success;

		public override string ToString() => pose.success
			? $"{pose} keyframes={keyframesMatched} mutual={bestMutual}"
			: $"failed: {failure ?? pose.failure} keyframes={keyframesMatched} mutual={bestMutual} correspondences={pose.correspondences}";
	}
}
