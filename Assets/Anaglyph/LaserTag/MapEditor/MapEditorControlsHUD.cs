using Anaglyph.LaserTag.Interface.HUD;
using Anaglyph.LaserTag.MapEditor.Tools;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.MapEditor
{
	public class MapEditorControlsHUD : MonoBehaviour
	{
		[SerializeField] private HandHUDPositioner positioner;
		[SerializeField] private UIDocument document;
		
		private VisualElement controls;
		private Label title;
		private Label bindings;
		private MapEditorTool.Mode? shownMode;

		private void OnEnable()
		{
			
			controls = HUDElement.Require<VisualElement>(this, "editor-controls-panel");
			title = controls.Q<Label>("controls-title");
			bindings = controls.Q<Label>("controls-bindings");
			shownMode = null;
			HUDElement.SetDisplayed(controls, false);
		}

		private void OnDisable()
		{
			if (controls != null)
				HUDElement.SetDisplayed(controls, false);
			positioner.SetHand(null);
		}

		private void Update()
		{
			// document.pivot = positioner.ControllerSide == 1 ? Pivot.RightCenter : Pivot.LeftCenter;
			
			MapEditorTool tool = MapEditorTool.DominantHand;
			var hand = tool != null && tool.isActiveAndEnabled ? tool.Hand : null;
			bool visible = MapEditor.IsActive && hand != null && hand.IsTracking;
			positioner.SetHand(visible ? hand : null);
			HUDElement.SetDisplayed(controls, visible);
			if (!visible || shownMode == MapEditorTool.CurrentMode) return;

			shownMode = MapEditorTool.CurrentMode;
			string modeKey = shownMode switch
			{
				MapEditorTool.Mode.Move => "move",
				MapEditorTool.Mode.Place => "place",
				MapEditorTool.Mode.Tags => "tags",
				MapEditorTool.Mode.MeasureTagSize => "measure-tag-size",
				_ => throw new System.ArgumentOutOfRangeException()
			};
			title.SetBinding("text", MenuCopy.String("Map", $"controls.{modeKey}.title"));
			bindings.SetBinding("text", MenuCopy.String("Map", $"controls.{modeKey}.bindings"));
		}
	}
}
