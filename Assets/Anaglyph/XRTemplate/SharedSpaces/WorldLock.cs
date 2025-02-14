using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	/// <summary>
	/// Transforms VR playspace so that the anchor matches its networked position
	/// </summary>
	[DefaultExecutionOrder(500)]
	public class WorldLock : MonoBehaviour
	{
		private static IAnchor _lockedTo;
		public static event Action<IAnchor> LockedToChanged;
		public static IAnchor ActiveLock
		{
			get => _lockedTo;
			set
			{
				bool changed = value != _lockedTo;
				_lockedTo = value;
				if (changed) 
					LockedToChanged?.Invoke(_lockedTo);
			}
		}

		private IAnchor anchor;
		[SerializeField] private float maxLockDistance = 3;

		[RuntimeInitializeOnLoadMethod]
		private static void OnInit()
		{
			OVRManager.display.RecenteredPose += HandleRecenter;

			Application.quitting += delegate
			{
				OVRManager.display.RecenteredPose -= HandleRecenter;
			};
		}

		private static async void HandleRecenter()
		{
			await Awaitable.EndOfFrameAsync();

			LockOnto(ActiveLock);
		}

		private void Awake()
		{
			TryGetComponent(out anchor);
		}

		private void OnDestroy()
		{
			if(ActiveLock == anchor)
				ActiveLock = null;
		}

        private void LateUpdate()
        {
			if (ActiveLock == anchor)
				return;

			Vector3 camPosition = MainXROrigin.Instance.Camera.transform.position;
			float distanceFromOrigin = Vector3.Distance(anchor.TrackedPose.position, camPosition);

			if (distanceFromOrigin < maxLockDistance || ActiveLock == null)
				LockToAnchor(anchor);
		}

		public static void LockToAnchor(IAnchor anchor)
		{
			if (!anchor.Anchored)
				return;

			ActiveLock = anchor;

			LockOnto(anchor);
		}

		private static void LockOnto(IAnchor anchor)
		{
			if (anchor == null || !anchor.Anchored)
				return;

			Colocation.TransformTrackingSpace(anchor.TrackedPose, anchor.AnchoredPose);
		}
	}
}