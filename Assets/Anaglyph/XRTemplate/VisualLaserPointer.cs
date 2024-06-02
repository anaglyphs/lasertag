using UnityEngine;
using UnityEngine.Assertions;

namespace Anaglyph.XRTemplate
{
	[RequireComponent(typeof(LineRenderer))]

	[DefaultExecutionOrder(-100)]
	public class LineRendererLaser : MonoBehaviour
	{
		public Vector3 startOffset = new Vector3(0, 0, 0.05f);
		[SerializeField] private LineRenderer lineRenderer;

		private Vector3 localEndPos;

		[SerializeField] private Raycaster casterSubscribed;

		private void Awake()
		{
			Assert.IsNotNull(lineRenderer);

			SubscribeToRaycaster(casterSubscribed);
		}

		public void SubscribeToRaycaster(Raycaster raycaster)
		{
			if(casterSubscribed != null)
			{
				casterSubscribed.onHitPoint.RemoveListener(SetEndPositionForFrame);
				casterSubscribed.onCast.RemoveListener(OnCast);
			}

			if (raycaster == null)
				return;

			casterSubscribed = raycaster;

			casterSubscribed.onCast.AddListener(OnCast);
			casterSubscribed.onHitPoint.AddListener(SetEndPositionForFrame);
		}

		private void Start()
		{
			lineRenderer.positionCount = 2;
			lineRenderer.useWorldSpace = false;
			lineRenderer.SetPosition(0, startOffset);
		}

		private void Update()
		{
			lineRenderer.enabled = false;
		}

		private void OnCast()
		{
			lineRenderer.enabled = true;
			localEndPos = Vector3.forward;
		}

		public void SetEndPositionForFrame(Vector3 worldPos)
		{
			lineRenderer.enabled = true;
			localEndPos = transform.InverseTransformPoint(worldPos);
		}

		private void OnWillRenderObject()
		{
			lineRenderer.SetPosition(1, localEndPos);
			localEndPos = Vector3.forward;
		}
	}
}