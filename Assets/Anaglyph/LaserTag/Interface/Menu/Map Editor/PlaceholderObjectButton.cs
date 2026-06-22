using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class PlaceholderObjectButton : MonoBehaviour
	{
		[SerializeField] private MapObject mapObjPrefab;

		private void Awake()
		{
			Button button = GetComponent<Button>();
			button.onClick.AddListener(delegate
			{
				MapEditorTool[] tools =
					FindObjectsByType<MapEditorTool>(FindObjectsInactive.Include, FindObjectsSortMode.None);

				foreach (MapEditorTool tool in tools) tool.SetSpawnObject(mapObjPrefab);
			});
		}
	}
}