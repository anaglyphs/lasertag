using System;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	[RequireComponent(typeof(OVRSpatialAnchor))]
	public class WorldLockAnchor : MonoBehaviour
	{
		public event Action Aligned = delegate { };
		private OVRSpatialAnchor anchor;

		public Matrix4x4 target = Matrix4x4.identity;

		private void Awake()
		{
			anchor = GetComponent<OVRSpatialAnchor>();
		}

		private void OnEnable()
		{
			Application.focusChanged += OnFocusChange;

			if (XRSettings.enabled)
				OVRManager.display.RecenteredPose += Align;
		}

		private void OnDisable()
		{
			Application.focusChanged -= OnFocusChange;

			if (XRSettings.enabled)
				OVRManager.display.RecenteredPose -= Align;
		}

		private void OnFocusChange(bool b)
		{
			Align();
		}

		public void SetTargetAndAlign(Matrix4x4 mat)
		{
			target = mat;
			Align();
		}

		public async void Align()
		{
			if (!XRSettings.enabled)
			{
				Aligned.Invoke();
				return;
			}

			await anchor.WhenLocalizedAsync();
			await Awaitable.EndOfFrameAsync();

			// This function is normally private in Meta's stock Core SDK, 
			// Made public to fix pose update timing issue when using ARFoundation 
			anchor.UpdateTransform();

			Matrix4x4 currentMat = transform.localToWorldMatrix;
			MainXRRig.Instance.AlignSpace(currentMat, target);
			Aligned.Invoke();
		}
	}
}