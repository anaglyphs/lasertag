using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class ObjectPallet : MonoBehaviour
	{
		[SerializeField] private GameObject objButtonPrefab;
		[SerializeField] private RectTransform buttonParent;
		[SerializeField] private GameObjectMenu objectMenu;

		private void Awake()
		{
			foreach (GameObjectMenu.Entry entry in objectMenu.entries)
			{
				GameObject g = Instantiate(objButtonPrefab, buttonParent);
			}
		}
	}
}