using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Picks one of a row of marks: icons given as UXML children, or ticks and bar segments
	/// made by <see cref="SetTicks"/> and <see cref="SetSegments(float[])"/>. Press or drag across
	/// the row to choose. The value is the chosen mark's index. Shows its label and, beneath it,
	/// the chosen mark's text (or <see cref="FormatValue"/>'s).
	/// </summary>
	[UxmlElement]
	public partial class DetentField : BaseField<int>
	{
		public new static readonly string ussClassName = "detent-field";
		public static readonly string valueTextUssClassName = ussClassName + "__value";
		public static readonly string marksUssClassName = ussClassName + "__marks";
		public static readonly string tickUssClassName = ussClassName + "__tick";
		public static readonly string segmentUssClassName = ussClassName + "__segment";
		public static readonly string tickMarksUssClassName = marksUssClassName + "--ticks";
		public static readonly string litMarkUssClassName = DetentMark.ussClassName + "--lit";
		public static readonly string hoveredMarkUssClassName = DetentMark.ussClassName + "--hovered";

		private readonly VisualElement marks;
		private readonly Label valueText = new();
		private bool cumulative;
		private Func<int, string> formatValue;
		private int hoveredIndex = -1;

		public override VisualElement contentContainer => marks ?? this;

		/// <summary>Replaces the marks with evenly spaced ticks.</summary>
		public void SetTicks(int count) => ReplaceMarks(count, tickUssClassName, null);

		/// <summary>Replaces the marks with one bar of segments, each as wide as its weight.</summary>
		public void SetSegments(float[] weights) => ReplaceMarks(weights.Length, segmentUssClassName, weights);

		/// <summary>Replaces the marks with one bar of equal segments.</summary>
		public void SetSegments(int count) => ReplaceMarks(count, segmentUssClassName, null);

		private void ReplaceMarks(int count, string markClassName, float[] weights)
		{
			marks.EnableInClassList(tickMarksUssClassName, markClassName == tickUssClassName);
			marks.Clear();
			for (int i = 0; i < count; i++)
			{
				var mark = new DetentMark();
				mark.AddToClassList(markClassName);
				if (markClassName == segmentUssClassName)
					mark.style.flexGrow = weights?[i] ?? 1;
				marks.Add(mark);
			}
			Refresh();
		}

		/// <summary>Adds a USS class to one mark, to give it its own shape.</summary>
		public void AddMarkClass(int index, string className) => marks[index].AddToClassList(className);

		/// <summary>Light every mark up to the chosen one, not only the chosen one.</summary>
		[UxmlAttribute("cumulative")]
		public bool Cumulative
		{
			get => cumulative;
			set
			{
				cumulative = value;
				Refresh();
			}
		}

		/// <summary>Text for a value. Without it, the chosen mark's text shows.</summary>
		public Func<int, string> FormatValue
		{
			get => formatValue;
			set
			{
				formatValue = value;
				Refresh();
			}
		}

		public DetentField() : this(null) { }

		public DetentField(string label) : base(label, new VisualElement())
		{
			AddToClassList(ussClassName);
			marks = this.Q(className: inputUssClassName);
			marks.AddToClassList(marksUssClassName);
			valueText.AddToClassList(valueTextUssClassName);
			hierarchy.Insert(hierarchy.IndexOf(marks), valueText);

			marks.RegisterCallback<PointerDownEvent>(OnPointerDown);
			marks.RegisterCallback<PointerMoveEvent>(OnPointerMove);
			marks.RegisterCallback<PointerUpEvent>(OnPointerUp);
			marks.RegisterCallback<PointerLeaveEvent>(_ => SetHoveredIndex(-1));
			RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
			RegisterCallback<AttachToPanelEvent>(_ => Refresh());
		}

		public override void SetValueWithoutNotify(int newValue)
		{
			base.SetValueWithoutNotify(newValue);
			Refresh();
		}

		internal void Refresh()
		{
			RefreshMarks();
			RefreshValueText();
		}

		private void RefreshMarks()
		{
			int chosen = value;
			// Pointing inside a cumulative choice previews the smaller one over it
			bool hoveringWithinChoice = Cumulative && hoveredIndex >= 0 && hoveredIndex <= chosen;
			for (int i = 0; i < marks.childCount; i++)
			{
				bool lit = Covers(chosen, i);
				marks[i].EnableInClassList(litMarkUssClassName, lit);
				marks[i].EnableInClassList(hoveredMarkUssClassName, Covers(hoveredIndex, i) && (!lit || hoveringWithinChoice));
			}
		}

		internal void RefreshValueText()
		{
			int chosen = value;
			if (formatValue != null)
				valueText.text = formatValue(chosen);
			else
				valueText.text = chosen >= 0 && chosen < marks.childCount && marks[chosen] is DetentMark mark ? mark.Text : string.Empty;
		}

		private bool Covers(int markedIndex, int i) => Cumulative ? i <= markedIndex : i == markedIndex;

		private void SetHoveredIndex(int index)
		{
			if (index == hoveredIndex)
				return;
			hoveredIndex = index;
			RefreshMarks();
		}

		private int NearestMarkIndex(Vector2 panelPosition)
		{
			Vector2 marksLocalPosition = marks.WorldToLocal(panelPosition);
			int nearest = -1;
			float nearestDistance = float.MaxValue;
			for (int i = 0; i < marks.childCount; i++)
			{
				Rect markRect = marks[i].layout;
				float distance = Mathf.Abs(marksLocalPosition.x - markRect.center.x);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = i;
				}
			}
			return nearest;
		}

		private void ChooseMarkUnder(Vector2 panelPosition)
		{
			int nearest = NearestMarkIndex(panelPosition);
			if (nearest >= 0)
				value = nearest;
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			if (!enabledInHierarchy)
				return;
			marks.CapturePointer(evt.pointerId);
			ChooseMarkUnder(evt.position);
			evt.StopPropagation();
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (enabledInHierarchy)
				SetHoveredIndex(NearestMarkIndex(evt.position));
			if (marks.HasPointerCapture(evt.pointerId))
				ChooseMarkUnder(evt.position);
		}

		private void OnPointerUp(PointerUpEvent evt)
		{
			if (marks.HasPointerCapture(evt.pointerId))
				marks.ReleasePointer(evt.pointerId);
		}

		private void OnNavigationMove(NavigationMoveEvent evt)
		{
			int step = evt.direction switch
			{
				NavigationMoveEvent.Direction.Left => -1,
				NavigationMoveEvent.Direction.Right => 1,
				_ => 0,
			};
			if (step == 0 || marks.childCount == 0)
				return;
			value = Mathf.Clamp(value + step, 0, marks.childCount - 1);
			evt.StopPropagation();
		}
	}

	/// <summary>One choice in a <see cref="DetentField"/>; its text names the choice.</summary>
	[UxmlElement]
	public partial class DetentMark : VisualElement
	{
		public static readonly string ussClassName = "detent-field__mark";

		private string text = string.Empty;

		[UxmlAttribute("text"), CreateProperty]
		public string Text
		{
			get => text;
			set
			{
				text = value ?? string.Empty;
				GetFirstAncestorOfType<DetentField>()?.RefreshValueText();
			}
		}

		public DetentMark()
		{
			AddToClassList(ussClassName);
		}
	}
}
