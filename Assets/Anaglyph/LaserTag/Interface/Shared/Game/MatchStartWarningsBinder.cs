using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Presents MatchStartWarningsPage over the match settings, listing what the map lacks
	/// for the chosen settings. Shared by the headset menu and the desktop operator panel.
	/// </summary>
	public sealed class MatchStartWarningsBinder : IDisposable
	{
		public const string PageName = "match-start-warnings-modal";
		private const int ModalPriority = 10;

		private readonly NavView matchNavigation;
		private readonly NavPage page;
		private readonly Label missingBasesWarning;
		private readonly Label missingFlagsWarning;
		private readonly UIEventBindings bindings = new();

		/// <param name="editMap">Opens map editing. The page offers no edit button without it.</param>
		public MatchStartWarningsBinder(NavView matchNavigation, Action editMap = null)
		{
			this.matchNavigation = matchNavigation;
			page = matchNavigation.GetPage(PageName);
			missingBasesWarning = Require<Label>(page, "missing-bases-warning");
			missingFlagsWarning = Require<Label>(page, "missing-flags-warning");

			Button editMapButton = Require<Button>(page, "match-warnings-edit-map-button");
			SetShown(editMapButton, editMap != null);
			if (editMap != null)
				bindings.Click(editMapButton, () =>
				{
					Dismiss();
					editMap();
				});
			bindings.Click(Require<Button>(page, "match-warnings-dismiss-button"), Dismiss);
		}

		public void Dispose() => bindings.Dispose();

		/// <summary>Shows every warning that applies to these settings. False when there are none.</summary>
		public bool PresentWarningsFor(MatchSettings settings)
		{
			MissingMatchObjects missing = MapRequirements.MissingFor(settings);
			SetShown(missingBasesWarning, missing.HasFlag(MissingMatchObjects.BothTeamBases));
			SetShown(missingFlagsWarning, (missing & (MissingMatchObjects.AnyFlag | MissingMatchObjects.BothTeamFlags)) != 0);

			bool anyWarnings = missing != MissingMatchObjects.None;
			matchNavigation.SetModalPresented(page, anyWarnings, ModalPriority);
			return anyWarnings;
		}

		public void Dismiss() => matchNavigation.DismissModal(page);

		private static void SetShown(VisualElement element, bool shown) =>
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
	}
}
