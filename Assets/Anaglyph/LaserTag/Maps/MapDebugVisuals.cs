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
			if (!AnaglyphDebugging.DebugMode || MapManager.Instance == null)
				return;

			GameMap map = MapManager.Instance.CurrentMap;
			if (map == null)
				return;

			foreach (MapAnchorEntry anchor in map.anchors)
				DebugAxisVisual.DrawDebugAxis(
					anchor.canonPose.position, anchor.canonPose.rotation, anchorColor);

			foreach (MapTagEntry tag in map.tags)
				DebugAxisVisual.DrawDebugAxis(
					tag.canonPose.position, tag.canonPose.rotation, tagColor);
		}
	}
}
