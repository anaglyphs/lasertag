using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.Lasertag.Editor
{
	[EditorTool("Edit Multi-Line Path", typeof(MultiLineGraphic))]
	public sealed class MultiLineGraphicTool : EditorTool
	{
		private const string GridSizeKey = "Anaglyph.MultiLineGraphicTool.GridSize";
		private const string ClipboardPrefix = "Anaglyph.MultiLineGraphic.Segments:";
		private const float PointPickRadius = 9f;
		private const float SegmentPickRadius = 8f;
		private const float PanelWidth = 286f;

		private static readonly Color GridColor = new(0.45f, 0.7f, 0.9f, 0.13f);
		private static readonly Color GridAxisColor = new(0.45f, 0.8f, 1f, 0.38f);
		private static readonly Color SelectedColor = new(1f, 0.58f, 0.12f, 1f);
		private static readonly Color ActiveColor = new(0.2f, 0.85f, 1f, 1f);
		private static readonly BoxBoundsHandle BoundsHandle = new()
		{
			axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y,
			handleColor = SelectedColor,
			wireframeColor = new Color(1f, 0.58f, 0.12f, 0.8f),
		};

		private readonly HashSet<PointId> selectedPoints = new();
		private HashSet<PointId> selectionBeforeBox = new();

		private int activeSegment = -1;
		private float gridSize = 10f;
		private bool showHelp;
		private bool boxSelecting;
		private Vector2 boxStart;
		private Vector2 boxEnd;
		private Rect panelRect;

		[Serializable]
		private sealed class SegmentClipboard
		{
			public Segment[] segments;
		}

		private readonly struct PointId : IEquatable<PointId>
		{
			public readonly int segment;
			public readonly int point;

			public PointId(int segment, int point)
			{
				this.segment = segment;
				this.point = point;
			}

			public bool Equals(PointId other) => segment == other.segment && point == other.point;
			public override bool Equals(object obj) => obj is PointId other && Equals(other);
			public override int GetHashCode() => HashCode.Combine(segment, point);
		}

		private readonly struct EdgeHit
		{
			public readonly int segment;
			public readonly int edgeStart;
			public readonly float distance;

			public EdgeHit(int segment, int edgeStart, float distance)
			{
				this.segment = segment;
				this.edgeStart = edgeStart;
				this.distance = distance;
			}

			public bool isValid => segment >= 0;
		}

		public override GUIContent toolbarIcon
		{
			get
			{
				GUIContent builtIn = EditorGUIUtility.IconContent("EditCollider");
				return new GUIContent(builtIn.image, "Edit Multi-Line Path");
			}
		}

		public override void OnActivated()
		{
			gridSize = Mathf.Max(0.0001f, EditorPrefs.GetFloat(GridSizeKey, 10f));
			panelRect = GetPanelRect();
			Undo.undoRedoPerformed += OnUndoRedo;
			EnsureActiveSegment();
			SceneView.RepaintAll();
		}

		public override void OnWillBeDeactivated()
		{
			Undo.undoRedoPerformed -= OnUndoRedo;
			selectedPoints.Clear();
			if (boxSelecting)
				GUIUtility.hotControl = 0;
			boxSelecting = false;
			SceneView.RepaintAll();
		}

		private void OnDestroy()
		{
			Undo.undoRedoPerformed -= OnUndoRedo;
		}

		public override void OnToolGUI(EditorWindow window)
		{
			if (window is not SceneView sceneView || target is not MultiLineGraphic graphic)
				return;

			SanitizeSelection(graphic);
			EnsureActiveSegment();
			panelRect = GetPanelRect();

			Event evt = Event.current;
			int sceneControl = GUIUtility.GetControlID(FocusType.Passive);
			if (evt.type == EventType.Layout && !evt.alt && !IsPointerOverPanel(evt.mousePosition))
				HandleUtility.AddDefaultControl(sceneControl);

			DrawGrid(graphic);
			DrawSegments(graphic);
			DrawSelectionTransform(graphic);
			DrawPointHandles(graphic);
			DrawCreationPreview(graphic);
			HandleKeyboard(graphic, sceneView);
			HandlePointer(graphic, sceneControl);
			DrawBoxSelection();
			DrawToolPanel(graphic, sceneView);
		}

		private void OnUndoRedo()
		{
			if (target is MultiLineGraphic graphic)
				SanitizeSelection(graphic);
			SceneView.RepaintAll();
		}

		private void DrawGrid(MultiLineGraphic graphic)
		{
			if (gridSize <= 0f || Event.current.type != EventType.Repaint)
				return;

			Rect graphicRect = graphic.rectTransform.rect;
			Rect contentRect = GetContentRect(graphic, gridSize * 10f);
			Rect range = Rect.MinMaxRect(
				Mathf.Min(graphicRect.xMin, contentRect.xMin),
				Mathf.Min(graphicRect.yMin, contentRect.yMin),
				Mathf.Max(graphicRect.xMax, contentRect.xMax),
				Mathf.Max(graphicRect.yMax, contentRect.yMax));

			float visibleStep = gridSize;
			float lineCount = range.width / visibleStep + range.height / visibleStep;
			if (lineCount > 240f)
				visibleStep *= Mathf.Ceil(lineCount / 240f);

			float minX = Mathf.Floor(range.xMin / visibleStep) * visibleStep;
			float maxX = Mathf.Ceil(range.xMax / visibleStep) * visibleStep;
			float minY = Mathf.Floor(range.yMin / visibleStep) * visibleStep;
			float maxY = Mathf.Ceil(range.yMax / visibleStep) * visibleStep;

			CompareFunction oldZTest = Handles.zTest;
			using (new Handles.DrawingScope(GridColor, graphic.transform.localToWorldMatrix))
			{
				Handles.zTest = CompareFunction.Always;
				for (float x = minX; x <= maxX + visibleStep * 0.1f; x += visibleStep)
					Handles.DrawLine(new Vector3(x, minY), new Vector3(x, maxY));
				for (float y = minY; y <= maxY + visibleStep * 0.1f; y += visibleStep)
					Handles.DrawLine(new Vector3(minX, y), new Vector3(maxX, y));
			}

			using (new Handles.DrawingScope(GridAxisColor, graphic.transform.localToWorldMatrix))
			{
				Handles.DrawLine(new Vector3(0f, minY), new Vector3(0f, maxY));
				Handles.DrawLine(new Vector3(minX, 0f), new Vector3(maxX, 0f));
			}
			Handles.zTest = oldZTest;
		}

		private static Rect GetContentRect(MultiLineGraphic graphic, float fallbackSize)
		{
			bool hasPoint = false;
			Vector2 min = Vector2.zero;
			Vector2 max = Vector2.zero;
			foreach (Segment segment in graphic.segments ?? Array.Empty<Segment>())
			{
				foreach (Vector2 point in segment.points ?? Array.Empty<Vector2>())
				{
					if (!hasPoint)
					{
						min = max = point;
						hasPoint = true;
					}
					else
					{
						min = Vector2.Min(min, point);
						max = Vector2.Max(max, point);
					}
				}
			}

			if (!hasPoint)
				return new Rect(-fallbackSize * 0.5f, -fallbackSize * 0.5f, fallbackSize, fallbackSize);

			Vector2 padding = Vector2.one * fallbackSize * 0.15f;
			min -= padding;
			max += padding;
			return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
		}

		private void DrawSegments(MultiLineGraphic graphic)
		{
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
			{
				Segment segment = segments[segmentIndex];
				Vector2[] points = segment.points ?? Array.Empty<Vector2>();
				if (points.Length < 2)
					continue;

				Vector3[] worldPoints = new Vector3[points.Length + (segment.closed ? 1 : 0)];
				for (int i = 0; i < points.Length; i++)
					worldPoints[i] = graphic.transform.TransformPoint(points[i]);
				if (segment.closed)
					worldPoints[^1] = worldPoints[0];

				Color color = segment.color;
				color.a = Mathf.Max(color.a, 0.55f);
				if (segmentIndex == activeSegment)
					color = Color.Lerp(color, ActiveColor, 0.35f);

				using (new Handles.DrawingScope(color))
					Handles.DrawAAPolyLine(segmentIndex == activeSegment ? 4f : 2.5f, worldPoints);
			}
		}

		private void DrawPointHandles(MultiLineGraphic graphic)
		{
			bool creationModifier = HasCreationModifier(Event.current);
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
			{
				Vector2[] points = segments[segmentIndex].points ?? Array.Empty<Vector2>();
				for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
				{
					PointId id = new(segmentIndex, pointIndex);
					Vector3 world = graphic.transform.TransformPoint(points[pointIndex]);
					float size = HandleUtility.GetHandleSize(world) * 0.065f;
					Color color = selectedPoints.Contains(id)
						? SelectedColor
						: segmentIndex == activeSegment ? ActiveColor : Color.white;

					using (new Handles.DrawingScope(color))
					{
						if (creationModifier && Event.current.type == EventType.Repaint)
							Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
						else if (Handles.Button(world, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
							SelectPoint(id, Event.current.shift);
					}
				}
			}
		}

		private void DrawSelectionTransform(MultiLineGraphic graphic)
		{
			if (selectedPoints.Count == 0)
				return;

			GetSelectionBounds(graphic, out Vector2 min, out Vector2 max);
			Vector2 center = (min + max) * 0.5f;
			Vector3 centerWorld = graphic.transform.TransformPoint(center);
			float localHandleSize = HandleUtility.GetHandleSize(centerWorld) * 0.11f /
				Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(graphic.transform.lossyScale.x), Mathf.Abs(graphic.transform.lossyScale.y)));

			using (new Handles.DrawingScope(SelectedColor, graphic.transform.localToWorldMatrix))
			{
				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.Slider2D(
					center,
					Vector3.forward,
					Vector3.right,
					Vector3.up,
					localHandleSize,
					Handles.RectangleHandleCap,
					new Vector2(gridSize, gridSize),
					true);
				if (EditorGUI.EndChangeCheck())
				{
					MoveSelection(graphic, Snap((Vector2)moved) - center);
					return;
				}

				if (selectedPoints.Count < 2)
					return;

				Vector2 actualSize = max - min;
				Vector2 displaySize = new(
					Mathf.Max(actualSize.x, gridSize),
					Mathf.Max(actualSize.y, gridSize));
				BoundsHandle.center = center;
				BoundsHandle.size = new Vector3(displaySize.x, displaySize.y, 0f);

				EditorGUI.BeginChangeCheck();
				BoundsHandle.DrawHandle();
				if (EditorGUI.EndChangeCheck())
				{
					Vector2 rawCenter = BoundsHandle.center;
					Vector2 rawSize = BoundsHandle.size;
					Vector2 a = Snap(rawCenter - rawSize * 0.5f);
					Vector2 b = Snap(rawCenter + rawSize * 0.5f);
					ScaleSelection(graphic, min, max, Vector2.Min(a, b), Vector2.Max(a, b));
				}
			}
		}

		private void DrawCreationPreview(MultiLineGraphic graphic)
		{
			Event evt = Event.current;
			if (evt.type != EventType.Repaint || !HasCreationModifier(evt) || evt.alt || IsPointerOverPanel(evt.mousePosition) ||
				!TryGetMouseLocal(graphic, evt.mousePosition, out Vector2 mouseLocal))
				return;

			Vector2 snapped = Snap(mouseLocal);
			PointId existingPoint = FindPoint(graphic, evt.mousePosition, PointPickRadius);
			EdgeHit edge = FindEdge(graphic, evt.mousePosition, SegmentPickRadius + 4f);
			Vector3 world = graphic.transform.TransformPoint(snapped);
			float size = HandleUtility.GetHandleSize(world) * 0.08f;

			using (new Handles.DrawingScope(existingPoint.segment >= 0 ? SelectedColor : ActiveColor))
			{
				Handles.SphereHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
				if (existingPoint.segment >= 0)
					return;

				if (edge.isValid)
				{
					Vector2[] points = graphic.segments[edge.segment].points;
					int next = (edge.edgeStart + 1) % points.Length;
					Handles.DrawDottedLine(graphic.transform.TransformPoint(points[edge.edgeStart]), world, 4f);
					Handles.DrawDottedLine(world, graphic.transform.TransformPoint(points[next]), 4f);
				}
				else if (activeSegment >= 0 && activeSegment < (graphic.segments?.Length ?? 0))
				{
					Segment segment = graphic.segments[activeSegment];
					Vector2[] points = segment.points ?? Array.Empty<Vector2>();
					if (points.Length > 0)
						Handles.DrawDottedLine(graphic.transform.TransformPoint(points[^1]), world, 4f);
					if (segment.closed && points.Length > 1)
						Handles.DrawDottedLine(world, graphic.transform.TransformPoint(points[0]), 4f);
				}
			}
		}

		private void HandleKeyboard(MultiLineGraphic graphic, SceneView sceneView)
		{
			Event evt = Event.current;
			if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
				return;

			bool action = evt.control || evt.command;
			if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && selectedPoints.Count > 0)
			{
				DeleteSelectedPoints(graphic);
				evt.Use();
			}
			else if (action && evt.keyCode == KeyCode.D)
			{
				InsertMidpoint(graphic, sceneView);
				evt.Use();
			}
			else if (action && evt.keyCode == KeyCode.C)
			{
				CopySelectedSegments(graphic, sceneView);
				evt.Use();
			}
			else if (action && evt.keyCode == KeyCode.V)
			{
				PasteSegments(graphic, sceneView);
				evt.Use();
			}
			else if (action && evt.keyCode == KeyCode.A)
			{
				SelectAll(graphic);
				evt.Use();
			}
			else if (evt.keyCode == KeyCode.Escape)
			{
				if (selectedPoints.Count > 0 || boxSelecting)
				{
					selectedPoints.Clear();
					if (boxSelecting)
						GUIUtility.hotControl = 0;
					boxSelecting = false;
				}
				else
				{
					ToolManager.RestorePreviousTool();
				}
				evt.Use();
			}
		}

		private void HandlePointer(MultiLineGraphic graphic, int sceneControl)
		{
			Event evt = Event.current;
			if (!boxSelecting && (evt.alt || IsPointerOverPanel(evt.mousePosition)))
				return;

			if (evt.type == EventType.MouseDown && evt.button == 0 && HasCreationModifier(evt))
			{
				CreatePointAtMouse(graphic, evt.mousePosition);
				evt.Use();
				return;
			}

			if (evt.type == EventType.MouseDown && evt.button == 0 && !HasCreationModifier(evt))
			{
				EdgeHit edge = FindEdge(graphic, evt.mousePosition, SegmentPickRadius);
				if (edge.isValid)
				{
					SelectSegment(graphic, edge.segment, evt.shift);
					evt.Use();
					return;
				}

				boxSelecting = true;
				boxStart = boxEnd = evt.mousePosition;
				selectionBeforeBox = evt.shift ? new HashSet<PointId>(selectedPoints) : new HashSet<PointId>();
				GUIUtility.hotControl = sceneControl;
				evt.Use();
			}
			else if (boxSelecting && evt.type == EventType.MouseDrag && evt.button == 0)
			{
				boxEnd = evt.mousePosition;
				UpdateBoxSelection(graphic);
				evt.Use();
			}
			else if (boxSelecting && evt.type == EventType.MouseUp && evt.button == 0)
			{
				boxEnd = evt.mousePosition;
				if ((boxEnd - boxStart).sqrMagnitude < 9f)
				{
					selectedPoints.Clear();
					selectedPoints.UnionWith(selectionBeforeBox);
				}
				else
					UpdateBoxSelection(graphic);
				boxSelecting = false;
				GUIUtility.hotControl = 0;
				evt.Use();
			}
		}

		private void DrawBoxSelection()
		{
			if (!boxSelecting || Event.current.type != EventType.Repaint)
				return;

			Rect rect = GUIRect(boxStart, boxEnd);
			Handles.BeginGUI();
			EditorGUI.DrawRect(rect, new Color(0.2f, 0.65f, 1f, 0.12f));
			EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), ActiveColor);
			EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), ActiveColor);
			EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), ActiveColor);
			EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), ActiveColor);
			Handles.EndGUI();
		}

		private void DrawToolPanel(MultiLineGraphic graphic, SceneView sceneView)
		{
			panelRect = GetPanelRect();

			Handles.BeginGUI();
			GUILayout.BeginArea(panelRect, EditorStyles.helpBox);
			GUILayout.Label("Multi-Line Path", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			float nextGridSize = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Grid Size", gridSize));
			if (EditorGUI.EndChangeCheck())
			{
				gridSize = nextGridSize;
				EditorPrefs.SetFloat(GridSizeKey, gridSize);
				sceneView.Repaint();
			}

			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			if (segments.Length > 0)
			{
				string[] labels = new string[segments.Length];
				for (int i = 0; i < labels.Length; i++)
					labels[i] = $"Segment {i + 1} ({segments[i].points?.Length ?? 0})";
				activeSegment = EditorGUILayout.Popup("Active", Mathf.Clamp(activeSegment, 0, segments.Length - 1), labels);
			}

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("New Segment"))
				AddSegment(graphic);
			using (new EditorGUI.DisabledScope(activeSegment < 0))
				if (GUILayout.Button("Delete Active"))
					DeleteActiveSegment(graphic);
			GUILayout.EndHorizontal();

			segments = graphic.segments ?? Array.Empty<Segment>();
			if (activeSegment >= 0 && activeSegment < segments.Length)
			{
				Segment segment = segments[activeSegment];
				EditorGUI.BeginChangeCheck();
				float thickness = Mathf.Max(0f, EditorGUILayout.FloatField("Thickness", segment.thickness));
				Color color = EditorGUILayout.ColorField("Color", segment.color);
				bool closed = EditorGUILayout.Toggle("Closed", segment.closed);
				if (EditorGUI.EndChangeCheck())
				{
					Record(graphic, "Edit Line Segment");
					segment.thickness = thickness;
					segment.color = color;
					segment.closed = closed;
					segments[activeSegment] = segment;
					graphic.segments = segments;
					Changed(graphic);
				}
			}

			GUILayout.Label($"{selectedPoints.Count} point{(selectedPoints.Count == 1 ? string.Empty : "s")} selected", EditorStyles.miniLabel);
			showHelp = EditorGUILayout.Foldout(showHelp, "Controls", true);
			if (showHelp)
			{
				GUILayout.Label(
					"Ctrl/Command-click: add or insert\n" +
					"Click line: select its segment\n" +
					"Shift/box: add to point selection\n" +
					"Ctrl/Command-D: insert midpoint\n" +
					"Ctrl/Command-C/V: copy/paste segments\n" +
					"Delete/Backspace: delete points\n" +
					"Escape: clear selection / leave tool",
					EditorStyles.wordWrappedMiniLabel);
			}
			GUILayout.EndArea();
			Handles.EndGUI();
		}

		private void SelectPoint(PointId id, bool additive)
		{
			if (!additive)
				selectedPoints.Clear();
			if (additive && !selectedPoints.Add(id))
				selectedPoints.Remove(id);
			else
				selectedPoints.Add(id);
			activeSegment = id.segment;
			SceneView.RepaintAll();
		}

		private void SelectSegment(MultiLineGraphic graphic, int segmentIndex, bool additive)
		{
			if (!additive)
				selectedPoints.Clear();

			Vector2[] points = graphic.segments[segmentIndex].points ?? Array.Empty<Vector2>();
			bool allSelected = points.Length > 0;
			for (int i = 0; i < points.Length; i++)
				allSelected &= selectedPoints.Contains(new PointId(segmentIndex, i));

			for (int i = 0; i < points.Length; i++)
			{
				PointId id = new(segmentIndex, i);
				if (additive && allSelected)
					selectedPoints.Remove(id);
				else
					selectedPoints.Add(id);
			}
			activeSegment = segmentIndex;
			SceneView.RepaintAll();
		}

		private void SelectAll(MultiLineGraphic graphic)
		{
			selectedPoints.Clear();
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int s = 0; s < segments.Length; s++)
				for (int p = 0; p < (segments[s].points?.Length ?? 0); p++)
					selectedPoints.Add(new PointId(s, p));
			SceneView.RepaintAll();
		}

		private void UpdateBoxSelection(MultiLineGraphic graphic)
		{
			Rect rect = GUIRect(boxStart, boxEnd);
			selectedPoints.Clear();
			selectedPoints.UnionWith(selectionBeforeBox);

			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int s = 0; s < segments.Length; s++)
			{
				Vector2[] points = segments[s].points ?? Array.Empty<Vector2>();
				for (int p = 0; p < points.Length; p++)
				{
					Vector2 gui = HandleUtility.WorldToGUIPoint(graphic.transform.TransformPoint(points[p]));
					if (rect.Contains(gui))
						selectedPoints.Add(new PointId(s, p));
				}
			}
			SceneView.RepaintAll();
		}

		private void CreatePointAtMouse(MultiLineGraphic graphic, Vector2 mousePosition)
		{
			if (!TryGetMouseLocal(graphic, mousePosition, out Vector2 mouseLocal))
				return;

			PointId existing = FindPoint(graphic, mousePosition, PointPickRadius);
			if (existing.segment >= 0)
			{
				SelectPoint(existing, false);
				return;
			}

			Vector2 point = Snap(mouseLocal);
			EdgeHit edge = FindEdge(graphic, mousePosition, SegmentPickRadius + 4f);
			if (edge.isValid)
			{
				InsertPoint(graphic, edge.segment, edge.edgeStart + 1, point, "Insert Line Point");
				return;
			}

			if (activeSegment < 0)
				AddSegment(graphic);
			if (activeSegment >= 0)
				InsertPoint(graphic, activeSegment, graphic.segments[activeSegment].points?.Length ?? 0, point, "Add Line Point");
		}

		private void InsertPoint(MultiLineGraphic graphic, int segmentIndex, int insertIndex, Vector2 point, string undoName)
		{
			Record(graphic, undoName);
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			Segment segment = segments[segmentIndex];
			List<Vector2> points = new(segment.points ?? Array.Empty<Vector2>());
			insertIndex = Mathf.Clamp(insertIndex, 0, points.Count);
			points.Insert(insertIndex, point);
			segment.points = points.ToArray();
			segments[segmentIndex] = segment;
			graphic.segments = segments;
			selectedPoints.Clear();
			selectedPoints.Add(new PointId(segmentIndex, insertIndex));
			activeSegment = segmentIndex;
			Changed(graphic);
		}

		private void InsertMidpoint(MultiLineGraphic graphic, SceneView sceneView)
		{
			if (selectedPoints.Count != 2)
			{
				Notify(sceneView, "Select two adjacent points to insert a midpoint.");
				return;
			}

			PointId[] ids = new PointId[2];
			selectedPoints.CopyTo(ids);
			if (ids[0].segment != ids[1].segment)
			{
				Notify(sceneView, "The two points must belong to the same segment.");
				return;
			}

			int segmentIndex = ids[0].segment;
			Segment segment = graphic.segments[segmentIndex];
			Vector2[] points = segment.points ?? Array.Empty<Vector2>();
			int low = Mathf.Min(ids[0].point, ids[1].point);
			int high = Mathf.Max(ids[0].point, ids[1].point);
			bool closingEdge = segment.closed && low == 0 && high == points.Length - 1;
			if (high != low + 1 && !closingEdge)
			{
				Notify(sceneView, "Select two adjacent points to insert a midpoint.");
				return;
			}

			Vector2 midpoint = Snap((points[low] + points[high]) * 0.5f);
			InsertPoint(graphic, segmentIndex, closingEdge ? points.Length : high, midpoint, "Insert Line Midpoint");
		}

		private void DeleteSelectedPoints(MultiLineGraphic graphic)
		{
			Record(graphic, selectedPoints.Count == 1 ? "Delete Line Point" : "Delete Line Points");
			Segment[] source = graphic.segments ?? Array.Empty<Segment>();
			List<Segment> result = new(source.Length);
			int newActive = -1;

			for (int s = 0; s < source.Length; s++)
			{
				Segment segment = source[s];
				Vector2[] sourcePoints = segment.points ?? Array.Empty<Vector2>();
				List<Vector2> kept = new(sourcePoints.Length);
				for (int p = 0; p < sourcePoints.Length; p++)
					if (!selectedPoints.Contains(new PointId(s, p)))
						kept.Add(sourcePoints[p]);

				if (sourcePoints.Length > 0 && kept.Count == 0)
					continue;

				if (s == activeSegment)
					newActive = result.Count;
				segment.points = kept.ToArray();
				result.Add(segment);
			}

			graphic.segments = result.ToArray();
			selectedPoints.Clear();
			activeSegment = newActive >= 0 ? newActive : Mathf.Min(activeSegment, result.Count - 1);
			Changed(graphic);
		}

		private void AddSegment(MultiLineGraphic graphic)
		{
			Record(graphic, "Add Line Segment");
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			Segment template = activeSegment >= 0 && activeSegment < segments.Length
				? segments[activeSegment]
				: new Segment { thickness = 1f, color = Color.white };
			template.points = Array.Empty<Vector2>();
			template.closed = false;
			template.overrideFirstAngle = false;
			template.overrideLastAngle = false;

			Array.Resize(ref segments, segments.Length + 1);
			segments[^1] = template;
			graphic.segments = segments;
			activeSegment = segments.Length - 1;
			selectedPoints.Clear();
			Changed(graphic);
		}

		private void DeleteActiveSegment(MultiLineGraphic graphic)
		{
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			if (activeSegment < 0 || activeSegment >= segments.Length)
				return;

			Record(graphic, "Delete Line Segment");
			List<Segment> result = new(segments);
			result.RemoveAt(activeSegment);
			graphic.segments = result.ToArray();
			activeSegment = Mathf.Min(activeSegment, result.Count - 1);
			selectedPoints.Clear();
			Changed(graphic);
		}

		private void CopySelectedSegments(MultiLineGraphic graphic, SceneView sceneView)
		{
			List<int> segmentIndices = GetSelectedSegmentIndices();
			if (segmentIndices.Count == 0)
			{
				Notify(sceneView, "Select a point or line before copying segments.");
				return;
			}

			Segment[] copied = new Segment[segmentIndices.Count];
			for (int i = 0; i < copied.Length; i++)
				copied[i] = graphic.segments[segmentIndices[i]];
			EditorGUIUtility.systemCopyBuffer = ClipboardPrefix + JsonUtility.ToJson(new SegmentClipboard { segments = copied });
			Notify(sceneView, $"Copied {copied.Length} segment{(copied.Length == 1 ? string.Empty : "s")}.");
		}

		private void PasteSegments(MultiLineGraphic graphic, SceneView sceneView)
		{
			string buffer = EditorGUIUtility.systemCopyBuffer;
			if (string.IsNullOrEmpty(buffer) || !buffer.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
			{
				Notify(sceneView, "The clipboard does not contain MultiLineGraphic segments.");
				return;
			}

			SegmentClipboard clipboard;
			try
			{
				clipboard = JsonUtility.FromJson<SegmentClipboard>(buffer[ClipboardPrefix.Length..]);
			}
			catch (ArgumentException)
			{
				Notify(sceneView, "The copied segment data is invalid.");
				return;
			}

			if (clipboard?.segments == null || clipboard.segments.Length == 0)
				return;

			Record(graphic, "Paste Line Segments");
			Segment[] existing = graphic.segments ?? Array.Empty<Segment>();
			int firstNew = existing.Length;
			Array.Resize(ref existing, existing.Length + clipboard.segments.Length);
			Vector2 offset = Vector2.one * gridSize;
			for (int i = 0; i < clipboard.segments.Length; i++)
			{
				Segment segment = clipboard.segments[i];
				Vector2[] sourcePoints = segment.points ?? Array.Empty<Vector2>();
				segment.points = new Vector2[sourcePoints.Length];
				for (int p = 0; p < sourcePoints.Length; p++)
					segment.points[p] = Snap(sourcePoints[p] + offset);
				existing[firstNew + i] = segment;
			}

			graphic.segments = existing;
			selectedPoints.Clear();
			for (int s = firstNew; s < existing.Length; s++)
				for (int p = 0; p < (existing[s].points?.Length ?? 0); p++)
					selectedPoints.Add(new PointId(s, p));
			activeSegment = firstNew;
			Changed(graphic);
		}

		private List<int> GetSelectedSegmentIndices()
		{
			HashSet<int> unique = new();
			foreach (PointId id in selectedPoints)
				unique.Add(id.segment);
			List<int> result = new(unique);
			result.Sort();
			return result;
		}

		private void MoveSelection(MultiLineGraphic graphic, Vector2 delta)
		{
			if (delta == Vector2.zero)
				return;
			Record(graphic, "Move Line Points");
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			foreach (PointId id in selectedPoints)
			{
				Segment segment = segments[id.segment];
				segment.points[id.point] = Snap(segment.points[id.point] + delta);
				segments[id.segment] = segment;
			}
			graphic.segments = segments;
			Changed(graphic);
		}

		private void ScaleSelection(MultiLineGraphic graphic, Vector2 oldMin, Vector2 oldMax, Vector2 newMin, Vector2 newMax)
		{
			Record(graphic, "Scale Line Points");
			Vector2 oldSize = oldMax - oldMin;
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			foreach (PointId id in selectedPoints)
			{
				Segment segment = segments[id.segment];
				Vector2 point = segment.points[id.point];
				float x = oldSize.x > 0.000001f ? Mathf.InverseLerp(oldMin.x, oldMax.x, point.x) : 0.5f;
				float y = oldSize.y > 0.000001f ? Mathf.InverseLerp(oldMin.y, oldMax.y, point.y) : 0.5f;
				segment.points[id.point] = Snap(new Vector2(
					Mathf.Lerp(newMin.x, newMax.x, x),
					Mathf.Lerp(newMin.y, newMax.y, y)));
				segments[id.segment] = segment;
			}
			graphic.segments = segments;
			Changed(graphic);
		}

		private void GetSelectionBounds(MultiLineGraphic graphic, out Vector2 min, out Vector2 max)
		{
			bool first = true;
			min = max = Vector2.zero;
			foreach (PointId id in selectedPoints)
			{
				Vector2 point = graphic.segments[id.segment].points[id.point];
				if (first)
				{
					min = max = point;
					first = false;
				}
				else
				{
					min = Vector2.Min(min, point);
					max = Vector2.Max(max, point);
				}
			}
		}

		private PointId FindPoint(MultiLineGraphic graphic, Vector2 guiPosition, float maxDistance)
		{
			PointId best = new(-1, -1);
			float bestDistance = maxDistance;
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int s = 0; s < segments.Length; s++)
			{
				Vector2[] points = segments[s].points ?? Array.Empty<Vector2>();
				for (int p = 0; p < points.Length; p++)
				{
					Vector2 pointGui = HandleUtility.WorldToGUIPoint(graphic.transform.TransformPoint(points[p]));
					float distance = Vector2.Distance(guiPosition, pointGui);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = new PointId(s, p);
					}
				}
			}
			return best;
		}

		private EdgeHit FindEdge(MultiLineGraphic graphic, Vector2 guiPosition, float maxDistance)
		{
			EdgeHit best = new(-1, -1, maxDistance);
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			for (int s = 0; s < segments.Length; s++)
			{
				Segment segment = segments[s];
				Vector2[] points = segment.points ?? Array.Empty<Vector2>();
				int edgeCount = segment.closed ? points.Length : points.Length - 1;
				for (int edge = 0; edge < edgeCount; edge++)
				{
					int next = (edge + 1) % points.Length;
					Vector2 a = HandleUtility.WorldToGUIPoint(graphic.transform.TransformPoint(points[edge]));
					Vector2 b = HandleUtility.WorldToGUIPoint(graphic.transform.TransformPoint(points[next]));
					float distance = DistanceToSegment(guiPosition, a, b);
					if (distance < best.distance)
						best = new EdgeHit(s, edge, distance);
				}
			}
			return best;
		}

		private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
		{
			Vector2 delta = b - a;
			float lengthSquared = delta.sqrMagnitude;
			if (lengthSquared < 0.000001f)
				return Vector2.Distance(point, a);
			float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / lengthSquared);
			return Vector2.Distance(point, a + delta * t);
		}

		private static bool TryGetMouseLocal(MultiLineGraphic graphic, Vector2 guiPosition, out Vector2 local)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
			Vector3 planeNormal = Vector3.Cross(
				graphic.transform.TransformVector(Vector3.right),
				graphic.transform.TransformVector(Vector3.up)).normalized;
			Plane plane = new(planeNormal, graphic.transform.position);
			if (plane.Raycast(ray, out float distance))
			{
				Vector3 local3 = graphic.transform.InverseTransformPoint(ray.GetPoint(distance));
				local = local3;
				return true;
			}
			local = default;
			return false;
		}

		private Vector2 Snap(Vector2 point) => new(
			Mathf.Round(point.x / gridSize) * gridSize,
			Mathf.Round(point.y / gridSize) * gridSize);

		private void SanitizeSelection(MultiLineGraphic graphic)
		{
			Segment[] segments = graphic.segments ?? Array.Empty<Segment>();
			selectedPoints.RemoveWhere(id =>
				id.segment < 0 || id.segment >= segments.Length ||
				id.point < 0 || id.point >= (segments[id.segment].points?.Length ?? 0));
			activeSegment = segments.Length == 0 ? -1 : Mathf.Clamp(activeSegment, 0, segments.Length - 1);
		}

		private void EnsureActiveSegment()
		{
			if (target is not MultiLineGraphic graphic)
				return;
			int count = graphic.segments?.Length ?? 0;
			if (count == 0)
				activeSegment = -1;
			else if (activeSegment < 0 || activeSegment >= count)
				activeSegment = 0;
		}

		private static void Record(MultiLineGraphic graphic, string name) => Undo.RecordObject(graphic, name);

		private static void Changed(MultiLineGraphic graphic)
		{
			graphic.SetVerticesDirty();
			EditorUtility.SetDirty(graphic);
			PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
			SceneView.RepaintAll();
		}

		private static void Notify(SceneView sceneView, string message) =>
			sceneView.ShowNotification(new GUIContent(message), 1.8f);

		private static bool HasCreationModifier(Event evt) => evt.control || evt.command;

		private bool IsPointerOverPanel(Vector2 mousePosition) => panelRect.Contains(mousePosition);

		private Rect GetPanelRect() => new(12f, 12f, PanelWidth, showHelp ? 328f : 230f);

		private static Rect GUIRect(Vector2 a, Vector2 b) => Rect.MinMaxRect(
			Mathf.Min(a.x, b.x),
			Mathf.Min(a.y, b.y),
			Mathf.Max(a.x, b.x),
			Mathf.Max(a.y, b.y));
	}
}
