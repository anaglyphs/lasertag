using Anaglyph.LaserTag.MapEditor.Tools;
using NUnit.Framework;
using System;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Object = UnityEngine.Object;
using UnityEngine;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Tests
{
	public class TagSizeRulerTests
	{
		[Test] // AprilTag without registered tags: two-tag mode, where size stays editable
		public void MeasurementSurvivesMapChangesButStopsWhenTheSpaceFrameChanges()
		{
			var owner = new GameObject("Measurement state test");
			owner.SetActive(false);
			Assert.That(SpaceManager.Instance, Is.Null);
			bool wasEditing = MapEditor.MapEditor.IsActive;
			SpaceManagerTestContext context = null;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			try
			{
				var manager = owner.AddComponent<SpaceManager>();
				var space = Space.Create("Test"); space.alignment.method = ColocationManager.ColocationMethod.AprilTag;
				space.maps.Add(Map.Create("Map 1"));
				context = new SpaceManagerTestContext(manager, owner.AddComponent<ColocationManager>(), space);
				context.ShowMap(space.maps[0]);
				typeof(MapEditor.MapEditor).GetProperty("IsActive").SetValue(null, true);
				var tool = owner.AddComponent<MapEditorTool>();
				typeof(MapEditorTool).GetField("measurementSpaceContext", flags).SetValue(tool, manager.Frame.frameId.ToString());
				var allowed = typeof(MapEditorTool).GetProperty("MeasurementAllowed", flags);
				Assert.That(allowed.GetValue(tool), Is.True);
				space.alignment.tagSizeCm = 12;
				Assert.That(allowed.GetValue(tool), Is.True);
				space.maps.Add(Map.Create("Map 2"));
				context.ShowMap(space.maps[1]);
				Assert.That(allowed.GetValue(tool), Is.True, "a map change keeps the frame");
				space.frameId = Space.NewId();
				Assert.That(allowed.GetValue(tool), Is.False, "a rebased frame discards the measurement");
				context.ShowSpace(null);
				Assert.That(allowed.GetValue(tool), Is.False);
			}
			finally
			{
				typeof(MapEditor.MapEditor).GetProperty("IsActive").SetValue(null, wasEditing);
				context?.Dispose();
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void MeasurementUsesCentimetersAndSurvivesRigAlignment()
		{
			var tracking = new GameObject("Ruler test tracking space");
			try
			{
				using var ruler = new TagSizeRuler(tracking.transform, 0);
				ruler.SetStart(new Vector3(0.2f, 1f, 0.5f));
				Vector3 localEnd = new Vector3(0.23f, 1.04f, 0.5f);
				Assert.That(ruler.LengthCm(localEnd), Is.EqualTo(5f).Within(0.001f));
				tracking.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(0f, 75f, 0f));
				Assert.That(ruler.LengthCm(tracking.transform.TransformPoint(localEnd)), Is.EqualTo(5f).Within(0.001f));
				ruler.Reset();
				Assert.That(ruler.HasStart, Is.False);
				ruler.SetStart(tracking.transform.TransformPoint(localEnd));
				Assert.That(ruler.LengthCm(tracking.transform.TransformPoint(localEnd)), Is.Zero.Within(0.001f));
			}
			finally { Object.DestroyImmediate(tracking); }
		}

		[TestCase(1f, 0f, 0f)]
		[TestCase(-1f, 0f, 0f)]
		[TestCase(0f, 1f, 0f)]
		[TestCase(0f, -1f, 0f)]
		[TestCase(1f, 1f, 0.5f)]
		public void LabelStaysParallelAndFacesViewer(float x, float y, float z)
		{
			Vector3 edge = new Vector3(x, y, z);
			Vector3 viewer = new Vector3(0f, 0f, -1f);
			Assert.That(TagSizeRuler.TryGetLabelRotation(edge, Vector3.zero, viewer,
				Vector3.right, Vector3.up, out Quaternion rotation), Is.True);
			Assert.That(Mathf.Abs(Vector3.Dot(rotation * Vector3.right, edge.normalized)), Is.GreaterThan(0.999f));
			Assert.That(Vector3.Dot(rotation * Vector3.forward, -viewer), Is.GreaterThan(0f));
			Assert.That(Vector3.Dot(rotation * Vector3.right, x == 0f ? Vector3.up : Vector3.right), Is.GreaterThan(0f));
		}

		[TestCase(0f, 0f, 0f)]
		[TestCase(0f, 0f, 1f)]
		public void DegenerateOrEndOnEdgesHideLabel(float x, float y, float z)
		{
			Assert.That(TagSizeRuler.TryGetLabelRotation(new Vector3(x, y, z), Vector3.zero,
				Vector3.back, Vector3.right, Vector3.up, out _), Is.False);
		}
	}
}
