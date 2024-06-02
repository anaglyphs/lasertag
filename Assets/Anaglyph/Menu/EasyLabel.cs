using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	public class EasyLabel : MonoBehaviour
	{
#if UNITY_EDITOR
		private void OnValidate()
		{
			GetComponentInChildren<Text>().text = gameObject.name;
		}
#endif
	}
}