using AprilTag;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocationGraphics : MonoBehaviour
	{
		[SerializeField] private AprilTagColocator colocator;

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

		//private void OnEnable()
		//{
		//	ProceduralDrawFeature.Draw += RenderFoundTags;
		//}

		//private void OnDisable()
		//{
		//	ProceduralDrawFeature.Draw -= RenderFoundTags;
		//}

		private void OnTagsDetected(IReadOnlyList<TagPose> tagPoses)
		{
			latestTagPoses = tagPoses;
		}

		private void LateUpdate()
		{
			if (!colocator.ColocationActive)
				return;

			Vector3 scale;

			if (latestTagPoses != null)
			{
				scale = Vector3.one * colocator.tagSize * 3;
				var color = Color.white;

				foreach (var tagPose in latestTagPoses)
				{
					if (colocator.IsOwner)
					{
						var tagRegistered = colocator.CanonTags.ContainsKey(tagPose.ID);
						color = tagRegistered ? Color.green : Color.yellow;
					}

					mpb.SetColor(BaseColorID, color);

					var model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, scale);
					Graphics.DrawMesh(indicatorMesh, model, indicatorMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}

			if (Anaglyph.DebugMode)
			{
				scale = Vector3.one * 0.03f;
				mpb.SetColor(BaseColorID, Color.green);
				foreach (var canonTag in colocator.CanonTags.Values)
				{
					var model = Matrix4x4.TRS(canonTag.position, Quaternion.identity, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}

				scale = Vector3.one * 0.02f;
				mpb.SetColor(BaseColorID, Color.yellow);
				foreach (var localTagPos in colocator.LocalTags.Values)
				{
					var model = MainXRRig.TrackingSpace.localToWorldMatrix *
					            Matrix4x4.TRS(localTagPos, Quaternion.identity, scale);
					Graphics.DrawMesh(debugPointMesh, model, debugMaterial, 0, MainXRRig.Camera, 0, mpb);
				}
			}
		}

		private void RenderFoundTags(RasterCommandBuffer cmd)
		{
		}
	}
}