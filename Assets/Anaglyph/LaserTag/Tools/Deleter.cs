using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	public class Deleter : MonoBehaviour
	{
		[SerializeField] private BoundsMesh boundsVisual;
		[SerializeField] private Transform cursor;
		[SerializeField] LineRenderer lineRenderer;
		
		private HandedHierarchy hand;

		private Deletable hoveredDeletable;

		private void Awake()
		{
			hand = GetComponentInParent<HandedHierarchy>(true);

			lineRenderer.SetPositions(new[] { Vector3.zero, Vector3.zero });
			lineRenderer.useWorldSpace = false;
		}

		private void Update()
		{
			lineRenderer.enabled = false;
			boundsVisual.enabled = false;
			cursor.gameObject.SetActive(false);

			lineRenderer.SetPosition(1, Vector3.forward);
			lineRenderer.enabled = true;

			Ray ray = new(transform.position, transform.forward);
			bool didHit = Physics.Raycast(ray, out RaycastHit hitInfo);
			hoveredDeletable = hitInfo.collider?.GetComponentInParent<Deletable>();
			
			if (!didHit || hoveredDeletable == null)
			{
				return;
			}

			boundsVisual.enabled = true;
			cursor.gameObject.SetActive(true);

			cursor.position = hitInfo.point;
			lineRenderer.SetPosition(1, Vector3.forward * hitInfo.distance);
			boundsVisual.SetTrackedObject(hoveredDeletable);
		}

		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				hoveredDeletable?.Delete();
		}
	}
}