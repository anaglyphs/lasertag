using UnityEngine;

namespace Anaglyph
{
    public class ActiveIfEditor : MonoBehaviour
    {
#if !UNITY_EDITOR
		private void Awake() {
			gameObject.SetActive(false);
		}
#endif
    }
}
