using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class BulletLight : MonoBehaviour
    {
        [SerializeField] private Transform bulletTransform;
		private Vector3 previousBulletPosition;

		private void OnEnable()
		{
			transform.position = previousBulletPosition;
			previousBulletPosition = transform.position;
		}

		private void LateUpdate()
		{
			transform.position = (bulletTransform.position + previousBulletPosition) / 2f;
			previousBulletPosition = bulletTransform.position;
		}
	}
}
