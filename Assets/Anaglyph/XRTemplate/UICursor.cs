using UnityEngine;


namespace Anaglyph.XRTemplate
{
    public class UICursor : MonoBehaviour
    {
        [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor interactor;
		private SpriteRenderer sprite;

		private void Awake()
		{
			sprite = GetComponentInChildren<SpriteRenderer>();
		}

		private void LateUpdate()
		{
			bool isOverUI = interactor.IsOverUIGameObject();

			sprite.enabled = isOverUI;

			if(isOverUI)
			{	
				interactor.TryGetHitInfo(out Vector3 hitPos, out Vector3 hitNorm, out int posInLine, out bool isValid);
				transform.position = hitPos;

				transform.rotation = Quaternion.LookRotation(-hitNorm, transform.parent.up);
			}
		}
	}
}
