using System;
using System.Linq;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Operator
{
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
		private string reviewedVersion;
		private float nextRefresh;
		private AprilTagSetupSession Session => LaserTagMapCoordinator.Instance?.TagSetup;
		private static string Copy(string key) => MenuCopy.Get("Map", "tag-setup." + key);

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
			tags = Require<Label>(page, "setup-tags");
			error = Require<Label>(page, "setup-error");
			confirm = Require<Toggle>(page, "setup-confirm");
			steps = new[] { Require<VisualElement>(page, "setup-print"), Require<VisualElement>(page, "setup-connect"),
				Require<VisualElement>(page, "setup-review") };
			bindings.Click(back, () => { step = Math.Max(0, step - 1); Refresh(); });
			bindings.Click(next, () => { step = Math.Min(2, step + 1); if (step == 2) focusTags(); Refresh(); });
			bindings.Click(focus, focusTags);
			bindings.Click(Require<Button>(page, "setup-print-link"), () => Application.OpenURL("https://novanix.github.io/apriltag-pdf-gen/"));
			bindings.Click(Require<Button>(page, "setup-cancel"), () => { Session?.Cancel(); Refresh(); });
			bindings.Click(finish, Finish);
			bindings.Value(confirm, _ => Refresh());
			Refresh();
		}

		private void Finish()
		{
			if (!confirm.value || Session?.CanFinish != true ||
				reviewedVersion != LaserTagMapCoordinator.Instance.CurrentSpace.referenceVersion) { Refresh(); return; }
			if (!Session.Finish()) error.text = Copy("save-failed");
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
			var session = Session;
			bool active = session?.IsActive == true;
			if (active != presented)
			{
				if (active)
				{
					step = 0;
					error.text = "";
					confirm.SetValueWithoutNotify(false);
					reviewedVersion = null;
				}
				presented = active;
				navigation.SetModalPresented(page, active, 15);
			}
			RefreshStepVisibility();
			if (!active) return;
			var coordinator = LaserTagMapCoordinator.Instance;
			var space = coordinator.CurrentSpace;
			if (reviewedVersion != space.referenceVersion)
			{
				reviewedVersion = space.referenceVersion;
				confirm.SetValueWithoutNotify(false);
			}
			scope.text = MenuCopy.Format("Map", "tag-setup.space", space.name);
			stepLabel.text = MenuCopy.Format("Map", "tag-setup.step", step + 1, Copy(new[] { "print-title", "connect-title", "review-title" }[step]));
			address.text = string.IsNullOrEmpty(OperatorHost.LocalAddress) ? Copy("no-address") : OperatorHost.LocalAddress;
			int connected = OperatorHost.GetConnectedClients().Count(client => !client.isThisServer);
			string instruction;
			if (connected == 0)
				instruction = Copy("waiting-headset");
			else if (coordinator.WaitingForAlignmentAuthor && space.HasReferenceBasedData && !space.HasTags)
				instruction = MenuCopy.Get("Map", "alignment.waiting-for-reference-headset");
			else if (!session.SizeConfirmed)
				instruction = Copy("measuring");
			else if (space.HasTags && !session.CanFinish)
				instruction = Copy("wait-validation");
			else
				instruction = Copy("registering");
			// status.text = MenuCopy.Format("Map", "tag-setup.progress", connected, space.tags.Count) + "\n" + instruction;
			tags.text = space.HasTags ? string.Join(", ", space.tags.OrderBy(tag => tag.id).Select(tag => "#" + tag.id)) : Copy("no-tags");
			next.SetEnabled(step == 0 || space.HasTags);
			finish.SetEnabled(confirm.value && session.CanFinish);
			finish.tooltip = session.CanFinish ? null : Copy("wait-validation");
			confirm.SetEnabled(session.CanFinish);
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
			Session?.Cancel();
			bindings.Dispose();
			if (presented) navigation.SetModalPresented(page, false, 15);
		}
	}
}
