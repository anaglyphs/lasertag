using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// The page between match settings and a queued match: lists the bases and flags the chosen
	/// settings still need, and allows starting once they are placed.
	/// </summary>
	public sealed class MatchObjectsBinder
	{
		public const string PageName = "match-objects-page";

		private readonly FlashingLabel recognizingSpaceLabel;
		private readonly VisualElement section;
		private readonly Label missingBasesWarning;
		private readonly Label missingFlagsWarning;

		public Button PlaceObjectsButton { get; }
		public Button StartButton { get; }

		public MatchObjectsBinder(NavPage page)
		{
			recognizingSpaceLabel = Require<FlashingLabel>(page, "recognizing-space-label");
			section = Require<VisualElement>(page, "match-objects-section");
			missingBasesWarning = Require<Label>(page, "missing-bases-warning");
			missingFlagsWarning = Require<Label>(page, "missing-flags-warning");
			PlaceObjectsButton = Require<Button>(page, "place-objects-button");
			StartButton = Require<Button>(page, "start-match-button");
		}

		/// <summary>Shows what a match with these settings still needs. True when it can start.</summary>
		public bool Refresh(MatchSettings settings, bool placingObjects)
		{
			SpaceManager spaces = SpaceManager.Instance;
			bool recognizingSpace = spaces != null && spaces.Space == null;
			SetShown(recognizingSpaceLabel, recognizingSpace);
			recognizingSpaceLabel.Flashing = recognizingSpace;
			SetShown(section, !recognizingSpace);
			SetShown(PlaceObjectsButton, !recognizingSpace && !placingObjects);

			MissingMatchObjects missing = MapRequirements.MissingFor(settings);
			SetShown(missingBasesWarning, missing.HasFlag(MissingMatchObjects.BothTeamBases));
			SetShown(missingFlagsWarning, missing.HasFlag(MissingMatchObjects.AnyFlag));

			bool canStart = missing == MissingMatchObjects.None;
			StartButton.SetEnabled(canStart);
			return canStart;
		}

		private static void SetShown(VisualElement element, bool shown) =>
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
	}
}
