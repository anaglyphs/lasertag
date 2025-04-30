using UnityEngine;

namespace Anaglyph.Zombies
{
    public class NewMonoBehaviourScript : MonoBehaviour
    {
        [SerializeField] private GameObject go;

		private void OnDestroy()
		{
			Instantiate(go, transform.position, transform.rotation);
		}
	}
}
