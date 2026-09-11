using System;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Binds manual tag size, count, and removal without requiring headset tools.</summary>
	public sealed class TagConfigurationBinder : IDisposable
	{
		private readonly Slider tagSizeSlider;
		private readonly Label tagSizeNote;
		private readonly Label tagStatus;
		private readonly Button unregisterAllTagsButton;
		private readonly UIEventBindings bindings = new();
		private const float tagSizeSettleSeconds = 0.1f;
		private float? pendingTagSizeCm;
		private float pendingTagSizeTime;
		private bool sizeInputEnabled = true;
		private string alignmentStatus;

		public TagConfigurationBinder(VisualElement root)
		{
			tagSizeSlider = Require<Slider>(root, "tag-size-slider");
			tagSizeNote = Require<Label>(root, "tag-size-note");
			tagStatus = Require<Label>(root, "tag-status");
			unregisterAllTagsButton = Require<Button>(root, "unregister-all-tags-button");
			TextField input = tagSizeSlider.Q<TextField>();
			if (input != null)
			{
				input.isDelayed = true;
				input.keyboardType = TouchScreenKeyboardType.DecimalPad;
			}
			bindings.Value(tagSizeSlider, OnTagSizeChanged);
			bindings.Click(unregisterAllTagsButton, OnUnregisterAllTagsClicked);
			MenuCopy.Changed += RefreshControls;
		}

		public void Dispose()
		{
			MenuCopy.Changed -= RefreshControls;
			bindings.Dispose();
			FlushPendingSize();
		}

		// A slider drag never takes focus and reports no end event. Commit only the size
		// it settles on so the session does not negotiate every intermediate value.
		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
		}

		public void FlushPendingSize()
		{
			if (!pendingTagSizeCm.HasValue) return;
			float centimeters = pendingTagSizeCm.Value;
			pendingTagSizeCm = null;
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager != null && !manager.SetTagSize(centimeters))
				RefreshControls();
		}

		private void OnUnregisterAllTagsClicked()
		{
			LaserTagMapCoordinator.Instance?.UnregisterAllTags();
			RefreshControls();
		}

		public void Refresh(bool sizeInputEnabled = true, string alignmentStatus = null)
		{
			this.sizeInputEnabled = sizeInputEnabled;
			this.alignmentStatus = alignmentStatus;
			if (pendingTagSizeCm.HasValue && Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingSize();
			RefreshControls();
		}

		private void RefreshControls()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null)
			{
				SetMessage(tagStatus, null);
				tagSizeSlider.SetEnabled(false);
				unregisterAllTagsButton.SetEnabled(false);
				SetMessage(tagSizeNote, null);
				return;
			}

			string sizeBlocker = manager.DescribeTagSizeBlocker();
			tagSizeSlider.SetEnabled(sizeBlocker == null && sizeInputEnabled);
			SetMessage(tagSizeNote, sizeBlocker == alignmentStatus ? null : sizeBlocker);
			if (!pendingTagSizeCm.HasValue && !IsBeingEdited(tagSizeSlider))
			{
				tagSizeSlider.highValue = Mathf.Max(50f, manager.EffectiveTagSizeCm);
				tagSizeSlider.SetValueWithoutNotify(manager.EffectiveTagSizeCm);
			}

			tagStatus.style.display = DisplayStyle.Flex;
			int registered = manager.CurrentMap != null ? manager.CurrentMap.tags.Count : 0;
			unregisterAllTagsButton.SetEnabled(registered > 0 && manager.DescribeTagRemovalBlocker() == null);
			tagStatus.text = MenuCopy.Format("Game", "alignment.tag-count", registered);
		}

		private static bool IsBeingEdited(VisualElement field)
		{
			Focusable focused = field.panel?.focusController?.focusedElement;
			return focused is VisualElement element && (element == field || field.Contains(element));
		}

		private static void SetMessage(Label label, string message)
		{
			label.text = message ?? "";
			label.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
		}
	}
}
