using System;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	[UxmlElement]
	public sealed partial class FlashingLabel : Label
	{
		public new static readonly string ussClassName = "flashing-label";
		public static readonly string dimmedUssClassName = "flashing-label--dimmed";

		private readonly IVisualElementScheduledItem flashTick;
		private bool flashing = true;
		private int intervalMs = 600;
		private bool dimmed;

		[UxmlAttribute("flashing")]
		public bool Flashing
		{
			get => flashing;
			set
			{
				if (flashing == value) return;
				flashing = value;
				RestartFlashing();
			}
		}

		[UxmlAttribute("interval-ms")]
		public int IntervalMs
		{
			get => intervalMs;
			set
			{
				value = Math.Max(1, value);
				if (intervalMs == value) return;
				intervalMs = value;
				RestartFlashing();
			}
		}

		public FlashingLabel()
		{
			AddToClassList(ussClassName);
			pickingMode = PickingMode.Ignore;
			flashTick = schedule.Execute(ToggleFlash);
			flashTick.Pause();
			RegisterCallback<AttachToPanelEvent>(_ => RestartFlashing());
			RegisterCallback<DetachFromPanelEvent>(_ => flashTick.Pause());
		}

		private void RestartFlashing()
		{
			flashTick.Pause();
			SetDimmed(false);
			if (flashing && panel != null)
				flashTick.Every(intervalMs).ExecuteLater(intervalMs);
		}

		private void ToggleFlash() => SetDimmed(!dimmed);

		private void SetDimmed(bool value)
		{
			if (dimmed == value) return;
			dimmed = value;
			EnableInClassList(dimmedUssClassName, value);
		}
	}
}
