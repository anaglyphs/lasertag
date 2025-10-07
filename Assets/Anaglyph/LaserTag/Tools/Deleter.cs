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

		private Deletable hoveredDeletable;

		private void Awake()
		{
			lineRenderer.SetPositions(new[] { Vector3.zero, Vector3.zero });
			lineRenderer.useWorldSpace = false;
		}

		private void LateUpdate()
		{
			Ray ray = new(transform.position, transform.forward);
			bool didHit = Physics.Raycast(ray, out RaycastHit hitInfo);

			hoveredDeletable = didHit ? hitInfo.collider.GetComponentInParent<Deletable>() : null;

			lineRenderer.SetPosition(1, Vector3.forward * (didHit ? hitInfo.distance : 1f));

			if (didHit)
			{
				boundsVisual.enabled = true;
				boundsVisual.SetTrackedObject(hoveredDeletable);

				cursor.position = hitInfo.point;
				cursor.gameObject.SetActive(true);
			}
			else
			{
				boundsVisual.enabled = false;
				cursor.gameObject.SetActive(false);

				return;
			}
		}

		private void OnEnable()
		{
			lineRenderer.enabled = true;
		}

		private void OnDisable()
		{
			lineRenderer.enabled = false;
			boundsVisual.enabled = false;
			cursor.gameObject.SetActive(false);
		}

		private void OnFire(InputAction.CallbackContext context)
		{
			if (!enabled)
				return;

			if (context.performed && context.ReadValueAsButton())
				hoveredDeletable?.Delete();
		}
	}
}