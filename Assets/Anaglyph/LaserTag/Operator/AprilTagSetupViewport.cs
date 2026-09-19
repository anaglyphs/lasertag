using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Operator
{
	public sealed class AprilTagSetupViewport : VisualElement
	{
		private Camera camera;
		private MapSpace space;
		private readonly Material tagIndicatorMaterial;
		private readonly IndicatorRenderer indicators = new();
		private readonly List<(int id, string text, Vector2 position)> labels = new();
		public AprilTagSetupViewport(Material tagIndicatorMaterial)
		{
			this.tagIndicatorMaterial = tagIndicatorMaterial;
			pickingMode = PickingMode.Ignore;
			AddToClassList("tag-setup-viewport");
			generateVisualContent += DrawLabels;
		}

		public void Refresh(Camera camera, MapSpace space)
		{
			this.camera = camera;
			this.space = space;
			DrawIndicators();
		}

		public void FocusTags()
		{
			if (!camera || space?.HasTags != true) return;
			var bounds = new Bounds(space.Frame.ToCanonical(space.tags[0].canonPose).position, Vector3.one);
			foreach (var tag in space.tags) bounds.Encapsulate(space.Frame.ToCanonical(tag.canonPose).position);
			float radius = bounds.extents.magnitude + space.tagSizeCm * .01f;
			float halfFov = Mathf.Atan(Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * Mathf.Min(1f, camera.aspect));
			float distance = radius / Mathf.Sin(halfFov) * 1.2f;
			var rotation = Quaternion.Euler(35, 35, 0);
			camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
			if (camera.orthographic) camera.orthographicSize = radius / Mathf.Min(1f, camera.aspect) * 1.2f;
		}

		private bool Project(Vector3 world, out Vector2 point)
		{
			Vector3 projected = camera.WorldToViewportPoint(world);
			point = new(projected.x * contentRect.width, (1 - projected.y) * contentRect.height);
			return projected.z > camera.nearClipPlane;
		}

		private void DrawIndicators()
		{
			int labelCount = 0;
			bool labelsChanged = false;
			if (camera && space != null && panel != null && visible && resolvedStyle.display != DisplayStyle.None &&
				contentRect.width > 0 && contentRect.height > 0)
			{
				Vector3 scale = Vector3.one * (space.tagSizeCm * .03f);
				Quaternion quadRotation = Quaternion.Euler(90, 0, 0);
				foreach (var tag in space.tags)
				{
					Pose pose = space.Frame.ToCanonical(tag.canonPose);
					Matrix4x4 transform = Matrix4x4.TRS(pose.position, pose.rotation * quadRotation, scale);
					indicators.DrawQuad(camera, tagIndicatorMaterial, transform, Color.cyan);
					if (!Project(pose.position, out var center)) continue;

					Vector2 labelPosition = center + new Vector2(8, -28);
					if (labelCount == labels.Count)
					{
						labels.Add((tag.id, "#" + tag.id, labelPosition));
						labelsChanged = true;
					}
					else if (labels[labelCount].id != tag.id || labels[labelCount].position != labelPosition)
					{
						string text = labels[labelCount].id == tag.id ? labels[labelCount].text : "#" + tag.id;
						labels[labelCount] = (tag.id, text, labelPosition);
						labelsChanged = true;
					}
					labelCount++;
				}
			}
			if (labels.Count > labelCount)
			{
				labels.RemoveRange(labelCount, labels.Count - labelCount);
				labelsChanged = true;
			}
			if (labelsChanged) MarkDirtyRepaint();
		}

		private void DrawLabels(MeshGenerationContext context)
		{
			foreach (var label in labels)
				context.DrawText(label.text, label.position, 28, Color.white);
		}
	}
}
