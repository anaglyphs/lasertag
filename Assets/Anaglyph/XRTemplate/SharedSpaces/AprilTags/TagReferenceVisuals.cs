using AprilTag;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class TagReferenceVisuals : MonoBehaviour
	{
		[FormerlySerializedAs("source")] [SerializeField] private TagConstraintProvider provider;

		[SerializeField] private Mesh indicatorMesh;
		[SerializeField] private Material indicatorMaterial;

		[SerializeField] private Mesh debugPointMesh;
		[SerializeField] private Material debugMaterial;

		private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
		private MaterialPropertyBlock mpb;

		private IReadOnlyList<TagPose> latestTagPoses;
		private readonly List<TaggedAnchorConstraintData> anchorScratch = new();

		private void Awake()
		{
			mpb = new MaterialPropertyBlock();
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

					mpb.SetColor(BaseColorID, color);

					Matrix4x4 model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, scale);
					Graphics.DrawMesh(indicatorMesh, model, indicatorMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}

			if (AnaglyphDebugging.DebugMode)
			{
				scale = Vector3.one * 0.02f;
				mpb.SetColor(BaseColorID, Color.green);
				foreach (Pose canonTag in provider.RegisteredTags.Values)
				{
					Matrix4x4 model = Matrix4x4.TRS(canonTag.position, Quaternion.identity, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}

				mpb.SetColor(BaseColorID, Color.white);
				anchorScratch.Clear();
				provider.GetLocalAnchorConstraints(anchorScratch);
				foreach (TaggedAnchorConstraintData anchor in anchorScratch)
				{
					Matrix4x4 model = Matrix4x4.TRS(
						anchor.canonPose.position, anchor.canonPose.rotation, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}
		}
	}
}
