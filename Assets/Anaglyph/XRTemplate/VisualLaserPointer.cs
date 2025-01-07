using UnityEngine;
using UnityEngine.Assertions;

namespace Anaglyph.XRTemplate
{
	[RequireComponent(typeof(LineRenderer))]

	[DefaultExecutionOrder(32000)]
	public class LineRendererLaser : MonoBehaviour
	{
		public Vector3 startOffset = new Vector3(0, 0, 0.05f);
		[SerializeField] private LineRenderer lineRenderer = null;

		private Vector3 localEndPos;

		[SerializeField] private Raycaster casterSubscribed = null;

		private bool setForFrame;

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

		private void LateUpdate()
		{
			lineRenderer.SetPosition(1, localEndPos);
			lineRenderer.enabled = setForFrame;

			setForFrame = false;
			localEndPos = Vector3.forward;
		}

		private void OnCast()
		{
			localEndPos = Vector3.forward;
			setForFrame = true;
		}

		public void SetEndPositionForFrame(Vector3 worldPos)
		{
			localEndPos = transform.InverseTransformPoint(worldPos);
			setForFrame = true;
		}
	}
}