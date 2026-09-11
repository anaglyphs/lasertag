using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Operator;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Keeps the prefab's map-picker configuration and selection across visual-tree bindings.
	/// GameMenu and OperatorMenu bind it after preparing their controls; no execution-order dependency.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public sealed class MapManagerUI : MonoBehaviour
	{
		[Tooltip("Use the operator-hostable map catalog instead of the headset room filter")]
		[SerializeField] private bool operatorMode;
		private MapPickerBinder picker;

		public void Bind(VisualElement root)
		{
			picker ??= new MapPickerBinder(IncludeMap, GetEmptyStateKey);
			picker.Bind(root);
		}

		private bool IncludeMap(GameMap map)
		{
			if (operatorMode)
				return OperatorHost.CanHostMap(map);

			// Headsets only hide a map positively located elsewhere. System-provided
			// origins do not depend on a room's anchors, and untested maps stay visible.
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			return manager == null || map.systemFrameForTagSetup ||
				map.preferredColocationMethod == ColocationManager.ColocationMethod.SystemDetermined ||
				manager.GetMapPresence(map.id) != MapPresence.Elsewhere;
		}

		private string GetEmptyStateKey(int savedCount) => operatorMode
			? "maps.empty-operator"
			: savedCount == 0 ? "maps.empty" : "maps.empty-room";

		public void Unbind() => picker?.Dispose();
		private void OnDisable() => Unbind();
	}
}
