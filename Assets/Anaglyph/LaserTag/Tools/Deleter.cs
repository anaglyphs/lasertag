using Anaglyph.XRTemplate;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	[RequireComponent(typeof(Raycaster))]
	public class Deleter : MonoBehaviour
	{
		private Raycaster raycaster;
		private LineRenderer lineRenderer;
		[SerializeField] private ObjectBoundsVisual boundsVisual;
		[SerializeField] private RaycasterCursor cursor;

		private Deletable hoveredDeletable;

		private HandedHierarchy handedness;

		private void Awake()
		{
			handedness = GetComponentInParent<HandedHierarchy>(true);

			TryGetComponent(out raycaster);
			TryGetComponent(out lineRenderer);


			lineRenderer.SetPositions(new[] { Vector3.zero, Vector3.zero });
			lineRenderer.useWorldSpace = false;


			raycaster.OnHitObjectChange += HandleHitObjectChange;
		}

		private void Update()
		{
			bool overUI = handedness.RayInteractor.IsOverUIGameObject();
			cursor.gameObject.SetActive(!overUI);
			lineRenderer.enabled = !overUI;

			if (overUI) return;


			bool didHit = raycaster.Raycast();

			cursor.gameObject.SetActive(didHit);
			if (didHit)
				lineRenderer.SetPosition(1, Vector3.forward * raycaster.Result.distance);
			else
				lineRenderer.SetPosition(1, Vector3.forward);
		}

		private void HandleHitObjectChange(GameObject newObj, GameObject prev)
		{
			var hoveredDeletable = newObj?.GetComponentInParent<Deletable>();
			boundsVisual.SetTrackedObject(newObj);
		}

		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				hoveredDeletable.Delete();
		}
	}
}