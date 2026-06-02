using AprilTag;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class TagColocatorVisuals : MonoBehaviour
	{
		[SerializeField] private TagColocator colocator;

		[SerializeField] private Mesh indicatorMesh;
		[SerializeField] private Material indicatorMaterial;

		[SerializeField] private Mesh debugPointMesh;
		[SerializeField] private Material debugMaterial;

		private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
		private MaterialPropertyBlock mpb;

		private IReadOnlyList<TagPose> latestTagPoses;

		private void Awake()
		{
			mpb = new MaterialPropertyBlock();
		}

		private void Start()
		{
			colocator.TagTracker.OnDetectTags += OnTagsDetected;
		}

		private void OnTagsDetected(IReadOnlyList<TagPose> tagPoses)
		{
			latestTagPoses = tagPoses;
		}

		private void LateUpdate()
		{
			if (!colocator.IsActive)
				return;

			Vector3 scale;

			if (latestTagPoses != null)
			{
				scale = Vector3.one * (colocator.TagSizeCm * 0.03f);
				Color color = Color.white;

				foreach (TagPose tagPose in latestTagPoses)
				{
					if (!colocator.CanonTags.ContainsKey(tagPose.ID))
						color = Color.yellow;

					mpb.SetColor(BaseColorID, color);

					Matrix4x4 model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, scale);
					Graphics.DrawMesh(indicatorMesh, model, indicatorMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}

			if (Debug.DebugMode)
			{
				scale = Vector3.one * 0.02f;
				mpb.SetColor(BaseColorID, Color.green);
				foreach (Pose canonTag in colocator.CanonTags.Values)
				{
					Matrix4x4 model = Matrix4x4.TRS(canonTag.position, Quaternion.identity, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}

				mpb.SetColor(BaseColorID, Color.white);
				foreach (Vector3 localTagPos in colocator.LocalTags.Values)
				{
					Matrix4x4 model = MainXRRig.TrackingSpace.localToWorldMatrix *
					                  Matrix4x4.TRS(localTagPos, Quaternion.identity, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}
		}
	}
}