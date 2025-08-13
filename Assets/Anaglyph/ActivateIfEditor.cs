using UnityEngine;

namespace Anaglyph
{
    public class ActivateIfEditor : MonoBehaviour
    {
#if !UNITY_EDITOR
		private void Awake() {
			gameObject.SetActive(false);
		}
#endif
    }
}
