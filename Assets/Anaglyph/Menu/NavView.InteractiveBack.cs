using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Going back by dragging: the page follows the finger off to the right over the page before it,
	/// and nothing navigates until the drag is let go far enough. A drag let go short slides back.
	/// </summary>
	public sealed partial class NavView
	{
		private const float CancelSeconds = 0.15f;

		private NavPage previewTarget;
		private float previewProgress, previewWidth;
		private Action restorePreview;
		private IVisualElementScheduledItem cancelTick;
		private double cancelStart;
		private float cancelFrom;

		/// <summary>Whether a drag back can follow the finger here: back to a page, not out of a modal.</summary>
		public bool CanDragBack => CanGoBack && TopModalIndex() == -1 && outgoingPage == null && previewTarget == null;

		/// <summary>The innermost navigation shown under <paramref name="root"/> that can go back; null when none can.</summary>
		public static NavView InnermostThatCanGoBack(VisualElement root)
		{
			NavView innermost = null;
			int innermostDepth = -1;
			root?.Query<NavView>().ForEach(view =>
			{
				if (!IsShown(view) || !view.CanGoBack) return;
				int depth = Depth(view);
				if (depth <= innermostDepth) return;
				innermost = view;
				innermostDepth = depth;
			});
			return innermost;
		}

		/// <summary>Starts a drag back: the page before shows behind this one. False when it can't follow a finger.</summary>
		public bool BeginDragBack()
		{
			EnsureBuilt();
			FinishCancel();
			if (!CanDragBack || CurrentPage == null || !(CurrentPage.layout.width > 0)) return false;

			NavPage current = CurrentPage;
			previewTarget = history[^2];
			Rect bounds = current.layout;
			previewWidth = bounds.width;
			var currentLayout = (current.style.position, current.style.left, current.style.top,
				current.style.width, current.style.height, current.style.translate);
			var targetTranslate = previewTarget.style.translate;
			var viewLayout = (style.minWidth, style.maxWidth, style.minHeight, style.maxHeight, style.overflow);
			NavPage target = previewTarget;
			restorePreview = () =>
			{
				(current.style.position, current.style.left, current.style.top,
					current.style.width, current.style.height, current.style.translate) = currentLayout;
				target.style.translate = targetTranslate;
				(style.minWidth, style.maxWidth, style.minHeight, style.maxHeight, style.overflow) = viewLayout;
			};

			style.minWidth = style.maxWidth = layout.width;
			style.minHeight = style.maxHeight = layout.height;
			style.overflow = Overflow.Hidden;
			current.style.position = Position.Absolute;
			current.style.left = bounds.x - resolvedStyle.borderLeftWidth - current.resolvedStyle.marginLeft;
			current.style.top = bounds.y - resolvedStyle.borderTopWidth - current.resolvedStyle.marginTop;
			current.style.width = bounds.width;
			current.style.height = bounds.height;
			current.BringToFront();
			SetPageVisible(previewTarget, true);
			// whatever the finger pressed on the page lets go: a drag isn't a tap
			ReleasePagePointers(current);
			UpdateDragBack(0);
			return true;
		}

		/// <summary>How far across the drag has come, as a fraction of the page's width.</summary>
		public void UpdateDragBack(float progress)
		{
			if (previewTarget == null) return;
			previewProgress = Mathf.Clamp01(progress);
			SetTranslation(CurrentPage, new Vector2(previewWidth * previewProgress, 0));
			SetTranslation(previewTarget, new Vector2(-previewWidth * (1 - previewProgress), 0));
		}

		/// <summary>Width of the page being dragged, in panel units, to turn a finger's travel into progress.</summary>
		public float DragBackWidth => previewWidth;

		/// <summary>Lets go: <paramref name="commit"/> goes back from where the drag is; otherwise the page slides back.</summary>
		public void EndDragBack(bool commit)
		{
			if (previewTarget == null) return;
			if (commit)
			{
				float progress = previewProgress;
				EndPreview();
				resumeBackAt = progress;
				GoBack();
				resumeBackAt = 0;
				return;
			}

			cancelFrom = previewProgress;
			cancelStart = Time.realtimeSinceStartupAsDouble;
			cancelTick ??= schedule.Execute(AdvanceCancel).Every(0);
			cancelTick.Resume();
		}

		private void AdvanceCancel()
		{
			float t = (float)((Time.realtimeSinceStartupAsDouble - cancelStart) / CancelSeconds);
			if (t >= 1 || previewTarget == null)
			{
				FinishCancel();
				return;
			}

			UpdateDragBack(cancelFrom * Mathf.Pow(1 - t, 3));
		}

		private void FinishCancel()
		{
			cancelTick?.Pause();
			if (previewTarget != null) EndPreview();
		}

		private void EndPreview()
		{
			SetPageVisible(previewTarget, false);
			restorePreview?.Invoke();
			restorePreview = null;
			previewTarget = null;
			previewProgress = 0;
		}
	}
}
