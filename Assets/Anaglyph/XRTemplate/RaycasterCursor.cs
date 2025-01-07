using UnityEngine;
using UnityEngine.Assertions;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-100)]
	public class RaycasterCursor : MonoBehaviour
	{
		[SerializeField] private Raycaster raycaster = null;
		[SerializeField] private new Renderer renderer = null;

		private void Awake()
		{
			Assert.IsNotNull(raycaster);
			Assert.IsNotNull(renderer);

			raycaster.onHitPoint.AddListener(SetPos);
		}

		public void SetMaterial(Material mat)
		{
			renderer.material = mat;
		}

		private void Update()
		{
			renderer.enabled = false;
		}

		private void SetPos(Vector3 pos)
		{
			renderer.enabled = true;
			transform.position = pos;
		}
	}
}
