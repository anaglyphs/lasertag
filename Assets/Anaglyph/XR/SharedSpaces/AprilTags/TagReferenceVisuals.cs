using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.Rendering;
using AprilTag;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces.AprilTags
{
	public class TagReferenceVisuals : MonoBehaviour
	{
		[SerializeField] private AprilTagColocationConstraintProvider provider;

		[SerializeField] private Mesh indicatorMesh;
		[SerializeField] private Material indicatorMaterial;

		[SerializeField] private Mesh debugPointMesh;
		[SerializeField] private Material debugMaterial;

		private IndicatorRenderer indicators;

		/// <summary>
		/// The tag being aimed at, or -1. Written by whatever is authoring tags; kept here so the
		/// highlight travels with the indicator it belongs to rather than being a second overlay.
		/// </summary>
		public static int HighlightedTagId { get; set; } = -1;

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => HighlightedTagId = -1;

		private IReadOnlyList<TagPose> latestTagPoses;
		private readonly List<TaggedAnchorConstraintData> anchorScratch = new();

		private void Awake()
		{
			indicators = new IndicatorRenderer();
		}

		private void Start()
		{
			provider.TagTracker.OnDetectTags += OnTagsDetected;
		}

		private void OnTagsDetected(IReadOnlyList<TagPose> tagPoses)
		{
			latestTagPoses = tagPoses;
		}

		private void LateUpdate()
		{
			if (!provider.IsDetecting)
				return;

			Vector3 scale;

			if (latestTagPoses != null)
			{
				scale = Vector3.one * (provider.TagSizeCm * 0.03f);

				foreach (TagPose tagPose in latestTagPoses)
				{
					Color color = provider.RegisteredTags.ContainsKey(tagPose.ID)
						? Color.white
						: Color.yellow;

					if (tagPose.ID == HighlightedTagId)
						color = Color.green;

					Matrix4x4 model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, scale);
					indicators.DrawMesh(MainXRRig.Camera, indicatorMesh, indicatorMaterial, model, color);
				}
			}

			if (AnaglyphDebugging.DebugMode)
			{
				scale = Vector3.one * 0.02f;
				foreach (Pose canonTag in provider.RegisteredTags.Values)
				{
					Matrix4x4 model = Matrix4x4.TRS(canonTag.position, Quaternion.identity, scale);
					indicators.DrawMesh(MainXRRig.Camera, debugPointMesh, debugMaterial, model, Color.green);
				}

				anchorScratch.Clear();
				provider.GetLocalAnchorConstraints(anchorScratch);
				foreach (TaggedAnchorConstraintData anchor in anchorScratch)
				{
					Matrix4x4 model = Matrix4x4.TRS(
						anchor.canonPose.position, anchor.canonPose.rotation, scale);
					indicators.DrawMesh(MainXRRig.Camera, debugPointMesh, debugMaterial, model, Color.white);
				}
			}
		}
	}
}
