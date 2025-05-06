using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class WallSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;

		[SerializeField] private float separate = 1;

		private void Awake()
		{
			int num = Mathf.FloorToInt(prefab.GetComponent<WallMover>().MaxDist / separate);

			for (int i = 0; i < num; i++)
				Instantiate(prefab, Vector3.forward * i * separate, Quaternion.identity, transform);
		}
	}
}
