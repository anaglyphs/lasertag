using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	public class URL : MonoBehaviour
	{
		public string url;

		public void Open() => Application.OpenURL(url);

		private void Awake()
		{
			if (TryGetComponent(out Button button))
				button.onClick.AddListener(Open);
		}
	}
}
