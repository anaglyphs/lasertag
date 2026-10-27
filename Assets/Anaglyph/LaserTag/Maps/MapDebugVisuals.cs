using Anaglyph.Debugging;
using Anaglyph.Debugging.Visuals;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Draws the shown space's reference poses while debug mode is on.</summary>
	public class MapDebugVisuals : MonoBehaviour
	{
		[SerializeField] private Color anchorColor = Color.cyan;
		[SerializeField] private Color tagColor = Color.magenta;

		private void Update()
		{
			if (!AnaglyphDebugging.DebugMode || SpaceManager.Instance == null) return;
			Space space = SpaceManager.Instance.Space;
			if (space == null) return;
			foreach (AnchorRef anchor in space.anchors) DebugAxisVisual.DrawDebugAxis(anchor.pose.position, anchor.pose.rotation, anchorColor);
			foreach (TagRef tag in space.tags) DebugAxisVisual.DrawDebugAxis(tag.pose.position, tag.pose.rotation, tagColor);
		}
	}
}
