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
			OVRManager.display.RecenteredPose += Align;
		}

		private void OnDisable()
		{
			Application.focusChanged -= OnFocusChange;
			OVRManager.display.RecenteredPose -= Align;
		}

		private void OnFocusChange(bool b)
		{
			if (b)
				Align();
		}

		public void SetTargetAndAlign(Matrix4x4 mat)
		{
			target = mat;
			Align();
		}

		public async void Align()
		{
			if (!XRSettings.enabled) return;

			await anchor.WhenLocalizedAsync();
			await Awaitable.EndOfFrameAsync();

			var currentMat = transform.localToWorldMatrix;
			MainXRRig.Instance.AlignSpace(currentMat, target);
			Aligned.Invoke();
		}
	}
}