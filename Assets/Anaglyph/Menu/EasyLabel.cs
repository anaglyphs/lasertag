using TMPro;
using UnityEngine;

namespace Anaglyph.Menu
{
	public class EasyLabel : MonoBehaviour
	{
#if UNITY_EDITOR
		private void OnValidate()
		{
			GetComponentInChildren<TMP_Text>().text = gameObject.name;
		}
#endif
	}
}