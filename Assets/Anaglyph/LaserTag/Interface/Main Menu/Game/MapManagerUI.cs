using System;
using Anaglyph.Menu;
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
		[Tooltip("Expose operator space creation in the shared catalog")]
		[SerializeField] private bool operatorMode;
		private MapPickerBinder picker;

		public void Bind(VisualElement root, NavPage spaceDetails, Action editMap)
		{
			picker ??= new MapPickerBinder(operatorMode);
			picker.Bind(root, editMap, spaceDetails.NavigateHere);
		}

		public void Unbind() => picker?.Dispose();
		private void OnDisable() => Unbind();
	}
}
