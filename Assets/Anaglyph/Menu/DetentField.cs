using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// A tile that picks one of a row of marks: icons given as UXML children, or
	/// <c>tick-count</c> plain ticks. Press or drag across the row to choose.
	/// The value is the chosen mark's index. Shows its label and, beneath it,
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
		public static readonly string barMarksUssClassName = marksUssClassName + "--bar";
		public static readonly string litMarkUssClassName = DetentMark.ussClassName + "--lit";

		private readonly VisualElement marks;
		private readonly Label valueText = new();
		private int tickCount;
		private bool cumulative;
		private Func<int, string> formatValue;

		public override VisualElement contentContainer => marks ?? this;

		/// <summary>Plain ticks to generate. Zero keeps the UXML children as marks.</summary>
		[UxmlAttribute("tick-count")]
		public int TickCount
		{
			get => tickCount;
			set
			{
				tickCount = Mathf.Max(0, value);
				if (tickCount == 0)
					return;
				marks.RemoveFromClassList(barMarksUssClassName);
				marks.Clear();
				for (int i = 0; i < tickCount; i++)
				{
					var tick = new DetentMark();
					tick.AddToClassList(tickUssClassName);
					if (i == 0)
						tick.style.marginLeft = 0;
					marks.Add(tick);
				}
				Refresh();
			}
		}

		/// <summary>
		/// Replaces the marks with one bar of segments, each as wide as its weight.
		/// </summary>
		public void SetSegmentWeights(float[] weights)
		{
			tickCount = 0;
			marks.AddToClassList(barMarksUssClassName);
			marks.Clear();
			foreach (float weight in weights)
			{
				var segment = new DetentMark();
				segment.AddToClassList(segmentUssClassName);
				segment.style.flexGrow = weight;
				marks.Add(segment);
			}
			Refresh();
		}

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
			int chosen = value;
			for (int i = 0; i < marks.childCount; i++)
				marks[i].EnableInClassList(litMarkUssClassName, Cumulative ? i <= chosen : i == chosen);

			if (formatValue != null)
				valueText.text = formatValue(chosen);
			else
				valueText.text = chosen >= 0 && chosen < marks.childCount && marks[chosen] is DetentMark mark ? mark.Text : string.Empty;
		}

		private void ChooseMarkUnder(Vector2 marksLocalPosition)
		{
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
			if (nearest >= 0)
				value = nearest;
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			if (!enabledInHierarchy)
				return;
			marks.CapturePointer(evt.pointerId);
			ChooseMarkUnder(evt.localPosition);
			evt.StopPropagation();
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (marks.HasPointerCapture(evt.pointerId))
				ChooseMarkUnder(evt.localPosition);
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
				GetFirstAncestorOfType<DetentField>()?.Refresh();
			}
		}

		public DetentMark()
		{
			AddToClassList(ussClassName);
		}
	}
}
