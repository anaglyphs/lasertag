using UnityEngine;

namespace Anaglyph
{
    public class DestroyIfNotEditor : MonoBehaviour
    {
#if !UNITY_EDITOR
		private void Start() {
			Destroy(gameObject);
		}
#endif
    }
}
