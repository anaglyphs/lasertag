using UnityEngine;

namespace Anaglyph.Menu
{
    public class DeactivateSiblingsOnEnable : MonoBehaviour
    {


        private void OnEnable()
        {
            if (transform.parent == null)
                return;
            
            for (int i = 0; i < transform.parent.childCount; i++)
            {
                Transform sibling = transform.parent.GetChild(i);
                if (sibling == transform)
                    continue;

                sibling.gameObject.SetActive(false);
            }
        }
    }
}
