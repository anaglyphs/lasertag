using Anaglyph.Debugging;
using Anaglyph.Debugging.Visuals;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Draws the current map's canon reference poses while debug mode is on.</summary>
	public class MapDebugVisuals : MonoBehaviour
	{
		[SerializeField] private Color anchorColor = Color.cyan;
		[SerializeField] private Color tagColor = Color.magenta;

		private void Update()
		{
			if (!AnaglyphDebugging.DebugMode || LaserTagMapCoordinator.Instance == null)
				return;

			MapSpace map = LaserTagMapCoordinator.Instance.CurrentSpace;
			if (map == null)
				return;

			foreach (MapAnchorEntry anchor in map.anchors)
				DebugAxisVisual.DrawDebugAxis(
					map.Frame.ToCanonical(anchor.canonPose).position, map.Frame.ToCanonical(anchor.canonPose).rotation, anchorColor);

			foreach (MapTagEntry tag in map.tags)
				DebugAxisVisual.DrawDebugAxis(
					map.Frame.ToCanonical(tag.canonPose).position, map.Frame.ToCanonical(tag.canonPose).rotation, tagColor);
		}
	}
}
