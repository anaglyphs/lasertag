using Anaglyph.LaserTag.Maps;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Operator
{
	public sealed class AprilTagSetupViewport : VisualElement
	{
		private Camera camera;
		private MapSpace space;
		private readonly Vector3[] corners = new Vector3[4];
		public AprilTagSetupViewport()
		{
			pickingMode = PickingMode.Ignore;
			AddToClassList("tag-setup-viewport");
			generateVisualContent += Draw;
		}

		public void Refresh(Camera camera, MapSpace space)
		{
			this.camera = camera;
			this.space = space;
			MarkDirtyRepaint();
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

		private void Draw(MeshGenerationContext context)
		{
			if (!camera || space == null || contentRect.width <= 0 || contentRect.height <= 0) return;
			var painter = context.painter2D;
			painter.lineWidth = 2;
			float halfSize = space.tagSizeCm * .005f;
			foreach (var tag in space.tags)
			{
				Pose pose = space.Frame.ToCanonical(tag.canonPose);
				Vector3 right = pose.rotation * Vector3.right * halfSize;
				Vector3 up = pose.rotation * Vector3.up * halfSize;
				corners[0] = pose.position - right - up;
				corners[1] = pose.position + right - up;
				corners[2] = pose.position + right + up;
				corners[3] = pose.position - right + up;
				bool visible = true;
				for (int i = 0; i < corners.Length; i++) visible &= Project(corners[i], out _);
				if (!visible || !Project(pose.position, out var center)) continue;
				painter.strokeColor = Color.cyan;
				painter.BeginPath();
				Project(corners[0], out var first);
				painter.MoveTo(first);
				for (int i = 1; i < corners.Length; i++) { Project(corners[i], out var point); painter.LineTo(point); }
				painter.ClosePath();
				painter.Stroke();
				if (Project(pose.position + pose.rotation * Vector3.forward * Mathf.Max(.15f, halfSize), out var normal))
				{
					painter.strokeColor = Color.yellow;
					painter.BeginPath(); painter.MoveTo(center); painter.LineTo(normal); painter.Stroke();
				}
				context.DrawText("#" + tag.id, center + new Vector2(8, -28), 28, Color.white);
			}
		}
	}
}
