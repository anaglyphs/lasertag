using System;
using System.Linq;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// The operator's three-step AprilTag setup page: print, connect a headset that measures
	/// and registers on the operator's behalf, review and finish. It presents itself while
	/// <see cref="TagSetup"/> runs and leaves when the setup ends.
	/// </summary>
	public sealed class AprilTagSetupWizard : IDisposable
	{
		private readonly NavView navigation;
		private readonly NavPage page;
		private readonly Button back, next, finish, focus;
		private readonly Label scope, stepLabel, address, status, tags, error;
		private readonly Toggle confirm;
		private readonly VisualElement[] steps;
		private readonly UIEventBindings bindings = new();
		private bool presented;
		private int step;
		private int reviewedTags = -1;
		private float nextRefresh;
		private static string Copy(string key) => MenuCopy.Get("Map", "tag-setup." + key);
		private static TagSetup Setup => SpaceManager.Instance != null ? SpaceManager.Instance.TagSetup : null;

		/// <summary>Whether a guided setup is running for the shown space.</summary>
		public bool IsActive => Setup != null && Setup.IsActive;

		public AprilTagSetupWizard(NavView navigation, Action focusTags)
		{
			this.navigation = navigation;
			page = navigation.GetPage("apriltag-setup-page");
			back = Require<Button>(page, "setup-back");
			next = Require<Button>(page, "setup-next");
			finish = Require<Button>(page, "setup-finish");
			focus = Require<Button>(page, "setup-focus-tags");
			scope = Require<Label>(page, "setup-space");
			stepLabel = Require<Label>(page, "setup-step");
			address = Require<Label>(page, "setup-address");
			status = Require<Label>(page, "setup-status");
			tags = Require<Label>(page, "setup-tags");
			error = Require<Label>(page, "setup-error");
			confirm = Require<Toggle>(page, "setup-confirm");
			steps = new[] { Require<VisualElement>(page, "setup-print"), Require<VisualElement>(page, "setup-connect"),
				Require<VisualElement>(page, "setup-review") };
			bindings.Click(back, () => { step = Math.Max(0, step - 1); Refresh(); });
			bindings.Click(next, () => { step = Math.Min(2, step + 1); if (step == 2) focusTags(); Refresh(); });
			bindings.Click(focus, focusTags);
			bindings.Click(Require<Button>(page, "setup-print-link"), () => Application.OpenURL("https://novanix.github.io/apriltag-pdf-gen/"));
			bindings.Click(Require<Button>(page, "setup-cancel"), () => { Setup?.Cancel(); Refresh(); });
			bindings.Click(finish, Finish);
			bindings.Value(confirm, _ => Refresh());
			Refresh();
		}

		private void Finish()
		{
			TagSetup setup = Setup;
			if (!confirm.value || setup == null || !setup.CanFinish) { Refresh(); return; }
			if (!setup.Finish()) error.text = Copy("save-failed");
			Refresh();
		}

		public void Tick()
		{
			if (Time.unscaledTime < nextRefresh) return;
			nextRefresh = Time.unscaledTime + .25f;
			Refresh();
		}

		public void Refresh()
		{
			bool active = IsActive;
			if (active != presented)
			{
				if (active)
				{
					step = 0;
					error.text = "";
					confirm.SetValueWithoutNotify(false);
					reviewedTags = -1;
				}
				presented = active;
				navigation.SetModalPresented(page, active, 15);
			}
			RefreshStepVisibility();
			if (!active) return;
			TagSetup setup = Setup;
			Space space = SpaceManager.Instance.Space;
			// a tag registered after the review was ticked needs another look
			if (reviewedTags != space.tags.Count)
			{
				reviewedTags = space.tags.Count;
				confirm.SetValueWithoutNotify(false);
			}
			scope.text = MenuCopy.Format("Map", "tag-setup.space", space.name);
			stepLabel.text = MenuCopy.Format("Map", "tag-setup.step", step + 1, Copy(new[] { "print-title", "connect-title", "review-title" }[step]));
			address.text = string.IsNullOrEmpty(OperatorHost.LocalAddress) ? Copy("no-address") : OperatorHost.LocalAddress;
			status.text = !setup.State.hasAuthor ? Copy("waiting-headset")
				: !setup.SizeConfirmed ? Copy("measuring")
				: space.HasTags && !setup.CanFinish ? Copy("wait-validation")
				: Copy("registering");
			tags.text = space.HasTags ? string.Join(", ", space.tags.OrderBy(tag => tag.id).Select(tag => "#" + tag.id)) : Copy("no-tags");
			next.SetEnabled(step == 0 || space.HasTags);
			confirm.SetEnabled(setup.CanFinish);
			finish.SetEnabled(confirm.value && setup.CanFinish);
			finish.tooltip = setup.CanFinish ? null : Copy("wait-validation");
			focus.SetEnabled(space.HasTags);
		}

		private void RefreshStepVisibility()
		{
			address.style.display = step != 1 ? DisplayStyle.None : DisplayStyle.Flex;
			for (int i = 0; i < steps.Length; i++) steps[i].style.display = step != i ? DisplayStyle.None : DisplayStyle.Flex;
			back.style.display = step == 0 ? DisplayStyle.None : DisplayStyle.Flex;
			next.style.display = step == 2 ? DisplayStyle.None : DisplayStyle.Flex;
			finish.style.display = step != 2 ? DisplayStyle.None : DisplayStyle.Flex;
		}

		public void Dispose()
		{
			Setup?.Cancel();
			bindings.Dispose();
			if (presented) navigation.SetModalPresented(page, false, 15);
		}
	}
}
